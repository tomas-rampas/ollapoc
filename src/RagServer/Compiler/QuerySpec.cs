namespace RagServer.Compiler;

/// <summary>
/// Typed intermediate representation of a natural-language data query.
/// The model generates a JSON payload that is deserialized into this record;
/// <see cref="IrToDslCompiler"/> then converts it to an Elasticsearch DSL.
/// </summary>
public record QuerySpec(
    string Entity,
    IReadOnlyList<Filter> Filters,
    TimeRange? TimeRange,
    IReadOnlyList<SortClause> Sort,
    IReadOnlyList<Aggregation> Aggregations,
    int? Limit);

/// <summary>A filter predicate on a single document field.</summary>
public record Filter(string Field, FilterOperator Operator, string Value);

/// <summary>Absolute or ES date-math bounded time range on a date field.</summary>
public record TimeRange(string Field, string? From, string? To);   // ISO-8601 or ES date math

/// <summary>A sort directive for a single field.</summary>
public record SortClause(string Field, SortDirection Direction);

/// <summary>A bucket or metric aggregation on a single field.</summary>
public record Aggregation(AggregationType Type, string Field, string? Name);

public enum FilterOperator { Eq, Neq, Gt, Gte, Lt, Lte, Contains, In, NotIn }
public enum SortDirection { Asc, Desc }
public enum AggregationType { Count, Sum, Avg, Min, Max, Terms }
