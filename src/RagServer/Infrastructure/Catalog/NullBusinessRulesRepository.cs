namespace RagServer.Infrastructure.Catalog;

public sealed class NullBusinessRulesRepository : IBusinessRulesRepository
{
    public Task<IReadOnlyList<BusinessRule>> GetRulesAsync(
        string entityName, bool? mandatoryOnly = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BusinessRule>>([]);
}
