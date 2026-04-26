using Microsoft.Extensions.AI;
using Moq;
using RagServer.Infrastructure;
using RagServer.Options;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Infrastructure;

public class EmbeddingCacheTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Embedding<float> MakeEmbedding(float seed = 0.1f)
        => new(new ReadOnlyMemory<float>(new[] { seed, seed + 0.1f, seed + 0.2f }));

    private static GeneratedEmbeddings<Embedding<float>> MakeResult(float seed = 0.1f)
        => new(new List<Embedding<float>> { MakeEmbedding(seed) });

    private static (EmbeddingCache Cache, Mock<IEmbeddingGenerator<string, Embedding<float>>> InnerMock)
        BuildCache(int capacity = 10)
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mock.Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        var cache = new EmbeddingCache(mock.Object, Create(new RagOptions { EmbeddingCacheSize = capacity }));
        return (cache, mock);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task First_Call_Is_A_Cache_Miss()
    {
        var (cache, mock) = BuildCache();

        await cache.GenerateAsync(new[] { "hello" });

        mock.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Second_Call_Same_Key_Is_Cache_Hit()
    {
        var (cache, mock) = BuildCache();

        await cache.GenerateAsync(new[] { "hello" });
        await cache.GenerateAsync(new[] { "hello" });

        // Inner called only once — the second call was served from cache
        mock.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Different_Keys_Both_Miss()
    {
        var (cache, mock) = BuildCache();

        await cache.GenerateAsync(new[] { "alpha" });
        await cache.GenerateAsync(new[] { "beta" });

        mock.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Eviction_Removes_Lru_Entry()
    {
        // Capacity of 2; inserting a 3rd key evicts the LRU (first inserted key)
        var (cache, mock) = BuildCache(capacity: 2);

        // Miss: "key1" → cached
        await cache.GenerateAsync(new[] { "key1" });
        // Miss: "key2" → cached (key1 still present but older)
        await cache.GenerateAsync(new[] { "key2" });
        // Miss: "key3" → key1 evicted as LRU (key2 was accessed more recently)
        await cache.GenerateAsync(new[] { "key3" });

        // Reset call count tracking — verify that key1 is re-fetched (evicted)
        mock.Invocations.Clear();
        mock.Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult());

        await cache.GenerateAsync(new[] { "key1" });

        // key1 was evicted so inner must be called again
        mock.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Batch_Input_Bypasses_Cache()
    {
        var (cache, mock) = BuildCache();

        // Batch of 2 values — cache only handles single-item requests
        var batch = new[] { "a", "b" };
        mock.Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                new List<Embedding<float>> { MakeEmbedding(0.1f), MakeEmbedding(0.2f) }));

        await cache.GenerateAsync(batch);
        await cache.GenerateAsync(batch);

        // Both calls bypass cache → inner called twice
        mock.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Cache_Hit_Does_Not_Call_Inner_Twice()
    {
        var (cache, mock) = BuildCache();
        const string key = "the-query";

        await cache.GenerateAsync(new[] { key });
        await cache.GenerateAsync(new[] { key });

        mock.Verify(g => g.GenerateAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<EmbeddingGenerationOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(1));
    }
}
