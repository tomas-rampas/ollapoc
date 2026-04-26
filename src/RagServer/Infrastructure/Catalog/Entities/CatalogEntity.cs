namespace RagServer.Infrastructure.Catalog.Entities;

public sealed class CatalogEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string EntityType { get; set; }
    public string? Description { get; set; }
    public ICollection<CatalogAttribute> Attributes { get; set; } = [];
    public ICollection<CriticalDataElement> CDEs { get; set; } = [];
    public ICollection<EntityRelationship> SourceRelationships { get; set; } = [];
    public ICollection<EntityRelationship> TargetRelationships { get; set; } = [];
}
