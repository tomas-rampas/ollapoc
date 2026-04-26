using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagServer.Compiler;
using RagServer.Infrastructure.Catalog;
using RagServer.Options;
using RagServer.Telemetry;

namespace RagServer.Pipelines;

/// <summary>
/// UC-3 Data Pipeline: NL → QuerySpec IR → IrToDslCompiler → ES validate → ES search → SSE.
/// One retry on parse failure, validation failure, or ES validation failure.
/// </summary>
public sealed class DataPipeline(
    CatalogDbContext db,
    [FromKeyedServices("chat")] IChatClient chatClient,
    ElasticsearchClient es,
    IrToDslCompiler compiler,
    QuerySpecValidator validator,
    IOptions<RagOptions> opts,
    ILogger<DataPipeline> logger,
    IOptions<OllamaOptions> ollamaOpts)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions StatsJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task ExecuteAsync(string query, HttpResponse response, CancellationToken ct)
    {
        using var activity = RagActivitySource.Source.StartActivity("rag.data_pipeline");
        var sw = Stopwatch.StartNew();

        // ── Schema context from CatalogDbContext ──────────────────────────────
        var entities = await db.CatalogEntities
            .Include(e => e.Attributes)
            .ToListAsync(ct);

        var knownEntities = entities
            .Select(e => e.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var schemaContext = BuildSchemaContext(entities);

        // ── Build prompt ──────────────────────────────────────────────────────
        var systemPrompt = BuildSystemPrompt(schemaContext);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, query)
        };

        var chatOpts = new ChatOptions { MaxOutputTokens = opts.Value.DataIrMaxTokens };
        var maxRetries = Math.Max(0, opts.Value.DataMaxRetries);

        QuerySpec? spec = null;
        SearchRequest? compiledRequest = null;
        string? lastError = null;
        bool? irValidFirstTry = null;

        // ── NL → IR loop (up to 1 + maxRetries attempts) ─────────────────────
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (lastError is not null)
            {
                messages.Add(new ChatMessage(ChatRole.User,
                    $"The previous QuerySpec was invalid: {lastError}. " +
                    "Please fix the JSON and return only the corrected QuerySpec object."));
            }

            ChatResponse irResponse;
            try
            {
                irResponse = await chatClient.GetResponseAsync(messages, chatOpts, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Chat client failed on attempt {Attempt}", attempt);
                lastError = "LLM call failed.";
                continue;
            }

            messages.AddRange(irResponse.Messages);

            var rawJson = ExtractJson(irResponse.Text ?? "");
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                lastError = "Response did not contain a JSON object.";
                continue;
            }

            try
            {
                spec = JsonSerializer.Deserialize<QuerySpec>(rawJson, JsonOpts);
            }
            catch (JsonException jex)
            {
                logger.LogWarning(jex, "Failed to parse QuerySpec JSON on attempt {Attempt}", attempt);
                lastError = $"JSON parse error: {jex.Message}";
                continue;
            }

            if (spec is null)
            {
                lastError = "Deserialized QuerySpec was null.";
                continue;
            }

            // Validate schema
            var validation = validator.Validate(spec, knownEntities);
            if (!validation.IsValid)
            {
                lastError = string.Join("; ", validation.Errors);
                logger.LogWarning("QuerySpec validation failed (attempt {Attempt}): {Errors}", attempt, lastError);
                spec = null;
                continue;
            }

            // Compile and ES-validate the query
            SearchRequest searchRequest;
            try
            {
                searchRequest = compiler.Compile(spec);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IrToDslCompiler failed on attempt {Attempt}", attempt);
                lastError = $"Compiler error: {ex.Message}";
                spec = null;
                continue;
            }

            var validateResp = await es.Indices.ValidateQueryAsync(
                new ValidateQueryRequest(searchRequest.Indices!)
                {
                    Query = searchRequest.Query
                }, ct);

            if (!validateResp.IsValidResponse || !validateResp.Valid)
            {
                lastError = "Generated query failed ES validation.";
                logger.LogWarning("ES validation failed on attempt {Attempt}", attempt);
                spec = null;
                continue;
            }

            // Spec is valid — break out and execute
            irValidFirstTry = attempt == 0;
            compiledRequest = searchRequest;
            break;
        }

        // ── Stream answer ─────────────────────────────────────────────────────
        if (spec is null)
        {
            sw.Stop();
            const string fallback = "I could not generate a valid data query for your question.";
            await response.WriteAsync($"data: {fallback}\n\n", ct);
            await response.Body.FlushAsync(ct);
            await EmitStatsAsync(response, sw.ElapsedMilliseconds, irValidFirstTry, totalResultRows: null, ct);
            return;
        }

        activity?.SetTag("rag.data.entity", spec.Entity);

        // Emit the query_spec metadata event
        var specJson = JsonSerializer.Serialize(spec, JsonOpts);
        await response.WriteAsync($"event: query_spec\ndata: {EscapeSse(specJson)}\n\n", ct);
        await response.Body.FlushAsync(ct);

        // Execute the search (reuse compiled request — no second Compile call)
        var esSw = Stopwatch.StartNew();
        var searchResp = await es.SearchAsync<JsonElement>(compiledRequest!, ct);
        esSw.Stop();
        RagMetrics.EsSearchDurationMs.Record(esSw.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("pipeline", "Data"));

        if (!searchResp.IsValidResponse)
        {
            sw.Stop();
            const string msg = "Search query returned an error.";
            await response.WriteAsync($"data: {msg}\n\n", ct);
            await response.Body.FlushAsync(ct);
            await EmitStatsAsync(response, sw.ElapsedMilliseconds, irValidFirstTry, totalResultRows: null, ct);
            return;
        }

        var totalRows = searchResp.Hits.Count;
        var formatted = FormatResults(searchResp, spec);
        await response.WriteAsync($"data: {EscapeSse(formatted)}\n\n", ct);
        await response.Body.FlushAsync(ct);

        sw.Stop();
        await EmitStatsAsync(response, sw.ElapsedMilliseconds, irValidFirstTry, totalResultRows: totalRows, ct);
    }

    private async Task EmitStatsAsync(
        HttpResponse response, long latencyMs, bool? irValidFirstTry, int? totalResultRows,
        CancellationToken ct)
    {
        RagMetrics.RequestDurationMs.Record(latencyMs,
            new KeyValuePair<string, object?>("pipeline", "Data"),
            new KeyValuePair<string, object?>("model", ollamaOpts.Value.ChatModel));
        if (irValidFirstTry == true)
            RagMetrics.IrFirstTrySuccess.Add(1,
                new KeyValuePair<string, object?>("model", ollamaOpts.Value.ChatModel));

        var stats = new PipelineResult(
            Pipeline:       "Data",
            LatencyMs:      latencyMs,
            ModelName:      ollamaOpts.Value.ChatModel,
            IrValidFirstTry: irValidFirstTry,
            TotalResultRows: totalResultRows);

        var statsJson = JsonSerializer.Serialize(stats, StatsJsonOpts);
        await response.WriteAsync($"event: stats\ndata: {statsJson}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildSchemaContext(
        IEnumerable<RagServer.Infrastructure.Catalog.Entities.CatalogEntity> entities)
    {
        var sb = new StringBuilder();
        foreach (var entity in entities)
        {
            sb.AppendLine($"Entity: {entity.Name}");
            foreach (var attr in entity.Attributes)
                sb.AppendLine($"  - {attr.Name} ({attr.DataType})");
        }
        return sb.ToString();
    }

    private static string BuildSystemPrompt(string schemaContext) =>
        $$"""
        You are a data query assistant. Convert the user's natural-language question into a QuerySpec JSON object.
        Return ONLY the JSON — no explanation, no markdown fences, no extra text.

        QuerySpec schema:
        {
          "Entity": "<entity name>",
          "Filters": [{ "Field": "<field>", "Operator": "<operator>", "Value": "<value or empty string for IsNull/IsNotNull>" }],
          "TimeRange": { "Field": "<date field>", "From": "<ISO date or null>", "To": "<ISO date or null>", "RelativePeriod": "<token or null>" } or null,
          "Sort": [{ "Field": "<field>", "Direction": "<Asc|Desc>" }],
          "Aggregations": [{ "Type": "<aggregation type>", "Field": "<field>", "Name": "<optional name or null>" }],
          "Limit": <integer or null>
        }

        Filter operators: Eq, Neq, Gt, Gte, Lt, Lte, Contains, In, NotIn, IsNull, IsNotNull
        - In/NotIn: comma-separated values in Value field (e.g. "ACTIVE,PENDING")
        - IsNull/IsNotNull: Value field is ignored — use ""

        Aggregation types: Count, Sum, Avg, Min, Max, Terms, Distinct, GroupBy

        TimeRange.RelativePeriod tokens (use instead of From/To when convenient):
          today, yesterday, last_7_days, last_30_days, this_month, this_year, last_year

        Available entities and fields:
        {{schemaContext}}

        Example:
        { "Entity": "Trade", "Filters": [{"Field": "Status", "Operator": "Eq", "Value": "ACTIVE"}], "TimeRange": {"Field": "TradeDate", "From": null, "To": null, "RelativePeriod": "last_30_days"}, "Sort": [{"Field": "TradeDate", "Direction": "Desc"}], "Aggregations": [], "Limit": 10 }
        """;

    private static string ExtractJson(string text)
    {
        // Strip optional markdown code fences
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) trimmed = trimmed[..lastFence];
            trimmed = trimmed.Trim();
        }

        // Find the first '{' and last '}'
        var start = trimmed.IndexOf('{');
        var end   = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : string.Empty;
    }

    private static string FormatResults(SearchResponse<JsonElement> resp, QuerySpec spec)
    {
        if (!resp.Hits.Any())
            return $"No results found for {spec.Entity}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {resp.Total} {spec.Entity} records:");
        sb.AppendLine();

        foreach (var hit in resp.Hits)
        {
            if (hit.Source is { } src)
                sb.AppendLine($"- {src.ToString()}");
        }

        if (resp.Aggregations?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Aggregations:");
            foreach (var kv in resp.Aggregations)
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string EscapeSse(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\ndata: ");
}
