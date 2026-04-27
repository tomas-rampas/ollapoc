using System.ComponentModel;
using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagServer.Infrastructure.Catalog;
using RagServer.Options;
using RagServer.Telemetry;

namespace RagServer.Tools;

public record ResolvedEntity(string Name, string EntityType, string? Description);
public record EntitySummary(string Name, string EntityType, string EntityCode, string? Description);
public record ChildAttributeInfo(string Code, string DataType, string? Description);
public record EntityAttributeInfo(
    string Name,
    string DataType,
    string? Description,
    bool IsNullable,
    string? AttributeCode,
    bool IsMandatory,
    bool IsCde,
    string? Owner,
    string? Sensitivity,
    bool HasChildren,
    IReadOnlyList<ChildAttributeInfo> Children);
public record CdeInfo(string Name, string GovernanceOwner, string? RegulatoryReference, string? Description);
public record RelationshipInfo(string SourceEntity, string TargetEntity, string RelationshipType, string? Description);

public sealed class CatalogTools(
    CatalogDbContext db,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ElasticsearchClient es,
    IMongoExtensionRepository mongoRepo,
    IBusinessRulesRepository rulesRepo,
    IOptions<RagOptions> opts,
    ILogger<CatalogTools> logger)
{
    private const int MaxToolArgLength = 512;

    [Description("List all entities registered in the catalog, optionally filtered by entity type (e.g. 'reference', 'party', 'account', 'process', 'instruction', 'financial_instrument'). Use this when the user asks what entities or entity types exist in the system.")]
    public async Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(
        [Description("Optional entity type filter, e.g. 'reference'. Pass null to return all entities.")] string? entityType = null,
        CancellationToken ct = default)
    {
        if (entityType is not null && entityType.Length > MaxToolArgLength) return [];

        var query = db.CatalogEntities.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(e => e.EntityType.ToLower() == entityType.ToLower());

        return await query
            .OrderBy(e => e.EntityType).ThenBy(e => e.Name)
            .Select(e => new EntitySummary(e.Name, e.EntityType, e.EntityCode, e.Description))
            .ToListAsync(ct);
    }

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
            var entityType   = src.TryGetProperty("entityType",  out var et) ? et.GetString() ?? "" : "";
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

    [Description("Get the schema attributes of a catalog entity. Returns top-level attributes with inline child summaries for complex multi-value attributes (HasChildren=true). Use GetChildAttributesAsync to retrieve full child details.")]
    public async Task<IReadOnlyList<EntityAttributeInfo>> GetEntityAttributesAsync(
        [Description("Canonical entity name, e.g. 'Counterparty', 'Book', 'ClientAccount'")] string entityName,
        [Description("If true, return only mandatory attributes. Omit or pass null to return all.")] bool? mandatoryOnly = null,
        CancellationToken ct = default)
    {
        if (entityName.Length > MaxToolArgLength) return [];

        using var activity = RagActivitySource.Source.StartActivity("catalog.get_entity_attributes");
        activity?.SetTag("entity", entityName);
        activity?.SetTag("mandatoryOnly", mandatoryOnly);

        // Load top-level attributes with their children included for inline summary
        var query = db.CatalogAttributes
            .Include(a => a.Children)
            .Where(a => a.Entity.Name.ToLower() == entityName.ToLower()
                     && a.ParentAttributeId == null);

        if (mandatoryOnly == true)
            query = query.Where(a => a.IsMandatory);

        var rows = await query.ToListAsync(ct);

        return rows.Select(a => new EntityAttributeInfo(
            a.Name,
            a.DataType,
            a.Description,
            a.IsNullable,
            a.AttributeCode,
            a.IsMandatory,
            a.IsCde,
            a.Owner,
            a.Sensitivity,
            a.Children.Count > 0,
            a.Children
                .Select(c => new ChildAttributeInfo(c.AttributeCode ?? c.Name, c.DataType, c.Description))
                .ToList()
        )).ToList();
    }

    [Description("Returns the full schema details for each child item of a complex multi-value attribute (e.g. system_map, addresses, identifiers, risk_limits). Call this when GetEntityAttributesAsync returns an attribute with HasChildren=true.")]
    public async Task<IReadOnlyList<EntityAttributeInfo>> GetChildAttributesAsync(
        [Description("Canonical entity name, e.g. 'Counterparty'")] string entityName,
        [Description("AttributeCode of the parent complex attribute, e.g. 'system_map', 'addresses', 'identifiers'")] string parentAttributeCode,
        CancellationToken ct = default)
    {
        if (entityName.Length > MaxToolArgLength || parentAttributeCode.Length > MaxToolArgLength) return [];

        var children = await db.CatalogAttributes
            .Where(a => a.Entity.Name.ToLower() == entityName.ToLower()
                     && a.Parent != null
                     && a.Parent.AttributeCode == parentAttributeCode)
            .Select(a => new EntityAttributeInfo(
                a.Name,
                a.DataType,
                a.Description,
                a.IsNullable,
                a.AttributeCode,
                a.IsMandatory,
                a.IsCde,
                a.Owner,
                a.Sensitivity,
                false,
                new List<ChildAttributeInfo>()))
            .ToListAsync(ct);

        return children;
    }

    [Description("Returns data quality and business rules for a financial MDM entity. Rules can be MANDATORY (always apply) or CONDITIONAL (apply when conditions are met). Use mandatoryOnly=true to filter to only mandatory rules.")]
    public async Task<IReadOnlyList<BusinessRule>> GetEntityRulesAsync(
        [Description("Canonical entity name, e.g. 'Counterparty', 'Book'")] string entityName,
        [Description("If true, return only MANDATORY rules. Omit or pass null to return all active rules.")] bool? mandatoryOnly = null,
        CancellationToken ct = default)
    {
        if (entityName.Length > MaxToolArgLength) return [];

        using var activity = RagActivitySource.Source.StartActivity("catalog.get_rules");
        activity?.SetTag("entity", entityName);
        activity?.SetTag("mandatoryOnly", mandatoryOnly);

        return await rulesRepo.GetRulesAsync(entityName, mandatoryOnly, ct);
    }

    [Description("Get extension attributes for an entity from the MongoDB extensions store. Note: entity_extensions collection is not seeded in this version — returns empty for all entities.")]
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
