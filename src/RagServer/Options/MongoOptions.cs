namespace RagServer.Options;

public sealed class MongoOptions
{
    public string? ConnectionString { get; set; }
    public string Database { get; set; } = "catalog";
    public string ExtensionsCollection { get; set; } = "entity_extensions";
}
