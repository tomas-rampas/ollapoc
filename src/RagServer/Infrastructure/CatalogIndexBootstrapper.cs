using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using RagServer.Telemetry;

namespace RagServer.Infrastructure;

public sealed class CatalogIndexBootstrapper(ElasticsearchClient es, ILogger<CatalogIndexBootstrapper> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var activity = RagActivitySource.Source.StartActivity("infra.catalog_index_bootstrap");
        try
        {
            var exists = await es.Indices.ExistsAsync("catalog_terms", ct);
            if (exists.Exists) return;

            await es.Indices.CreateAsync("catalog_terms", c => c
                .Mappings(m => m
                    .Properties<object>(p => p
                        .Text("name")
                        .Keyword("aliases")
                        .Keyword("entity_type")
                        .Text("description")
                        .DenseVector("vector", dv => dv
                            .Dims(384)
                            .Similarity(DenseVectorSimilarity.Cosine)
                            .Index(true)))), ct);

            logger.LogInformation("catalog_terms index created");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "catalog_terms index bootstrap failed — will retry on next start");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
