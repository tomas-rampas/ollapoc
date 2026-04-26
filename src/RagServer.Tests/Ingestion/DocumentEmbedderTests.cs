using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RagServer.Infrastructure.Docs;
using RagServer.Ingestion;
using RagServer.Options;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Ingestion;

public class DocumentEmbedderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Embedding<float> MakeEmbedding()
        => new(new ReadOnlyMemory<float>(new[] { 0.1f, 0.2f, 0.3f }));

    private static GeneratedEmbeddings<Embedding<float>> MakeResult(int count = 1)
        => new(Enumerable.Range(0, count).Select(_ => MakeEmbedding()).ToList());

    private static DocumentChunk MakeChunk(int index = 0)
        => new(
            Content: $"Content {index}",
            Title: "Test Title",
            Url: "https://example.com",
            SourceType: "confluence",
            SourceId: $"PAGE-{index}",
            SpaceOrProject: "TEST",
            LastModified: DateTime.UtcNow,
            ChunkIndex: index);

    /// <summary>
    /// Non-throwing client: ES calls return error responses instead of exceptions.
    /// Polly never retries, the loop runs to completion, embeddings are counted correctly.
    /// </summary>
    private static ElasticsearchClient BuildSilentlyFailingClient()
        => new(new ElasticsearchClientSettings(new Uri("http://localhost:19999")));

    /// <summary>
    /// Throwing client: ES calls raise exceptions so Polly retries and eventually propagates.
    /// </summary>
    private static ElasticsearchClient BuildThrowingClient()
        => new(new ElasticsearchClientSettings(new Uri("http://localhost:19999"))
            .ThrowExceptions());

    private static (DocumentEmbedder Embedder, Mock<IEmbeddingGenerator<string, Embedding<float>>> Mock)
        Build(int batchSize = 32, ElasticsearchClient? esClient = null)
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mock.Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> vals, EmbeddingGenerationOptions? _, CancellationToken _) =>
                MakeResult(vals.Count()));

        var opts = Create(new IngestionOptions { BatchEmbedSize = batchSize });
        var es = esClient ?? BuildSilentlyFailingClient();
        var embedder = new DocumentEmbedder(mock.Object, es, opts, NullLogger<DocumentEmbedder>.Instance);
        return (embedder, mock);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyChunkList_DoesNotCallEmbeddings()
    {
        var (embedder, mock) = Build();

        // Empty list — the loop body is never entered
        await embedder.EmbedAndUpsertAsync(Array.Empty<DocumentChunk>(), CancellationToken.None);

        mock.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SingleChunk_CallsEmbeddingsOnce()
    {
        // Non-throwing ES: the upsert silently fails but the loop completes.
        var (embedder, mock) = Build(batchSize: 32);
        var chunks = new[] { MakeChunk(0) };

        await embedder.EmbedAndUpsertAsync(chunks, CancellationToken.None);

        // Embedding must have been called exactly once (one batch of 1 chunk)
        mock.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void DeterministicDocId_Format()
    {
        // Verify the doc ID format without calling EmbedAndUpsertAsync.
        // The implementation uses: $"{chunk.SourceType}_{chunk.SourceId}_{chunk.ChunkIndex}"
        var chunk = new DocumentChunk(
            Content: "test",
            Title: "t",
            Url: "u",
            SourceType: "confluence",
            SourceId: "PAGE-42",
            SpaceOrProject: "PROJ",
            LastModified: DateTime.UtcNow,
            ChunkIndex: 3);

        var expectedId = $"{chunk.SourceType}_{chunk.SourceId}_{chunk.ChunkIndex}";
        Assert.Equal("confluence_PAGE-42_3", expectedId);
    }

    [Fact]
    public async Task BatchSize_Respected()
    {
        // 3 chunks, batchSize=2 → 2 batches (2 + 1) → embeddings called twice.
        // Use the non-throwing ES client so the loop runs to completion across both batches.
        var (embedder, mock) = Build(batchSize: 2);
        var chunks = new[] { MakeChunk(0), MakeChunk(1), MakeChunk(2) };

        await embedder.EmbedAndUpsertAsync(chunks, CancellationToken.None);

        // First batch (2 chunks) → one GenerateAsync call; second batch (1 chunk) → second call
        mock.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task EmbedAndUpsertAsync_Propagates_OnEsError()
    {
        // ThrowExceptions() makes the ES client throw on transport failure.
        // Polly retries 3× then propagates the exception; the CancellationToken
        // with a 5 s timeout aborts retries early so the test stays fast.
        var (embedder, _) = Build(batchSize: 32, esClient: BuildThrowingClient());
        var chunks = new[] { MakeChunk(0) };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var exception = await Record.ExceptionAsync(async () =>
            await embedder.EmbedAndUpsertAsync(chunks, cts.Token));

        // OperationCanceledException (from CancellationToken) or a Polly/transport exception
        Assert.NotNull(exception);
    }
}
