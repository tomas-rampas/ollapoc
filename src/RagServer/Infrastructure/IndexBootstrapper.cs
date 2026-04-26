using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using RagServer.Telemetry;

namespace RagServer.Infrastructure;

public sealed class IndexBootstrapper(ElasticsearchClient es, ILogger<IndexBootstrapper> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var activity = RagActivitySource.Source.StartActivity("infra.index_bootstrap");
        try
        {
            var exists = await es.Indices.ExistsAsync("docs", ct);
            if (exists.Exists) return;

            await es.Indices.CreateAsync("docs", c => c
                .Mappings(m => m
                    .Properties<object>(p => p
                        .Text("content")
                        .DenseVector("vector", dv => dv
                            .Dims(384)
                            .Similarity(DenseVectorSimilarity.Cosine)
                            .Index(true))
                        .Keyword("source_type")
                        .Keyword("source_id")
                        .Keyword("url")
                        .Keyword("space_or_project")
                        .Text("title")
                        .Date("last_modified")
                        .IntegerNumber("chunk_index"))), ct);

            logger.LogInformation("docs index created");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "docs index bootstrap failed — will retry on next start");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
