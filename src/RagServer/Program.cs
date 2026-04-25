using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RagServer.Endpoints;
using RagServer.Infrastructure;
using RagServer.Infrastructure.Catalog;
using RagServer.Options;
using RagServer.Router;
using RagServer.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Configuration.AddEnvironmentVariables();

// SKIP_AUTH is only honoured in the Development environment — never in production
var isDevelopment = builder.Environment.IsDevelopment();
var skipAuth = isDevelopment &&
               builder.Configuration["SKIP_AUTH"]?.ToLowerInvariant() == "true";

if (skipAuth)
    builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Warning)
           .AddFilter("RagServer", LogLevel.Warning);

// ── Options ───────────────────────────────────────────────────────────────────
builder.Services.Configure<OllamaOptions>(o =>
{
    o.BaseUrl        = builder.Configuration["OLLAMA_BASE_URL"]        ?? o.BaseUrl;
    o.ChatModel      = builder.Configuration["OLLAMA_CHAT_MODEL"]      ?? o.ChatModel;
    o.EmbeddingModel = builder.Configuration["OLLAMA_EMBEDDING_MODEL"] ?? o.EmbeddingModel;
});

builder.Services.Configure<ElasticsearchOptions>(o =>
{
    o.Url      = builder.Configuration["ES_URL"]      ?? o.Url;
    o.Username = builder.Configuration["ES_USERNAME"] ?? o.Username;
    o.Password = builder.Configuration["ES_PASSWORD"] ?? o.Password;
});

// Read OIDC values once to avoid reading IConfiguration twice (used below in AddOpenIdConnect)
var oidcAuthority    = builder.Configuration["OIDC_AUTHORITY"]     ?? "";
var oidcClientId     = builder.Configuration["OIDC_CLIENT_ID"]     ?? "";
var oidcClientSecret = builder.Configuration["OIDC_CLIENT_SECRET"] ?? "";
var oidcCallbackPath = builder.Configuration["OIDC_CALLBACK_PATH"] ?? "/signin-oidc";

builder.Services.Configure<OidcOptions>(o =>
{
    o.Authority    = oidcAuthority;
    o.ClientId     = oidcClientId;
    o.ClientSecret = oidcClientSecret;
    o.CallbackPath = oidcCallbackPath;
});

builder.Services.Configure<RagOptions>(o =>
{
    if (int.TryParse(builder.Configuration["RAG_QUEUE_MAX_DEPTH"],         out var depth)) o.QueueMaxDepth    = depth;
    if (int.TryParse(builder.Configuration["RAG_EMBEDDING_CACHE_SIZE"],    out var cs))    o.EmbeddingCacheSize = cs;
    if (int.TryParse(builder.Configuration["RAG_CHAT_MAX_OUTPUT_TOKENS"],  out var mot))   o.MaxOutputTokens  = mot;
});

builder.Services.Configure<SqlServerOptions>(o =>
{
    o.ConnectionString = builder.Configuration["SQL_CONNECTION_STRING"];
});

// ── Ollama AI Clients (keyed by role) ─────────────────────────────────────────
// OllamaApiClient implements IChatClient and IEmbeddingGenerator<string, Embedding<float>> directly.
// Resolved via IOptions to keep a single source of truth for connection strings.
builder.Services.AddKeyedSingleton<IChatClient>("chat", (sp, _) =>
{
    var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    return (IChatClient)new OllamaApiClient(new Uri(opts.BaseUrl), opts.ChatModel);
});

builder.Services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>("embeddings", (sp, _) =>
{
    var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    return (IEmbeddingGenerator<string, Embedding<float>>)
        new OllamaApiClient(new Uri(opts.BaseUrl), opts.EmbeddingModel);
});

// ── Elasticsearch ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<ElasticsearchClient>(sp =>
{
    var opts     = sp.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
    var settings = new ElasticsearchClientSettings(new Uri(opts.Url))
        .Authentication(new BasicAuthentication(opts.Username, opts.Password));
    return new ElasticsearchClient(settings);
});

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource(RagActivitySource.Source.Name)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());

// ── Authentication ────────────────────────────────────────────────────────────
// Single AddAuthentication chain; DefaultChallengeScheme depends on SKIP_AUTH
var authBuilder = builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme          = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = skipAuth
            ? CookieAuthenticationDefaults.AuthenticationScheme
            : OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie();

if (!skipAuth)
{
    authBuilder.AddOpenIdConnect(o =>
    {
        o.Authority    = oidcAuthority;
        o.ClientId     = oidcClientId;
        o.ClientSecret = oidcClientSecret;
        o.CallbackPath = oidcCallbackPath;
        o.ResponseType = "code";
        o.SaveTokens   = true;
        o.Scope.Add("openid");
        o.Scope.Add("profile");
    });
}

builder.Services.AddAuthorization();

// ── EF Core ───────────────────────────────────────────────────────────────────
var connStr = builder.Configuration["SQL_CONNECTION_STRING"];
if (string.IsNullOrWhiteSpace(connStr))
    builder.Services.AddDbContextPool<CatalogDbContext>(o => o.UseInMemoryDatabase("dev"));
else
    builder.Services.AddDbContextPool<CatalogDbContext>(o => o.UseSqlServer(connStr));

// ── LLM Request Queue ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<LlmRequestQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LlmRequestQueue>());

// ── IntentRouter ──────────────────────────────────────────────────────────────
// Singleton so the in-process classification cache persists across requests
builder.Services.AddSingleton<IntentRouter>();

// ── Blazor / Razor Components ─────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Named HttpClient "rag": used by the Blazor Chat component for SSE requests.
// BaseAddress is resolved at request time via NavigationManager.BaseUri in the component.
builder.Services.AddHttpClient("rag");

var app = builder.Build();

// ── Startup warnings ──────────────────────────────────────────────────────────
var startupLogger = app.Logger;
if (skipAuth)
    startupLogger.LogWarning("SKIP_AUTH=true — authentication is disabled. Development only.");

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthEndpoints();

var chatRoute = app.MapPost("/api/chat", ChatEndpoint.Handle);
if (!skipAuth) chatRoute.RequireAuthorization();

app.MapRazorComponents<RagServer.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
