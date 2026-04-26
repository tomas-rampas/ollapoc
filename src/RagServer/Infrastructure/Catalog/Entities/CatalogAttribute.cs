namespace RagServer.Infrastructure.Catalog.Entities;

public sealed class CatalogAttribute
{
    public int Id { get; set; }
    public int CatalogEntityId { get; set; }
    public CatalogEntity Entity { get; set; } = null!;
    public required string Name { get; set; }
    public required string DataType { get; set; }   // "string", "decimal", "datetime", "int", "bool"
    public string? Description { get; set; }
    public bool IsNullable { get; set; }
}
