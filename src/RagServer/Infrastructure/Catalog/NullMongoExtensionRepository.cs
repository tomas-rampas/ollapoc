namespace RagServer.Infrastructure.Catalog;

public sealed class NullMongoExtensionRepository : IMongoExtensionRepository
{
    public Task<IReadOnlyList<ExtensionAttribute>> GetExtensionsAsync(
        string entityName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExtensionAttribute>>([]);
}
