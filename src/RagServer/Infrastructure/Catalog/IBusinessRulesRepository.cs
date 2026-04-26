namespace RagServer.Infrastructure.Catalog;

public record RuleCondition(string AttributeCode, string Operator, string Value);

public record BusinessRule(
    string RuleId,
    string EntityCode,
    string RuleName,
    string RuleType,
    string Description,
    IReadOnlyList<RuleCondition> Conditions,
    string Severity,
    string Owner,
    string? RegulatoryReference,
    bool IsActive);

public interface IBusinessRulesRepository
{
    Task<IReadOnlyList<BusinessRule>> GetRulesAsync(
        string entityName,
        bool? mandatoryOnly = null,
        CancellationToken ct = default);
}
