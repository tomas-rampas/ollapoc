using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RagServer.Options;

namespace RagServer.Infrastructure;

public sealed class EmbeddingCache(
    [FromKeyedServices("embeddings")] IEmbeddingGenerator<string, Embedding<float>> inner,
    IOptions<RagOptions> opts) : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly int _capacity = opts.Value.EmbeddingCacheSize;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly LinkedList<string> _order = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, Embedding<float> Value)> _map = new();

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken ct = default)
    {
        var list = values.ToList();
        if (list.Count != 1)
            return await inner.GenerateAsync(list, options, ct);

        var key = list[0];
        await _lock.WaitAsync(ct);
        try
        {
            if (_map.TryGetValue(key, out var entry))
            {
                _order.Remove(entry.Node);
                _order.AddFirst(entry.Node);
                Activity.Current?.SetTag("embedding.cache_hit", true);
                return new GeneratedEmbeddings<Embedding<float>>([entry.Value]);
            }
        }
        finally { _lock.Release(); }

        Activity.Current?.SetTag("embedding.cache_hit", false);
        var result = await inner.GenerateAsync(list, options, ct);
        var embedding = result[0];

        await _lock.WaitAsync(ct);
        try
        {
            if (_map.Count >= _capacity)
            {
                var lru = _order.Last!.Value;
                _map.Remove(lru);
                _order.RemoveLast();
            }
            var node = _order.AddFirst(key);
            _map[key] = (node, embedding);
        }
        finally { _lock.Release(); }
        return result;
    }

    public object? GetService(Type serviceType, object? key = null) => inner.GetService(serviceType, key);
    public void Dispose() { }
}
