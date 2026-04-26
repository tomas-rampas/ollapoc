using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RagServer.Infrastructure.Catalog;
using RagServer.Options;
using RagServer.Pipelines;
using RagServer.Tools;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Pipelines;

public class MetadataPipelineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an in-memory <see cref="CatalogDbContext"/> seeded with the model's HasData.
    /// </summary>
    private static CatalogDbContext BuildDb()
    {
        var dbOpts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"meta-pipeline-{Guid.NewGuid()}")
            .Options;
        var db = new CatalogDbContext(dbOpts);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>
    /// Creates a <see cref="CatalogTools"/> instance wired to an offline ES node and a null Mongo repo.
    /// </summary>
    private static CatalogTools BuildCatalogTools(CatalogDbContext? db = null)
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

        return new CatalogTools(
            db ?? BuildDb(),
            mockEmbeddings.Object,
            esClient,
            new NullMongoExtensionRepository(),
            Create(new RagOptions()),
            NullLogger<CatalogTools>.Instance);
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

    /// <summary>
    /// Builds a <see cref="MetadataPipeline"/> whose chat client returns the supplied
    /// <see cref="ChatResponse"/> on every call and whose catalog tools are fully wired.
    /// </summary>
    private static (MetadataPipeline Pipeline, Mock<IChatClient> ChatMock)
        BuildPipeline(ChatResponse? response = null, RagOptions? ragOpts = null)
    {
        var directAnswer = response
            ?? new ChatResponse(new ChatMessage(ChatRole.Assistant, "Direct answer."));

        var chatMock = new Mock<IChatClient>();
        chatMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(directAnswer);

        var pipeline = new MetadataPipeline(
            BuildCatalogTools(),
            chatMock.Object,
            Create(ragOpts ?? new RagOptions()),
            NullLogger<MetadataPipeline>.Instance,
            Create(new OllamaOptions()));

        return (pipeline, chatMock);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the chat client returns a direct answer with no tool calls, the pipeline should
    /// stream that answer text as SSE data frames.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoToolCalls_EmitsAnswerText()
    {
        const string answerText = "The Trade entity has 7 attributes.";
        var (pipeline, _) = BuildPipeline(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, answerText)));
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("What attributes does Trade have?", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains(answerText, output);
    }

    /// <summary>
    /// The <c>event: tools_used</c> SSE frame must always be emitted, even on the no-tool-call path.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_Always_EmitsToolsUsedEvent()
    {
        var (pipeline, _) = BuildPipeline();
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test query", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("event: tools_used", output);
    }

    /// <summary>
    /// When no tool calls were made, <c>tools_used</c> data must be the empty JSON array <c>[]</c>.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NoToolCalls_ToolsUsedIsEmptyArray()
    {
        var (pipeline, _) = BuildPipeline();
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test query", response, CancellationToken.None);

        var output = ReadBody(body);
        // The tools_used event frame must contain "[]"
        var eventIdx = output.IndexOf("event: tools_used", StringComparison.Ordinal);
        Assert.True(eventIdx >= 0, "tools_used event not found");
        var afterEvent = output[eventIdx..];
        Assert.Contains("[]", afterEvent);
    }

    /// <summary>
    /// When the chat client returns an empty message list, the pipeline should not throw.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithNoAnswer_DoesNotThrow()
    {
        // Return a ChatResponse with an empty messages list
        var emptyResponse = new ChatResponse(new List<ChatMessage>());
        var (pipeline, _) = BuildPipeline(emptyResponse);
        var (response, body) = BuildResponse();

        var ex = await Record.ExceptionAsync(
            () => pipeline.ExecuteAsync("query", response, CancellationToken.None));

        Assert.Null(ex);
    }

    /// <summary>
    /// With MetadataMaxTurns=1 and the chat client always returning a FunctionCallContent
    /// (simulated by returning a response whose single assistant message contains no text),
    /// the loop must terminate after exactly 1 turn instead of looping infinitely.
    /// We verify this by ensuring GetResponseAsync was called exactly once and the pipeline returns.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_MaxTurnsReached_LoopTerminates()
    {
        // Build a response that carries a FunctionCallContent so calls.Count > 0 each turn.
        // The FunctionCallContent refers to a valid tool name so dispatch succeeds silently.
        var functionCallMsg = new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("call-001", "ResolveEntityAsync",
                new Dictionary<string, object?> { ["name"] = "Trade" })
        ]);
        var responseWithCall = new ChatResponse(functionCallMsg);

        var chatMock = new Mock<IChatClient>();
        chatMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseWithCall);

        var pipeline = new MetadataPipeline(
            BuildCatalogTools(),
            chatMock.Object,
            Create(new RagOptions { MetadataMaxTurns = 1 }),
            NullLogger<MetadataPipeline>.Instance,
            Create(new OllamaOptions()));

        var (response, body) = BuildResponse();

        // Must terminate (not loop) and not throw
        var ex = await Record.ExceptionAsync(
            () => pipeline.ExecuteAsync("resolve trade", response, CancellationToken.None));

        Assert.Null(ex);

        // With MaxTurns=1 the loop body runs once, dispatches tool calls, then exits
        chatMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The first message sent to GetResponseAsync must be a system-role message.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CallsChatClient_WithSystemPrompt()
    {
        IEnumerable<ChatMessage>? capturedMessages = null;

        var chatMock = new Mock<IChatClient>();
        chatMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<ChatMessage> msgs, ChatOptions? _, CancellationToken _) =>
            {
                capturedMessages ??= msgs.ToList();
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var pipeline = new MetadataPipeline(
            BuildCatalogTools(),
            chatMock.Object,
            Create(new RagOptions()),
            NullLogger<MetadataPipeline>.Instance,
            Create(new OllamaOptions()));

        var (response, body) = BuildResponse();
        await pipeline.ExecuteAsync("any query", response, CancellationToken.None);

        Assert.NotNull(capturedMessages);
        var first = capturedMessages!.First();
        Assert.Equal(ChatRole.System, first.Role);
    }

    /// <summary>
    /// The message list sent to GetResponseAsync must contain the user's query text.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CallsChatClient_WithUserQuery()
    {
        const string testQuery = "List all CDEs for Trade";
        IEnumerable<ChatMessage>? capturedMessages = null;

        var chatMock = new Mock<IChatClient>();
        chatMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<ChatMessage> msgs, ChatOptions? _, CancellationToken _) =>
            {
                capturedMessages ??= msgs.ToList();
            })
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var pipeline = new MetadataPipeline(
            BuildCatalogTools(),
            chatMock.Object,
            Create(new RagOptions()),
            NullLogger<MetadataPipeline>.Instance,
            Create(new OllamaOptions()));

        var (response, body) = BuildResponse();
        await pipeline.ExecuteAsync(testQuery, response, CancellationToken.None);

        Assert.NotNull(capturedMessages);
        var userMessage = capturedMessages!.FirstOrDefault(m => m.Role == ChatRole.User);
        Assert.NotNull(userMessage);
        Assert.Contains(testQuery, userMessage!.Text ?? "");
    }

    /// <summary>
    /// Every SSE data frame produced by the pipeline must start with the "data:" prefix.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SseOutput_ContainsDataPrefix()
    {
        var (pipeline, _) = BuildPipeline(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Some answer text")));
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("data:", output);
    }

    /// <summary>
    /// Multi-line answer text should be encoded so each line begins with "data: "
    /// (the SSE continuation format), not a bare newline that would terminate the event.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_MultiLineAnswer_SseEncodesNewlines()
    {
        const string multiLineAnswer = "Line 1\nLine 2\nLine 3";
        var (pipeline, _) = BuildPipeline(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, multiLineAnswer)));
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test", response, CancellationToken.None);

        var output = ReadBody(body);
        // After SSE encoding, bare "\n" must not appear between the data lines —
        // each continuation must be "data: "
        Assert.Contains("data: Line 2", output);
        Assert.Contains("data: Line 3", output);
    }

    /// <summary>
    /// The <c>tools_used</c> SSE event must appear after any answer data frames.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ToolsUsedEvent_AppearsAfterAnswerData()
    {
        var (pipeline, _) = BuildPipeline(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "The answer is 42.")));
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test", response, CancellationToken.None);

        var output = ReadBody(body);
        var dataIdx = output.IndexOf("data: The answer", StringComparison.Ordinal);
        var eventIdx = output.IndexOf("event: tools_used", StringComparison.Ordinal);

        Assert.True(dataIdx >= 0, "answer data frame not found");
        Assert.True(eventIdx >= 0, "tools_used event not found");
        Assert.True(dataIdx < eventIdx, "answer data must appear before tools_used event");
    }

    [Fact]
    public async Task Given_SuccessfulExecution_When_ExecuteAsync_Then_EmitsStatsEventAfterToolsUsed()
    {
        var (pipeline, _) = BuildPipeline(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("test", response, CancellationToken.None);

        var output = ReadBody(body);
        var toolsIdx = output.IndexOf("event: tools_used", StringComparison.Ordinal);
        var statsIdx = output.IndexOf("event: stats", StringComparison.Ordinal);

        Assert.True(statsIdx >= 0, "stats event not found");
        Assert.True(toolsIdx < statsIdx, "tools_used event must appear before stats event");
        Assert.Contains("\"pipeline\"", output);
        Assert.Contains("\"latencyMs\"", output);
    }
}
