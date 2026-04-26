namespace RagServer.Infrastructure.Catalog.Entities;

public sealed class CriticalDataElement
{
    public int Id { get; set; }
    public int CatalogEntityId { get; set; }
    public CatalogEntity Entity { get; set; } = null!;
    public required string Name { get; set; }
    public required string GovernanceOwner { get; set; }
    public string? RegulatoryReference { get; set; }   // e.g. "EMIR Art.9", "MiFID II Art.26"
    public string? Description { get; set; }
}
