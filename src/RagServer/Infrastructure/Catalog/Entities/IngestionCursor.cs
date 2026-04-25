namespace RagServer.Infrastructure.Catalog.Entities;

public class IngestionCursor
{
    public string Source    { get; set; } = "";
    public string Cursor    { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
