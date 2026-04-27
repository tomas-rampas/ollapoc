using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RagServer.Compiler;
using RagServer.Infrastructure.Business;
using RagServer.Options;
using RagServer.Telemetry;

namespace RagServer.Pipelines;

/// <summary>
/// UC-3 SQL Data Pipeline: NL → QuerySpec IR → QuerySpecToSqlCompiler → SQL Server → SSE.
/// Mirrors <see cref="DataPipeline"/> structure but executes parameterized SQL instead of
/// Elasticsearch DSL, targeting the <see cref="BusinessDataContext"/> tables.
/// </summary>
public sealed class SqlDataPipeline(
    BusinessDataContext db,
    [FromKeyedServices("chat")] IChatClient chatClient,
    QuerySpecToSqlCompiler compiler,
    QuerySpecValidator validator,
    IOptions<RagOptions> opts,
    ILogger<SqlDataPipeline> logger,
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
        using var activity = RagActivitySource.Source.StartActivity("rag.sql_data_pipeline");
        var sw = Stopwatch.StartNew();

        // ── Schema context from static dictionary ─────────────────────────────
        var knownEntities = QuerySpecToSqlCompiler.Schema.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var schemaContext = BuildSchemaContext();

        // ── Build prompt ──────────────────────────────────────────────────────
        var systemPrompt = BuildSystemPrompt(schemaContext);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, query + " /no_think")
        };

        var chatOpts = new ChatOptions { MaxOutputTokens = opts.Value.DataIrMaxTokens };
        var maxRetries = Math.Max(0, opts.Value.DataMaxRetries);

        QuerySpec? spec = null;
        SqlQueryResult? compiledQuery = null;
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

            // Compile to SQL
            try
            {
                compiledQuery = compiler.Compile(spec);
            }
            catch (CompilerException ex)
            {
                logger.LogWarning(ex, "QuerySpecToSqlCompiler failed on attempt {Attempt}", attempt);
                lastError = $"Compiler error: {ex.Message}";
                spec = null;
                continue;
            }

            // Spec and query are valid — break
            irValidFirstTry = attempt == 0;
            break;
        }

        // ── Stream answer ─────────────────────────────────────────────────────
        if (spec is null || compiledQuery is null)
        {
            sw.Stop();
            const string fallback = "I could not generate a valid data query for your question.";
            await response.WriteAsync($"data: {fallback}\n\n", ct);
            await response.Body.FlushAsync(ct);
            await EmitStatsAsync(response, sw.ElapsedMilliseconds, irValidFirstTry, totalResultRows: null, ct);
            return;
        }

        activity?.SetTag("rag.data.entity", spec.Entity);

        // Emit query_spec metadata event
        var specJson = JsonSerializer.Serialize(spec, JsonOpts);
        await response.WriteAsync($"event: query_spec\ndata: {EscapeSse(specJson)}\n\n", ct);
        await response.Body.FlushAsync(ct);

        // Execute the SQL query
        var sqlSw = Stopwatch.StartNew();
        List<Dictionary<string, string>> rows;
        try
        {
            rows = await ExecuteSqlAsync(compiledQuery, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SQL execution failed");
            sw.Stop();
            const string msg = "SQL query returned an error.";
            await response.WriteAsync($"data: {msg}\n\n", ct);
            await response.Body.FlushAsync(ct);
            await EmitStatsAsync(response, sw.ElapsedMilliseconds, irValidFirstTry, totalResultRows: null, ct);
            return;
        }
        sqlSw.Stop();
        RagMetrics.EsSearchDurationMs.Record(sqlSw.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("pipeline", "SqlData"));

        var formatted = FormatResults(rows, spec);
        await response.WriteAsync($"data: {EscapeSse(formatted)}\n\n", ct);
        await response.Body.FlushAsync(ct);

        sw.Stop();
        await EmitStatsAsync(response, sw.ElapsedMilliseconds, irValidFirstTry, totalResultRows: rows.Count, ct);
    }

    // ── SQL execution ─────────────────────────────────────────────────────────

    private async Task<List<Dictionary<string, string>>> ExecuteSqlAsync(
        SqlQueryResult query, CancellationToken ct)
    {
        var results = new List<Dictionary<string, string>>();

        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = query.Sql;
        foreach (var p in query.Parameters)
            cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, string>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var colName = reader.GetName(i);
                var val = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
                row[colName] = val;
            }
            results.Add(row);
        }

        return results;
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    private async Task EmitStatsAsync(
        HttpResponse response, long latencyMs, bool? irValidFirstTry, int? totalResultRows,
        CancellationToken ct)
    {
        RagMetrics.RequestDurationMs.Record(latencyMs,
            new KeyValuePair<string, object?>("pipeline", "SqlData"),
            new KeyValuePair<string, object?>("model", ollamaOpts.Value.ChatModel));
        if (irValidFirstTry == true)
            RagMetrics.IrFirstTrySuccess.Add(1,
                new KeyValuePair<string, object?>("model", ollamaOpts.Value.ChatModel));

        var stats = new PipelineResult(
            Pipeline:       "SqlData",
            LatencyMs:      latencyMs,
            ModelName:      ollamaOpts.Value.ChatModel,
            IrValidFirstTry: irValidFirstTry,
            TotalResultRows: totalResultRows);

        var statsJson = JsonSerializer.Serialize(stats, StatsJsonOpts);
        await response.WriteAsync($"event: stats\ndata: {statsJson}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildSchemaContext()
    {
        var sb = new StringBuilder();
        foreach (var (entity, columns) in QuerySpecToSqlCompiler.Schema)
        {
            sb.AppendLine($"Entity: {entity}");
            foreach (var (col, dataType) in columns)
                sb.AppendLine($"  - {col} ({dataType})");
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
        { "Entity": "Counterparty", "Filters": [{"Field": "status", "Operator": "Eq", "Value": "Active"}], "TimeRange": null, "Sort": [{"Field": "legal_name", "Direction": "Asc"}], "Aggregations": [], "Limit": 10 }
        """;

    private static readonly System.Text.RegularExpressions.Regex ThinkPattern =
        new(@"<think>[\s\S]*?</think>\s*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string ExtractJson(string text)
    {
        text = ThinkPattern.Replace(text, "").TrimStart();
        var thinkIdx = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (thinkIdx >= 0) text = text[..thinkIdx];

        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) trimmed = trimmed[..lastFence];
            trimmed = trimmed.Trim();
        }

        var start = trimmed.IndexOf('{');
        var end   = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : string.Empty;
    }

    private static string FormatResults(List<Dictionary<string, string>> rows, QuerySpec spec)
    {
        if (rows.Count == 0)
            return $"No results found for {spec.Entity}.";

        var columns = rows[0].Keys.ToList();
        if (columns.Count == 0)
            return $"Found {rows.Count} {spec.Entity} records.";

        // Detect primary ID field for clickable links
        var entityLower = spec.Entity.ToLower();
        var idField = columns.FirstOrDefault(c => c.Equals($"{entityLower}_id", StringComparison.OrdinalIgnoreCase))
                      ?? columns.FirstOrDefault(c => c.Equals("id", StringComparison.OrdinalIgnoreCase))
                      ?? columns.FirstOrDefault(c => c.EndsWith("_id", StringComparison.OrdinalIgnoreCase));

        var sb = new StringBuilder();
        sb.AppendLine($"Found {rows.Count} **{spec.Entity}** records:");
        sb.AppendLine();

        // Table header
        sb.AppendLine("| " + string.Join(" | ", columns.Select(EscapeCell)) + " |");
        sb.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");

        // Table rows
        foreach (var row in rows)
        {
            var cells = columns.Select<string, string>(col =>
            {
                var raw = row.TryGetValue(col, out var v) ? v : "";
                raw = EscapeCell(raw);
                if (idField != null && col.Equals(idField, StringComparison.OrdinalIgnoreCase))
                    return $"[{raw}](/entity/{spec.Entity}/{raw})";
                return raw;
            });
            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
        }

        return sb.ToString().TrimEnd();
    }

    private static string EscapeCell(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");

    private static string EscapeSse(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\ndata: ");
}
