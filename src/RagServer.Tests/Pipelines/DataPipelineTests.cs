using System.Text;
using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RagServer.Compiler;
using RagServer.Infrastructure.Catalog;
using RagServer.Options;
using RagServer.Pipelines;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Pipelines;

public class DataPipelineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an in-memory <see cref="CatalogDbContext"/> seeded with the model's HasData.
    /// </summary>
    private static CatalogDbContext BuildDb()
    {
        var dbOpts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"data-pipeline-{Guid.NewGuid()}")
            .Options;
        var db = new CatalogDbContext(dbOpts);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>
    /// Creates an <see cref="ElasticsearchClient"/> backed by an <see cref="InMemoryRequestInvoker"/>
    /// that returns the same <paramref name="json"/> for every request.
    /// The <c>x-elastic-product: Elasticsearch</c> header is included to pass the product check.
    /// </summary>
    private static ElasticsearchClient BuildInMemoryEs(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            { "x-elastic-product", ["Elasticsearch"] }
        };
        var invoker = new InMemoryRequestInvoker(bytes, 200, headers: headers);
        return new ElasticsearchClient(new ElasticsearchClientSettings(invoker));
    }

    /// <summary>
    /// Creates an offline <see cref="ElasticsearchClient"/> that will fail if any request is made.
    /// </summary>
    private static ElasticsearchClient BuildOfflineEs() =>
        new(new ElasticsearchClientSettings(new Uri("http://localhost:19999")));

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
    /// Dual-purpose ES JSON that satisfies both <c>_validate/query</c> (reads <c>valid:true</c>)
    /// and <c>_search</c> (reads <c>hits.hits:[]</c>) in a single in-memory invoker.
    /// </summary>
    private const string DualPurposeEmptyJson =
        """{"valid":true,"hits":{"total":{"value":0,"relation":"eq"},"hits":[]}}""";

    /// <summary>
    /// A valid Trade QuerySpec JSON as returned by the model.
    /// </summary>
    private const string ValidTradeSpecJson =
        """{"Entity":"Trade","Filters":[],"TimeRange":null,"Sort":[],"Aggregations":[],"Limit":10}""";

    /// <summary>
    /// Builds a mock <see cref="IChatClient"/> that always returns the given text.
    /// </summary>
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

    /// <summary>
    /// Builds a <see cref="DataPipeline"/> with the supplied collaborators.
    /// </summary>
    private static DataPipeline BuildPipeline(
        IChatClient chatClient,
        ElasticsearchClient es,
        RagOptions? ragOpts = null)
    {
        return new DataPipeline(
            BuildDb(),
            chatClient,
            es,
            new IrToDslCompiler(),
            new QuerySpecValidator(),
            Create(ragOpts ?? new RagOptions()),
            NullLogger<DataPipeline>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the model returns non-JSON text the pipeline must report that it could not generate
    /// a valid query, without throwing.
    /// </summary>
    [Fact]
    public async Task Given_InvalidJson_When_Executed_Then_ReturnsErrorMessage()
    {
        var chatMock = BuildChatMock("not json at all");
        var pipeline = BuildPipeline(chatMock.Object, BuildOfflineEs());
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("show me trades", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("I could not generate", output);
    }

    /// <summary>
    /// When the model always returns a spec for an entity that is not in the catalog, the
    /// pipeline must retry exactly <c>DataMaxRetries</c> additional times (2 total) before
    /// falling back to the error message.
    /// </summary>
    [Fact]
    public async Task Given_InvalidSpec_When_FirstAttempt_Then_Retries()
    {
        // "Ghost" is not a seeded entity → validator always rejects
        const string ghostSpecJson =
            """{"Entity":"Ghost","Filters":[],"TimeRange":null,"Sort":[],"Aggregations":[],"Limit":null}""";

        var chatMock = BuildChatMock(ghostSpecJson);
        var pipeline = BuildPipeline(
            chatMock.Object,
            BuildOfflineEs(),
            new RagOptions { DataMaxRetries = 1 });  // 1 + 1 = 2 total attempts

        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("show me ghosts", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("I could not generate", output);

        // DataMaxRetries=1 → loop runs for attempt 0 and attempt 1 → 2 LLM calls total
        chatMock.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// When the model returns a valid IR for a known entity, the pipeline must emit an
    /// <c>event: query_spec</c> SSE frame before the results.
    /// </summary>
    [Fact]
    public async Task Given_ValidQuery_When_ModelReturnsValidIR_Then_EmitsQuerySpecEvent()
    {
        var chatMock = BuildChatMock(ValidTradeSpecJson);
        var pipeline = BuildPipeline(chatMock.Object, BuildInMemoryEs(DualPurposeEmptyJson));
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("show me trades", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("event: query_spec", output);
    }

    /// <summary>
    /// When the model wraps the JSON in a markdown code fence the pipeline must strip the fence
    /// and still successfully parse the spec, emitting <c>event: query_spec</c>.
    /// </summary>
    [Fact]
    public async Task Given_ValidQuery_When_ModelReturnsMarkdownFencedJson_Then_Parsed()
    {
        var fencedJson = $"```json\n{ValidTradeSpecJson}\n```";
        var chatMock = BuildChatMock(fencedJson);
        var pipeline = BuildPipeline(chatMock.Object, BuildInMemoryEs(DualPurposeEmptyJson));
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("show me trades", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("event: query_spec", output);
    }

    /// <summary>
    /// When ES returns non-empty hits the output must contain the query_spec event and must not
    /// contain the "I could not generate" fallback message.
    /// </summary>
    [Fact]
    public async Task Given_ValidSpec_When_EsSearchReturnsHits_Then_StreamsFormattedAnswer()
    {
        const string hitsJson =
            """
            {
              "valid":true,
              "hits":{
                "total":{"value":2,"relation":"eq"},
                "hits":[
                  {"_index":"trades","_id":"1","_source":{"Status":"FAILED","Notional":100000}},
                  {"_index":"trades","_id":"2","_source":{"Status":"FAILED","Notional":200000}}
                ]
              }
            }
            """;

        var chatMock = BuildChatMock(ValidTradeSpecJson);
        var pipeline = BuildPipeline(chatMock.Object, BuildInMemoryEs(hitsJson));
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("show me failed trades", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("event: query_spec", output);
        Assert.DoesNotContain("I could not generate", output);
    }

    /// <summary>
    /// When the search returns an empty hits array the pipeline must stream the "No results found"
    /// message.
    /// </summary>
    [Fact]
    public async Task Given_EmptyResults_When_Executed_Then_StreamsNoResultsMessage()
    {
        const string emptyHitsJson =
            """{"valid":true,"hits":{"total":{"value":0,"relation":"eq"},"hits":[]}}""";

        var chatMock = BuildChatMock(ValidTradeSpecJson);
        var pipeline = BuildPipeline(chatMock.Object, BuildInMemoryEs(emptyHitsJson));
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("show me trades", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("No results found", output);
    }

    /// <summary>
    /// A spec with an aggregation clause must pass validation, compile, and emit the
    /// <c>event: query_spec</c> SSE frame.
    /// </summary>
    [Fact]
    public async Task Given_AggregationSpec_When_EsResponds_Then_EmitsQuerySpecEvent()
    {
        const string aggSpecJson =
            """
            {
              "Entity":"Trade",
              "Filters":[],
              "TimeRange":null,
              "Sort":[],
              "Aggregations":[{"Type":"Count","Field":"Status","Name":"status_count"}],
              "Limit":null
            }
            """;

        var chatMock = BuildChatMock(aggSpecJson);
        var pipeline = BuildPipeline(chatMock.Object, BuildInMemoryEs(DualPurposeEmptyJson));
        var (response, body) = BuildResponse();

        await pipeline.ExecuteAsync("count trades by status", response, CancellationToken.None);

        var output = ReadBody(body);
        Assert.Contains("event: query_spec", output);
    }

    /// <summary>
    /// When the cancellation token is already cancelled before <c>ExecuteAsync</c> is called,
    /// the method must propagate the cancellation as an <see cref="OperationCanceledException"/>.
    /// </summary>
    [Fact]
    public async Task Given_CancellationRequested_When_Executing_Then_ThrowsOperationCancelled()
    {
        var chatMock = BuildChatMock(ValidTradeSpecJson);
        var pipeline = BuildPipeline(chatMock.Object, BuildOfflineEs());
        var (response, body) = BuildResponse();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.ExecuteAsync("show me trades", response, cts.Token));
    }
}
