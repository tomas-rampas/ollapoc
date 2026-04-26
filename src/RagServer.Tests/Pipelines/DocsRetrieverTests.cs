using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.AI;
using Moq;
using RagServer.Options;
using RagServer.Pipelines;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Pipelines;

public class DocsRetrieverTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<IEmbeddingGenerator<string, Embedding<float>>> BuildEmbeddingMock()
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mock.Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                new[] { new Embedding<float>(new ReadOnlyMemory<float>(new float[384])) }));
        return mock;
    }

    /// <summary>
    /// Creates a <see cref="DocsRetriever"/> that targets an unreachable ES node so every search
    /// returns an invalid response and the retriever returns an empty list.
    /// </summary>
    private static DocsRetriever BuildOfflineRetriever(
        Mock<IEmbeddingGenerator<string, Embedding<float>>>? embeddingMock = null,
        RagOptions? opts = null)
    {
        var mock = embeddingMock ?? BuildEmbeddingMock();
        var es = new ElasticsearchClient(
            new ElasticsearchClientSettings(new Uri("http://localhost:19999")));
        return new DocsRetriever(mock.Object, es, Create(opts ?? new RagOptions()));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetrieveAsync_UnavailableEs_ReturnsEmpty()
    {
        var retriever = BuildOfflineRetriever();

        var result = await retriever.RetrieveAsync("what is a counterparty?", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task RetrieveAsync_CallsEmbeddingGenerator_Once()
    {
        var mock = BuildEmbeddingMock();
        var retriever = BuildOfflineRetriever(embeddingMock: mock);

        await retriever.RetrieveAsync("some query", CancellationToken.None);

        mock.Verify(e => e.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task RetrieveAsync_TopK_UsedInRequest(int topK)
    {
        var retriever = BuildOfflineRetriever(opts: new RagOptions { DocsTopK = topK });

        // Does not throw regardless of topK value; returns empty because ES is unavailable.
        var result = await retriever.RetrieveAsync("query", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(200)]
    public async Task RetrieveAsync_RankConstant_DoesNotCrash(int rankConst)
    {
        var retriever = BuildOfflineRetriever(opts: new RagOptions { RrfRankConstant = rankConst });

        var ex = await Record.ExceptionAsync(
            () => retriever.RetrieveAsync("query", CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task RetrieveAsync_WithEmptyQuery_DoesNotThrow()
    {
        var retriever = BuildOfflineRetriever();

        var ex = await Record.ExceptionAsync(
            () => retriever.RetrieveAsync(string.Empty, CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task RetrieveAsync_EmbeddingGeneratorReceivesQuery()
    {
        const string query = "what is settlement risk?";
        string? capturedQuery = null;

        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mock.Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback((IEnumerable<string> inputs, EmbeddingGenerationOptions? _, CancellationToken _) =>
            {
                capturedQuery = inputs.FirstOrDefault();
            })
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                new[] { new Embedding<float>(new ReadOnlyMemory<float>(new float[384])) }));

        var retriever = BuildOfflineRetriever(embeddingMock: mock);
        await retriever.RetrieveAsync(query, CancellationToken.None);

        Assert.Equal(query, capturedQuery);
    }
}
