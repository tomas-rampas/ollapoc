namespace RagServer.Infrastructure.Catalog.Entities;

public sealed class CatalogAttribute
{
    public int Id { get; set; }
    public int CatalogEntityId { get; set; }
    public CatalogEntity Entity { get; set; } = null!;
    public required string Name { get; set; }
    public required string DataType { get; set; }   // "string", "decimal", "datetime", "int", "bool", "object", "object[]"
    public string? Description { get; set; }
    public bool IsNullable { get; set; }

    // Self-reference for complex multi-value attributes (e.g. system_map, addresses)
    public int? ParentAttributeId { get; set; }
    public CatalogAttribute? Parent { get; set; }
    public ICollection<CatalogAttribute> Children { get; set; } = [];

    // Metadata fields
    public string? AttributeCode { get; set; }
    public bool IsMandatory { get; set; }
    public bool IsCde { get; set; }
    public string? Owner { get; set; }
    public string? Sensitivity { get; set; }
    public string? BusinessTerm { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public DateTimeOffset? LastUpdatedDate { get; set; }
}
