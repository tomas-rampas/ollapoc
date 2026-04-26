namespace RagServer.Compiler;

/// <summary>
/// Validates a <see cref="QuerySpec"/> before it is compiled into an Elasticsearch DSL.
/// Pure stateless logic — no I/O, no allocations beyond the error list.
/// </summary>
public sealed class QuerySpecValidator
{
    /// <summary>
    /// Validates <paramref name="spec"/> against the set of <paramref name="knownEntities"/>.
    /// </summary>
    /// <param name="spec">The query spec produced by the model.</param>
    /// <param name="knownEntities">
    ///   Canonical entity names (case-insensitive comparison).
    ///   Typically sourced from the <c>schema_cards</c> index.
    /// </param>
    public ValidationResult Validate(QuerySpec spec, IReadOnlySet<string> knownEntities)
    {
        var errors = new List<string>();

        // ── Entity ────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(spec.Entity))
        {
            errors.Add("Entity must not be null or whitespace.");
        }
        else if (!knownEntities.Contains(spec.Entity, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Entity '{spec.Entity}' is not a known entity.");
        }

        // ── Filters ───────────────────────────────────────────────────────────
        foreach (var filter in spec.Filters ?? [])
        {
            if (string.IsNullOrWhiteSpace(filter.Field))
                errors.Add("Filter.Field must not be null or whitespace.");

            // IsNull/IsNotNull operate on field existence — Value is ignored and may be empty.
            var valueRequired = filter.Operator is not (FilterOperator.IsNull or FilterOperator.IsNotNull);
            if (valueRequired && string.IsNullOrEmpty(filter.Value))
                errors.Add($"Filter on field '{filter.Field}' requires a non-empty Value for operator {filter.Operator}.");
        }

        // ── TimeRange ─────────────────────────────────────────────────────────
        if (spec.TimeRange is { From: not null, To: not null } tr)
        {
            if (DateTimeOffset.TryParse(tr.From, out var fromDt) &&
                DateTimeOffset.TryParse(tr.To, out var toDt) &&
                fromDt > toDt)
            {
                errors.Add($"TimeRange.From ({tr.From}) must not be after TimeRange.To ({tr.To}).");
            }
        }

        // ── Sort ──────────────────────────────────────────────────────────────
        foreach (var sort in spec.Sort ?? [])
        {
            if (string.IsNullOrWhiteSpace(sort.Field))
                errors.Add("SortClause.Field must not be null or whitespace.");
        }

        // ── Aggregations ──────────────────────────────────────────────────────
        foreach (var agg in spec.Aggregations ?? [])
        {
            if (string.IsNullOrWhiteSpace(agg.Field))
                errors.Add("Aggregation.Field must not be null or whitespace.");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }
}

/// <summary>Result returned by <see cref="QuerySpecValidator.Validate"/>.</summary>
public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);
