using System.ComponentModel;
using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagServer.Infrastructure.Catalog;
using RagServer.Options;

namespace RagServer.Tools;

public record ResolvedEntity(string Name, string EntityType, string? Description);
public record EntityAttributeInfo(string Name, string DataType, string? Description, bool IsNullable);
public record CdeInfo(string Name, string GovernanceOwner, string? RegulatoryReference, string? Description);
public record RelationshipInfo(string SourceEntity, string TargetEntity, string RelationshipType, string? Description);

public sealed class CatalogTools(
    CatalogDbContext db,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ElasticsearchClient es,
    IMongoExtensionRepository mongoRepo,
    IOptions<RagOptions> opts,
    ILogger<CatalogTools> logger)
{
    private const int MaxToolArgLength = 512;

    [Description("Resolve a natural-language entity name to its canonical catalog name and entity type.")]
    public async Task<ResolvedEntity?> ResolveEntityAsync(
        [Description("Natural language entity name, e.g. 'trade', 'settlement', 'counterparty'")] string name,
        CancellationToken ct = default)
    {
        if (name.Length > MaxToolArgLength) return null;

        try
        {
            var embedResult = await embeddings.GenerateAsync([name], cancellationToken: ct);
            var qvec = embedResult[0].Vector.ToArray();

            var topK = opts.Value.CatalogTermsTopK;

            var resp = await es.SearchAsync<System.Text.Json.JsonElement>(s => s
                .Indices("catalog_terms")
                .Size(topK)
                .Knn(k => k
                    .Field("vector")
                    .QueryVector(qvec)
                    .K(topK)
                    .NumCandidates(topK * 5)), ct);

            if (!resp.IsValidResponse || !resp.Hits.Any())
                return null;

            var hit = resp.Hits.First();
            var src = hit.Source;
            var resolvedName = src.TryGetProperty("name",        out var n)  ? n.GetString()  ?? "" : "";
            var entityType   = src.TryGetProperty("entity_type", out var et) ? et.GetString() ?? "" : "";
            var description  = src.TryGetProperty("description", out var d)  ? d.GetString()       : null;

            return new ResolvedEntity(resolvedName, entityType, description);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ResolveEntity failed for '{Name}'", name);
            return null;
        }
    }

    [Description("Get all attributes (fields) of a catalog entity by its canonical name.")]
    public async Task<IReadOnlyList<EntityAttributeInfo>> GetEntityAttributesAsync(
        [Description("Canonical entity name, e.g. 'Trade', 'Settlement'")] string entityName,
        CancellationToken ct = default)
    {
        if (entityName.Length > MaxToolArgLength) return [];

        return await db.CatalogAttributes
            .Where(a => a.Entity.Name.ToLower() == entityName.ToLower())
            .Select(a => new EntityAttributeInfo(a.Name, a.DataType, a.Description, a.IsNullable))
            .ToListAsync(ct);
    }

    [Description("Get extension attributes for an entity from the MongoDB extensions store.")]
    public async Task<IReadOnlyList<ExtensionAttribute>> GetEntityExtensionsAsync(
        [Description("Canonical entity name, e.g. 'Trade'")] string entityName,
        CancellationToken ct = default)
    {
        if (entityName.Length > MaxToolArgLength) return [];

        return await mongoRepo.GetExtensionsAsync(entityName, ct);
    }

    [Description("List critical data elements (CDEs), optionally filtered by entity name.")]
    public async Task<IReadOnlyList<CdeInfo>> ListCDEAsync(
        [Description("Optional canonical entity name to filter by. Pass null to list all CDEs.")] string? entityName,
        CancellationToken ct = default)
    {
        if (entityName is not null && entityName.Length > MaxToolArgLength) return [];

        var query = db.CDEs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityName))
            query = query.Where(c => c.Entity.Name.ToLower() == entityName.ToLower());

        return await query
            .Select(c => new CdeInfo(c.Name, c.GovernanceOwner, c.RegulatoryReference, c.Description))
            .ToListAsync(ct);
    }

    [Description("Get relationships for an entity — both outgoing (entity is source) and incoming (entity is target).")]
    public async Task<IReadOnlyList<RelationshipInfo>> GetEntityRelationshipsAsync(
        [Description("Canonical entity name, e.g. 'Trade'")] string entityName,
        CancellationToken ct = default)
    {
        if (entityName.Length > MaxToolArgLength) return [];

        return await db.EntityRelationships
            .Where(r => r.SourceEntity.Name.ToLower() == entityName.ToLower() ||
                        r.TargetEntity.Name.ToLower() == entityName.ToLower())
            .Select(r => new RelationshipInfo(
                r.SourceEntity.Name,
                r.TargetEntity.Name,
                r.RelationshipType,
                r.Description))
            .ToListAsync(ct);
    }
}
