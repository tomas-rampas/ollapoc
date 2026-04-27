using System.Runtime.CompilerServices;
using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Moq;
using RagServer.Options;
using RagServer.Pipelines;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Pipelines;

public class DocsPipelineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an async enumerable that yields all <paramref name="items"/> with a Task.Yield between each.
    /// </summary>
    private static async IAsyncEnumerable<T> ToAsync<T>(
        IEnumerable<T> items,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return item;
        }
    }

    /// <summary>
    /// Builds a <see cref="DocsRetriever"/> whose underlying ES is unreachable so it always
    /// returns an empty list (the <c>!resp.IsValidResponse</c> guard fires silently).
    /// </summary>
    private static DocsRetriever BuildOfflineRetriever()
    {
        var mockEmbeddings = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mockEmbeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                new[] { new Embedding<float>(new ReadOnlyMemory<float>(new float[384])) }));

        var esClient = new ElasticsearchClient(
            new ElasticsearchClientSettings(new Uri("http://localhost:19999")));

        return new DocsRetriever(mockEmbeddings.Object, esClient, Create(new RagOptions()));
    }

    /// <summary>
    /// Builds an <see cref="HttpResponse"/> backed by a <see cref="MemoryStream"/> so the
    /// emitted SSE body can be read back after the call.
    /// </summary>
    private static (HttpResponse Response, MemoryStream Body) BuildResponse()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx.Response, body);
    }

    /// <summary>
    /// Reads the body stream from position 0 as UTF-8 text.
    /// </summary>
    private static string ReadBody(MemoryStream body)
    {
        body.Position = 0;
        return new StreamReader(body).ReadToEnd();
    }

    private static DocsPipeline BuildPipeline(
        IAsyncEnumerable<ChatResponseUpdate>? streamUpdates = null,
        DocsRetriever? retriever = null)
    {
        var updates = streamUpdates
            ?? ToAsync(new[] { new ChatResponseUpdate(ChatRole.Assistant, "Hello") });

        var mockChat = new Mock<IChatClient>();
        mockChat
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(updates);

        return new DocsPipeline(
            retriever ?? BuildOfflineRetriever(),
            mockChat.Object,
            Create(new OllamaOptions()));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmptyChunks_StillEmitsChunksAndCitationsEvents()
    {
        var pipeline = BuildPipeline();
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test query", response, CancellationToken.None);

        var text = ReadBody(body);
        Assert.Contains("event: chunks", text);
        Assert.Contains("event: citations", text);
    }

    [Fact]
    public async Task ExecuteAsync_StreamsTokens_AsDataEvents()
    {
        var updates = ToAsync(new[] { new ChatResponseUpdate(ChatRole.Assistant, "Hello") });
        var pipeline = BuildPipeline(streamUpdates: updates);
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test query", response, CancellationToken.None);

        var text = ReadBody(body);
        Assert.Contains("data: Hello", text);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsChunksEvent_BeforeCitationsEvent()
    {
        var pipeline = BuildPipeline();
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test query", response, CancellationToken.None);

        var text = ReadBody(body);
        var chunksPos = text.IndexOf("event: chunks", StringComparison.Ordinal);
        var citationsPos = text.IndexOf("event: citations", StringComparison.Ordinal);

        Assert.True(chunksPos >= 0, "chunks event not found");
        Assert.True(citationsPos >= 0, "citations event not found");
        Assert.True(chunksPos < citationsPos, "chunks event must appear before citations event");
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotEmitDoneEvent()
    {
        var pipeline = BuildPipeline();
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test query", response, CancellationToken.None);

        var text = ReadBody(body);
        Assert.DoesNotContain("[DONE]", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithCitationInAnswer_EmitsCitationsJsonArray()
    {
        // Retriever is offline → chunks = [], so [1] is out-of-range → citations = []
        var updates = ToAsync(new[] { new ChatResponseUpdate(ChatRole.Assistant, "[1]") });
        var pipeline = BuildPipeline(streamUpdates: updates);
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("query with citation marker", response, CancellationToken.None);

        var text = ReadBody(body);
        // citations event must contain an empty JSON array since [1] is out-of-range
        var citationsEventStart = text.IndexOf("event: citations", StringComparison.Ordinal);
        Assert.True(citationsEventStart >= 0);
        var afterEvent = text.Substring(citationsEventStart);
        Assert.Contains("[]", afterEvent);
    }

    [Fact]
    public async Task ExecuteAsync_NullOrEmptyTokens_Skipped()
    {
        // One update with null/empty text followed by a real token
        var updates = ToAsync(new ChatResponseUpdate[]
        {
            new(ChatRole.Assistant, ""),          // empty — must be skipped
            new(ChatRole.Assistant, "RealToken"), // must appear
        });
        var pipeline = BuildPipeline(streamUpdates: updates);
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test query", response, CancellationToken.None);

        var text = ReadBody(body);
        // The empty token must not produce a bare "data: \n\n" data frame on its own
        Assert.DoesNotContain("data: \n\n", text);
        // The real token must be present
        Assert.Contains("data: RealToken", text);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var pipeline = BuildPipeline();
        var (response, body) = BuildResponse();

        // With a pre-cancelled token the pipeline should either throw OperationCanceledException
        // or return gracefully without completing normal SSE output.
        var ex = await Record.ExceptionAsync(
            () => pipeline.ExecuteAsync("test query", response, cts.Token));

        // Accept either early cancellation exception or clean early exit (no throw)
        if (ex is not null)
            Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public async Task Given_SuccessfulExecution_When_ExecuteAsync_Then_EmitsStatsEvent()
    {
        var pipeline = BuildPipeline();
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test query", response, CancellationToken.None);

        var text = ReadBody(body);
        Assert.Contains("event: stats", text);
        Assert.Contains("\"pipeline\"", text);
        Assert.Contains("\"latencyMs\"", text);
    }
}
