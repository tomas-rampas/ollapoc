using System.Diagnostics;
using System.Text.Json;
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
    IOptions<RagOptions> opts)
{
    private const string SystemPrompt =
        "Answer ONLY from the source passages provided. " +
        "Cite each passage you use with [n] where n is its number. " +
        "Do not invent information not in the passages.";

    public async Task ExecuteAsync(string query, HttpResponse response, CancellationToken ct)
    {
        using var activity = RagActivitySource.Source.StartActivity("rag.docs_pipeline");

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

        // Stream answer tokens as SSE data events
        var answerBuilder = new System.Text.StringBuilder();
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOpts, ct))
        {
            var text = update.Text;
            if (string.IsNullOrEmpty(text))
                continue;

            answerBuilder.Append(text);
            await response.WriteAsync($"data: {EscapeSse(text)}\n\n", ct);
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
    }

    private static string EscapeSse(string text)
    {
        // Normalise all line endings to LF, then encode for SSE multi-line data
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return text.Replace("\n", "\ndata: ");
    }
}
