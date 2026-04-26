using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using RagServer.Infrastructure.Catalog;
using RagServer.Telemetry;

namespace RagServer.Infrastructure;

public sealed class CatalogIndexBootstrapper(
    ElasticsearchClient es,
    ILogger<CatalogIndexBootstrapper> logger,
    IServiceScopeFactory scopeFactory,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : IHostedService
{
    private const string IndexName  = "catalog_terms";
    private const int    BatchSize  = 50;

    private sealed record CatalogTermDoc(string name, string entity, string type, float[] vector);

    public async Task StartAsync(CancellationToken ct)
    {
        using var activity = RagActivitySource.Source.StartActivity("infra.catalog_terms_seed");
        try
        {
            // Always recreate the index so SQL data changes are reflected on each restart.
            var exists = await es.Indices.ExistsAsync(IndexName, ct);
            if (exists.Exists)
                await es.Indices.DeleteAsync(IndexName, ct);

            await es.Indices.CreateAsync(IndexName, c => c
                .Mappings(m => m
                    .Properties<object>(p => p
                        .Keyword("name")
                        .Keyword("entity")
                        .Keyword("type")
                        .DenseVector("vector", dv => dv
                            .Dims(384)
                            .Similarity(DenseVectorSimilarity.Cosine)
                            .Index(true)))), ct);

            logger.LogInformation("{Index} index created", IndexName);

            // ── Load catalog data from SQL via a scoped EF context ────────────
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            var entities   = await db.CatalogEntities.AsNoTracking().ToListAsync(ct);
            var attributes = await db.CatalogAttributes
                                     .AsNoTracking()
                                     .Include(a => a.Entity)
                                     .Where(a => a.ParentAttributeId == null)
                                     .ToListAsync(ct);
            var cdes       = await db.CDEs
                                     .AsNoTracking()
                                     .Include(c => c.Entity)
                                     .ToListAsync(ct);

            if (entities.Count == 0)
            {
                logger.LogInformation("{Index} seeding skipped — SQL catalog is empty", IndexName);
                return;
            }

            // ── Build all term documents ──────────────────────────────────────
            var docs = new List<CatalogTermDoc>(entities.Count + attributes.Count + cdes.Count);

            foreach (var e in entities)
            {
                var vec = await EmbedAsync(e.Name, ct);
                docs.Add(new CatalogTermDoc(e.Name, e.Name, "entity", vec));
            }

            foreach (var a in attributes)
            {
                var vec = await EmbedAsync(a.Name, ct);
                docs.Add(new CatalogTermDoc(a.Name, a.Entity.Name, "attribute", vec));
            }

            foreach (var c in cdes)
            {
                var vec = await EmbedAsync(c.Name, ct);
                docs.Add(new CatalogTermDoc(c.Name, c.Entity.Name, "cde", vec));
            }

            // ── Bulk-index in batches of BatchSize ────────────────────────────
            for (int i = 0; i < docs.Count; i += BatchSize)
            {
                var batch = docs.Skip(i).Take(BatchSize).ToList();
                var bulkRequest = new BulkRequest(IndexName)
                {
                    Operations = batch
                        .Select(d => (IBulkOperation)new BulkIndexOperation<CatalogTermDoc>(d))
                        .ToList()
                };

                var bulkResp = await es.BulkAsync(bulkRequest, ct);
                if (bulkResp.Errors)
                    logger.LogWarning("{Index} bulk batch {Offset} had errors", IndexName, i);
            }

            logger.LogInformation(
                "{Index} seeded with {EntityCount} entities, {AttrCount} attributes, {CdeCount} CDEs",
                IndexName, entities.Count, attributes.Count, cdes.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Index} bootstrap failed — will retry on next start", IndexName);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var results = await embedder.GenerateAsync(new[] { text }, cancellationToken: ct);
        return results[0].Vector.ToArray();
    }
}
