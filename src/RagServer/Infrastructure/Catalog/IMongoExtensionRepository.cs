namespace RagServer.Infrastructure.Catalog;

public record ExtensionAttribute(string Name, string Value, string? Description);

public interface IMongoExtensionRepository
{
    Task<IReadOnlyList<ExtensionAttribute>> GetExtensionsAsync(
        string entityName, CancellationToken ct = default);
}
