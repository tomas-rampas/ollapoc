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
using RagServer.Compiler;
using RagServer.Endpoints;
using RagServer.Infrastructure;
using RagServer.Infrastructure.Catalog;
using RagServer.Ingestion;
using RagServer.Options;
using RagServer.Pipelines;
using RagServer.Router;
using RagServer.Telemetry;
using RagServer.Tools;

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
    if (int.TryParse(builder.Configuration["RAG_METADATA_MAX_TURNS"],      out var mmt))   o.MetadataMaxTurns  = Math.Clamp(mmt, 1, 20);
    if (int.TryParse(builder.Configuration["RAG_CATALOG_TERMS_TOP_K"],     out var cttk))  o.CatalogTermsTopK  = cttk;
    if (int.TryParse(builder.Configuration["RAG_DATA_MAX_RETRIES"],        out var dmr))   o.DataMaxRetries    = dmr;
    if (int.TryParse(builder.Configuration["RAG_DATA_IR_MAX_TOKENS"],      out var dit))   o.DataIrMaxTokens   = dit;
});

builder.Services.Configure<SqlServerOptions>(o =>
{
    o.ConnectionString = builder.Configuration["SQL_CONNECTION_STRING"];
});

var mongoConnStr = builder.Configuration["MONGO_CONNECTION_STRING"];
builder.Services.Configure<MongoOptions>(o =>
{
    o.ConnectionString        = mongoConnStr;
    o.Database                = builder.Configuration["MONGO_DATABASE"]                ?? o.Database;
    o.ExtensionsCollection    = builder.Configuration["MONGO_EXTENSIONS_COLLECTION"]   ?? o.ExtensionsCollection;
});

builder.Services.Configure<ConfluenceOptions>(builder.Configuration.GetSection("Confluence"));
builder.Services.Configure<JiraOptions>(builder.Configuration.GetSection("Jira"));
builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection("Ingestion"));

var abTestEnabled = builder.Configuration["AB_TEST_ENABLED"]?.ToLowerInvariant() == "true";
builder.Services.Configure<AbTestOptions>(o =>
{
    o.ModelA  = builder.Configuration["AB_TEST_MODEL_A"] ?? o.ModelA;
    o.ModelB  = builder.Configuration["AB_TEST_MODEL_B"] ?? o.ModelB;
    o.Enabled = abTestEnabled;
});

// ── Ollama AI Clients (keyed by role) ─────────────────────────────────────────
// OllamaApiClient implements IChatClient and IEmbeddingGenerator<string, Embedding<float>> directly.
// Resolved via IOptions to keep a single source of truth for connection strings.

// AbTestChatClient wraps two model clients; when Enabled=false it passes through to model A.
builder.Services.AddSingleton<AbTestChatClient>(sp =>
{
    var abOpts     = sp.GetRequiredService<IOptions<AbTestOptions>>().Value;
    var ollamaOpts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    var clientA    = (IChatClient)new OllamaApiClient(new Uri(ollamaOpts.BaseUrl), abOpts.ModelA);
    var clientB    = (IChatClient)new OllamaApiClient(new Uri(ollamaOpts.BaseUrl), abOpts.ModelB);
    return new AbTestChatClient(clientA, clientB, sp.GetRequiredService<IOptions<AbTestOptions>>());
});

builder.Services.AddKeyedSingleton<IChatClient>("chat", (sp, _) =>
    abTestEnabled
        ? (IChatClient)sp.GetRequiredService<AbTestChatClient>()
        : (IChatClient)new OllamaApiClient(
            new Uri(sp.GetRequiredService<IOptions<OllamaOptions>>().Value.BaseUrl),
            sp.GetRequiredService<IOptions<OllamaOptions>>().Value.ChatModel));

builder.Services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>("embeddings", (sp, _) =>
{
    var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    return (IEmbeddingGenerator<string, Embedding<float>>)
        new OllamaApiClient(new Uri(opts.BaseUrl), opts.EmbeddingModel);
});

// Unkeyed IEmbeddingGenerator resolves to EmbeddingCache — wraps the keyed "embeddings" with LRU
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, EmbeddingCache>();

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
        .AddMeter("RagServer")
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

// ── MongoDB (optional — graceful degradation when MONGO_CONNECTION_STRING is absent) ────
if (string.IsNullOrWhiteSpace(mongoConnStr))
    builder.Services.AddSingleton<IMongoExtensionRepository, NullMongoExtensionRepository>();
else
    builder.Services.AddSingleton<IMongoExtensionRepository>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
        return new MongoExtensionRepository(opts);
    });

// ── Metadata pipeline (UC-2) ─────────────────────────────────────────────────────────────
// CatalogTools is Scoped because it captures CatalogDbContext (Scoped).
// MetadataPipeline is Scoped for the same reason.
builder.Services.AddScoped<CatalogTools>();
builder.Services.AddScoped<MetadataPipeline>();

// ── Data pipeline (UC-3) ─────────────────────────────────────────────────────────────────
// QuerySpecValidator and IrToDslCompiler are pure/stateless — Singleton.
// DataPipeline captures CatalogDbContext (Scoped) — must be Scoped.
builder.Services.AddSingleton<QuerySpecValidator>();
builder.Services.AddSingleton<IrToDslCompiler>();
builder.Services.AddScoped<DataPipeline>();

// ── LLM Request Queue ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<LlmRequestQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LlmRequestQueue>());

// ── IntentRouter ──────────────────────────────────────────────────────────────
// Singleton so the in-process classification cache persists across requests
builder.Services.AddSingleton<IntentRouter>();

// ── Ingestion ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<ConfluenceContentNormaliser>();
builder.Services.AddSingleton<AdfNormaliser>();
builder.Services.AddHttpClient<IConfluenceCrawler, ConfluenceCrawler>();
builder.Services.AddHttpClient<IJiraCrawler, JiraCrawler>();
builder.Services.AddSingleton<DocumentEmbedder>();
builder.Services.AddHostedService<IngestionScheduler>();

// ── Docs pipeline ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<DocsRetriever>();
builder.Services.AddSingleton<DocsPipeline>();

// ── Index bootstrap ───────────────────────────────────────────────────────────
builder.Services.AddHostedService<IndexBootstrapper>();
builder.Services.AddHostedService<CatalogIndexBootstrapper>();

// ── Blazor / Razor Components ─────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Named HttpClient "rag": used by the Blazor Chat component for SSE requests.
// BaseAddress is resolved at request time via NavigationManager.BaseUri in the component.
builder.Services.AddHttpClient("rag");

var app = builder.Build();

// ── OTel queue-depth gauge (registered after Build() so app.Services is available) ──────────
RagMetrics.Meter.CreateObservableGauge("rag.queue_depth",
    () => app.Services.GetRequiredService<LlmRequestQueue>().CurrentDepth);

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

app.MapAdminEndpoints();

app.MapRazorComponents<RagServer.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
