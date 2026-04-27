using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RagServer.Compiler;
using RagServer.Infrastructure.Business;
using RagServer.Options;
using RagServer.Pipelines;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Pipelines;

public class SqlDataPipelineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an in-memory <see cref="BusinessDataContext"/> seeded with the model's HasData.
    /// </summary>
    private static BusinessDataContext BuildDb()
    {
        var dbOpts = new DbContextOptionsBuilder<BusinessDataContext>()
            .UseInMemoryDatabase($"sql-pipeline-{Guid.NewGuid()}")
            .Options;
        var db = new BusinessDataContext(dbOpts);
        db.Database.EnsureCreated();
        return db;
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

    private static string ReadBody(MemoryStream body)
    {
        body.Position = 0;
        return new StreamReader(body).ReadToEnd();
    }

    private static Mock<IChatClient> BuildChatMock(string returnText)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, returnText)));
        return mock;
    }

    private static SqlDataPipeline BuildPipeline(IChatClient chatClient, RagOptions? ragOpts = null) =>
        new(
            BuildDb(),
            chatClient,
            new QuerySpecToSqlCompiler(),
            new QuerySpecValidator(),
            Create(ragOpts ?? new RagOptions()),
            NullLogger<SqlDataPipeline>.Instance,
            Create(new OllamaOptions()));

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the model returns non-JSON text the pipeline must emit the fallback message.
    /// </summary>
    [Fact]
    public async Task Given_InvalidJson_When_Executed_Then_ReturnsErrorMessage()
    {
        var chatMock = BuildChatMock("not json at all");
        var pipeline = BuildPipeline(chatMock.Object);
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("show me counterparties", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("I could not generate", output);
    }

    /// <summary>
    /// When the model returns a valid spec for a known entity, the pipeline emits
    /// <c>event: query_spec</c> and the query result table (empty or not).
    /// </summary>
    [Fact]
    public async Task Given_ValidSpec_When_Executed_Then_EmitsQuerySpecEventAndStats()
    {
        const string specJson =
            """{"Entity":"Counterparty","Filters":[],"TimeRange":null,"Sort":[],"Aggregations":[],"Limit":5}""";

        var chatMock = BuildChatMock(specJson);
        var pipeline = BuildPipeline(chatMock.Object);
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("give me some counterparties", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("event: query_spec", output);
        Assert.Contains("event: stats", output);
        Assert.DoesNotContain("I could not generate", output);
    }

    /// <summary>
    /// The pipeline emits the query_spec event for a valid Counterparty spec.
    /// The in-memory EF provider does not support raw-SQL execution via GetDbConnection(),
    /// so the result may be empty — but the query_spec event and stats must always be present.
    /// </summary>
    [Fact]
    public async Task Given_ValidSpec_When_QueryAll_Then_EmitsQuerySpecAndStatsEvents()
    {
        const string specJson =
            """{"Entity":"Counterparty","Filters":[],"TimeRange":null,"Sort":[],"Aggregations":[],"Limit":null}""";

        var chatMock = BuildChatMock(specJson);
        var pipeline = BuildPipeline(chatMock.Object);
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("list all counterparties", response, CancellationToken.None);

        var output = ReadBody(body);
        // Must always emit the spec event and stats regardless of SQL execution result
        Assert.Contains("event: query_spec", output);
        Assert.Contains("event: stats", output);
        Assert.DoesNotContain("I could not generate", output);
    }

    /// <summary>
    /// A filter for entity "Location" with city = "London" produces a valid QuerySpec.
    /// The pipeline must emit query_spec and stats events for a valid spec regardless of
    /// SQL execution outcome (the InMemory provider does not support raw SQL).
    /// </summary>
    [Fact]
    public async Task Given_EqFilterOnLocation_When_Executed_Then_EmitsQuerySpecAndStats()
    {
        const string specJson =
            """{"Entity":"Location","Filters":[{"Field":"city","Operator":"Eq","Value":"London"}],"TimeRange":null,"Sort":[],"Aggregations":[],"Limit":null}""";

        var chatMock = BuildChatMock(specJson);
        var pipeline = BuildPipeline(chatMock.Object);
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("show me London locations", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("event: query_spec", output);
        Assert.Contains("event: stats", output);
        Assert.DoesNotContain("I could not generate", output);
    }

    /// <summary>
    /// A spec that references an entity not in the SQL schema is rejected by the validator;
    /// the pipeline must emit the fallback error after exhausting retries.
    /// </summary>
    [Fact]
    public async Task Given_SpecWithUnknownEntity_When_Executed_Then_ReturnsErrorAfterRetries()
    {
        const string ghostSpecJson =
            """{"Entity":"Trade","Filters":[],"TimeRange":null,"Sort":[],"Aggregations":[],"Limit":null}""";

        var chatMock = BuildChatMock(ghostSpecJson);
        var pipeline = BuildPipeline(chatMock.Object, new RagOptions { DataMaxRetries = 1 });
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("show me trades", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("I could not generate", output);

        // 1 retry → 2 total LLM calls
        chatMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// The stats event must contain pipeline = "SqlData", latencyMs, and modelName.
    /// </summary>
    [Fact]
    public async Task Given_ValidSpec_When_Executed_Then_StatsContainsSqlDataPipelineName()
    {
        const string specJson =
            """{"Entity":"Currency","Filters":[],"TimeRange":null,"Sort":[],"Aggregations":[],"Limit":null}""";

        var chatMock = BuildChatMock(specJson);
        var pipeline = BuildPipeline(chatMock.Object);
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("list all currencies", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("event: stats", output);
        Assert.Contains("\"pipeline\"", output);
        Assert.Contains("SqlData", output);
        Assert.Contains("\"latencyMs\"", output);
    }

    /// <summary>
    /// Cancellation before the LLM call must propagate as OperationCanceledException.
    /// </summary>
    [Fact]
    public async Task Given_CancellationRequested_When_Executing_Then_ThrowsOperationCancelled()
    {
        var chatMock = BuildChatMock("irrelevant");
        var pipeline = BuildPipeline(chatMock.Object);
        var (response, body) = BuildResponse();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.ExecuteAsync("show me counterparties", response, cts.Token));
    }

    /// <summary>
    /// Even a filter that could match nothing should still emit a valid query_spec event.
    /// The in-memory EF provider does not support raw-SQL execution via GetDbConnection(),
    /// so the pipeline will either return empty results or the SQL error message.
    /// Either way the query_spec event must be emitted first.
    /// </summary>
    [Fact]
    public async Task Given_FilterSpec_When_Executed_Then_EmitsQuerySpecEvent()
    {
        const string specJson =
            """{"Entity":"Country","Filters":[{"Field":"country_code","Operator":"Eq","Value":"ZZ"}],"TimeRange":null,"Sort":[],"Aggregations":[],"Limit":null}""";

        var chatMock = BuildChatMock(specJson);
        var pipeline = BuildPipeline(chatMock.Object);
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("get country ZZ", response, CancellationToken.None);

        var output = ReadBody(body);
        // query_spec must be emitted before attempting SQL execution
        Assert.Contains("event: query_spec", output);
        Assert.DoesNotContain("I could not generate", output);
    }

    /// <summary>
    /// irValidFirstTry must be true in the stats event when the model's first response is valid.
    /// </summary>
    [Fact]
    public async Task Given_ValidIrOnFirstAttempt_When_Executed_Then_StatsContainsIrFirstTryTrue()
    {
        const string specJson =
            """{"Entity":"Book","Filters":[],"TimeRange":null,"Sort":[],"Aggregations":[],"Limit":null}""";

        var chatMock = BuildChatMock(specJson);
        var pipeline = BuildPipeline(chatMock.Object);
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("list all books", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("\"irValidFirstTry\":true", output);
    }
}
