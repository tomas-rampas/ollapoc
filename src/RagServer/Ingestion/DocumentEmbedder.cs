using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using RagServer.Infrastructure.Docs;
using RagServer.Options;
using RagServer.Telemetry;

namespace RagServer.Ingestion;

public sealed class DocumentEmbedder(
    [FromKeyedServices("embeddings")] IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ElasticsearchClient es,
    IOptions<IngestionOptions> opts,
    ILogger<DocumentEmbedder> logger)
{
    private static readonly ResiliencePipeline _pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(30)
        })
        .Build();

    public async Task EmbedAndUpsertAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken ct)
    {
        int batchSize = opts.Value.BatchEmbedSize;
        for (int i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize).ToList();
            using var embedAct = RagActivitySource.Source.StartActivity("ingestion.embed_batch");
            embedAct?.SetTag("ingestion.batch_size", batch.Count);

            var embs = await _pipeline.ExecuteAsync(
                async t => await embeddings.GenerateAsync(batch.Select(c => c.Content), null, t), ct);

            using var upsertAct = RagActivitySource.Source.StartActivity("ingestion.es_upsert");

            var operations = new BulkOperationsCollection();
            foreach (var (chunk, emb) in batch.Zip(embs))
            {
                var docId = $"{chunk.SourceType}_{chunk.SourceId}_{chunk.ChunkIndex}";
                var doc = new
                {
                    content          = chunk.Content,
                    title            = chunk.Title,
                    url              = chunk.Url,
                    source_type      = chunk.SourceType,
                    source_id        = chunk.SourceId,
                    space_or_project = chunk.SpaceOrProject,
                    last_modified    = chunk.LastModified,
                    chunk_index      = chunk.ChunkIndex,
                    vector           = emb.Vector.ToArray()
                };
                operations.Add(new BulkIndexOperation<object>(doc)
                {
                    Index = "docs",
                    Id = docId
                });
            }

            await _pipeline.ExecuteAsync(async t =>
            {
                var request = new BulkRequest { Operations = operations };
                var r = await es.BulkAsync(request, t);
                if (r.Errors)
                    logger.LogWarning("Bulk upsert had errors for batch starting at {Start}", i);
            }, ct);
        }
    }
}
