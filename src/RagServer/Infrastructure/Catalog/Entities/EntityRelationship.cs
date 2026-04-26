namespace RagServer.Infrastructure.Catalog.Entities;

public sealed class EntityRelationship
{
    public int Id { get; set; }
    public int SourceEntityId { get; set; }
    public CatalogEntity SourceEntity { get; set; } = null!;
    public int TargetEntityId { get; set; }
    public CatalogEntity TargetEntity { get; set; } = null!;
    public required string RelationshipType { get; set; }  // e.g. "hasCounterparty", "settledBy", "contains"
    public string? Description { get; set; }
}
