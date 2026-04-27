using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RagServer.Options;
using RagServer.Telemetry;

namespace RagServer.Pipelines;

/// <summary>
/// UC-1 Docs pipeline: hybrid ES retrieval → grounded Qwen3 prompt → streaming SSE answer with citations.
/// </summary>
public sealed class DocsPipeline(
    DocsRetriever retriever,
    [FromKeyedServices("chat")] IChatClient chatClient,
    IOptions<RagOptions> opts,
    IOptions<OllamaOptions> ollamaOpts)
{
    private static readonly JsonSerializerOptions StatsJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string SystemPrompt =
        "Answer ONLY from the source passages provided. " +
        "Cite each passage you use with [n] where n is its number. " +
        "Do not invent information not in the passages.";

    public async Task ExecuteAsync(string query, HttpResponse response, CancellationToken ct)
    {
        using var activity = RagActivitySource.Source.StartActivity("rag.docs_pipeline");
        var sw = Stopwatch.StartNew();

        var chunks = await retriever.RetrieveAsync(query, ct);
        activity?.SetTag("rag.chunks_retrieved", chunks.Count);

        // Build numbered passage block
        var passagesText = string.Join("\n\n", chunks.Select((chunk, i) =>
            $"[{i + 1}] (Source: {chunk.SourceType} — {chunk.Title})\n{chunk.Content}"));

        var userContent = string.IsNullOrWhiteSpace(passagesText)
            ? query
            : $"{passagesText}\n\nQuestion: {query}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User,   userContent)
        };

        var chatOpts = new ChatOptions
        {
            MaxOutputTokens = opts.Value.MaxOutputTokens
        };

        // Stream answer tokens as SSE data events, stripping any <think>…</think> block
        var answerBuilder = new System.Text.StringBuilder();
        var thinkStripper = new ThinkStripper();
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOpts, ct))
        {
            var text = update.Text;
            if (string.IsNullOrEmpty(text))
                continue;

            answerBuilder.Append(text);
            var toEmit = thinkStripper.Process(text);
            if (string.IsNullOrEmpty(toEmit))
                continue;

            await response.WriteAsync($"data: {EscapeSse(toEmit)}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }

        var answer = answerBuilder.ToString();

        // Emit chunk metadata event
        var chunkMeta = chunks.Select((chunk, i) => new
        {
            index      = i + 1,
            title      = chunk.Title,
            url        = chunk.Url,
            sourceType = chunk.SourceType
        });
        var chunksJson = JsonSerializer.Serialize(chunkMeta);
        await response.WriteAsync($"event: chunks\ndata: {chunksJson}\n\n", ct);
        await response.Body.FlushAsync(ct);

        // Emit citations event
        var citations = CitationExtractor.Extract(answer, chunks);
        activity?.SetTag("rag.citations_count", citations.Count);

        var citationsJson = JsonSerializer.Serialize(citations.Select(c => new
        {
            index = c.Index,
            url   = c.Url,
            title = c.Title
        }));
        await response.WriteAsync($"event: citations\ndata: {citationsJson}\n\n", ct);
        await response.Body.FlushAsync(ct);

        // Emit stats event
        sw.Stop();
        var tokenCount = answer.Length > 0 ? Math.Max(1, answer.Length / 4) : (int?)null;
        var latencyMs  = sw.ElapsedMilliseconds;
        var tps        = (latencyMs > 0 && tokenCount.HasValue)
            ? Math.Round((double)tokenCount.Value / latencyMs * 1000.0, 1) : (double?)null;

        RagMetrics.RequestDurationMs.Record(latencyMs,
            new KeyValuePair<string, object?>("pipeline", "Docs"),
            new KeyValuePair<string, object?>("model", ollamaOpts.Value.ChatModel));
        if (tps.HasValue)
            RagMetrics.TokensPerSecond.Record((float)tps.Value,
                new KeyValuePair<string, object?>("pipeline", "Docs"));

        var stats = new PipelineResult(
            Pipeline:       "Docs",
            LatencyMs:      latencyMs,
            ModelName:      ollamaOpts.Value.ChatModel,
            TokensGenerated: tokenCount,
            TokensPerSecond: tps);

        var statsJson = JsonSerializer.Serialize(stats, StatsJsonOpts);
        await response.WriteAsync($"event: stats\ndata: {statsJson}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static string EscapeSse(string text)
    {
        // Normalise all line endings to LF, then encode for SSE multi-line data
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return text.Replace("\n", "\ndata: ");
    }

    // Buffers tokens until the Qwen3 <think>…</think> chain-of-thought block ends,
    // then passes subsequent tokens through unchanged.
    private sealed class ThinkStripper
    {
        private readonly System.Text.StringBuilder _buf = new();
        private bool _inThink;
        private bool _done;

        public string? Process(string token)
        {
            if (_done) return token;

            _buf.Append(token);
            var s = _buf.ToString();

            if (!_inThink)
            {
                if (string.IsNullOrWhiteSpace(s)) return null;
                if (s.TrimStart().StartsWith("<think>", StringComparison.OrdinalIgnoreCase))
                {
                    _inThink = true;
                }
                else
                {
                    _done = true;
                    _buf.Clear();
                    return s;
                }
            }

            var end = s.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (end < 0) return null;

            _done = true;
            var after = s[(end + 8)..].TrimStart('\n', '\r');
            _buf.Clear();
            return after.Length > 0 ? after : null;
        }
    }
}
