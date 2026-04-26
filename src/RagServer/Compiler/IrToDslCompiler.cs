using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using EsAgg = Elastic.Clients.Elasticsearch.Aggregations;

namespace RagServer.Compiler;

/// <summary>
/// Compiles a <see cref="QuerySpec"/> IR into an Elasticsearch <see cref="SearchRequest"/>.
/// All logic is deterministic and pure — no I/O, no instance state.
/// </summary>
public sealed class IrToDslCompiler
{
    // Characters that are not valid in an Elasticsearch index name.
    // This guards against index-name injection should Compile ever be called
    // without a prior QuerySpecValidator gate.
    private static readonly char[] InvalidIndexChars =
        ['/', '\\', '*', '?', '"', '<', '>', '|', ' ', ',', '#', ':', '.'];

    /// <summary>
    /// Compiles <paramref name="spec"/> into a ready-to-execute <see cref="SearchRequest"/>.
    /// </summary>
    /// <remarks>
    /// Index name convention: <c>spec.Entity.ToLower() + "s"</c> (e.g. "Trade" → "trades").
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="spec"/>.<see cref="QuerySpec.Entity"/> contains characters
    /// that are not valid in an Elasticsearch index name.
    /// </exception>
    public SearchRequest Compile(QuerySpec spec)
    {
        // Defensive-in-depth: reject Entity values that could manipulate the index name.
        // The DataPipeline already runs QuerySpecValidator before calling Compile; this guard
        // protects callers that bypass the validator.
        if (spec.Entity.IndexOfAny(InvalidIndexChars) >= 0)
            throw new ArgumentException(
                $"Entity '{spec.Entity}' contains characters that are not valid in an index name.",
                nameof(spec));

        var index = spec.Entity.ToLower() + "s";

        return new SearchRequest(index)
        {
            Size        = spec.Limit ?? 20,
            Query       = BuildQuery(spec),
            Sort        = BuildSort(spec.Sort),
            Aggregations = BuildAggregations(spec.Aggregations),
        };
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    private static Query BuildQuery(QuerySpec spec)
    {
        var filters   = spec.Filters        ?? [];
        var timeRange = spec.TimeRange;

        if (filters.Count == 0 && timeRange is null)
            return new Query { MatchAll = new MatchAllQuery() };

        var must    = new List<Query>();
        var mustNot = new List<Query>();

        foreach (var f in filters)
        {
            switch (f.Operator)
            {
                case FilterOperator.Eq:
                    must.Add(new Query { Term = new TermQuery { Field = f.Field, Value = FieldValue.String(f.Value) } });
                    break;

                case FilterOperator.Neq:
                    mustNot.Add(new Query { Term = new TermQuery { Field = f.Field, Value = FieldValue.String(f.Value) } });
                    break;

                case FilterOperator.Gt:
                    must.Add(new Query { Range = new UntypedRangeQuery { Field = f.Field, Gt = f.Value } });
                    break;

                case FilterOperator.Gte:
                    must.Add(new Query { Range = new UntypedRangeQuery { Field = f.Field, Gte = f.Value } });
                    break;

                case FilterOperator.Lt:
                    must.Add(new Query { Range = new UntypedRangeQuery { Field = f.Field, Lt = f.Value } });
                    break;

                case FilterOperator.Lte:
                    must.Add(new Query { Range = new UntypedRangeQuery { Field = f.Field, Lte = f.Value } });
                    break;

                case FilterOperator.Contains:
                    must.Add(new Query { Match = new MatchQuery { Field = f.Field, Query = f.Value } });
                    break;

                case FilterOperator.In:
                {
                    var values = SplitValues(f.Value);
                    must.Add(new Query
                    {
                        Terms = new TermsQuery
                        {
                            Field = f.Field,
                            Terms = new TermsQueryField(values.Select(FieldValue.String).ToList())
                        }
                    });
                    break;
                }

                case FilterOperator.NotIn:
                {
                    var values = SplitValues(f.Value);
                    mustNot.Add(new Query
                    {
                        Terms = new TermsQuery
                        {
                            Field = f.Field,
                            Terms = new TermsQueryField(values.Select(FieldValue.String).ToList())
                        }
                    });
                    break;
                }

                case FilterOperator.IsNull:
                    // "field is null" → must_not: [{ exists: { field: "..." } }]
                    mustNot.Add(new Query { Exists = new ExistsQuery { Field = f.Field } });
                    break;

                case FilterOperator.IsNotNull:
                    // "field is not null" → must: [{ exists: { field: "..." } }]
                    must.Add(new Query { Exists = new ExistsQuery { Field = f.Field } });
                    break;
            }
        }

        if (timeRange is not null)
        {
            var rangeQuery = new UntypedRangeQuery { Field = timeRange.Field };

            if (timeRange.RelativePeriod is not null)
            {
                // RelativePeriod takes precedence over From/To
                var (gte, lte) = MapRelativePeriod(timeRange.RelativePeriod);
                rangeQuery.Gte = gte;
                rangeQuery.Lte = lte;
            }
            else
            {
                if (timeRange.From is not null) rangeQuery.Gte = timeRange.From;
                if (timeRange.To   is not null) rangeQuery.Lte = timeRange.To;
            }

            must.Add(new Query { Range = rangeQuery });
        }

        if (must.Count == 0 && mustNot.Count == 0)
            return new Query { MatchAll = new MatchAllQuery() };

        return new Query
        {
            Bool = new BoolQuery
            {
                Must    = must.Count    > 0 ? must    : null,
                MustNot = mustNot.Count > 0 ? mustNot : null,
            }
        };
    }

    // ── Sort ──────────────────────────────────────────────────────────────────

    private static ICollection<SortOptions>? BuildSort(IReadOnlyList<SortClause>? sortClauses)
    {
        if (sortClauses is null || sortClauses.Count == 0)
            return null;

        return sortClauses
            .Select(sc => new SortOptions
            {
                Field = new FieldSort
                {
                    Field = sc.Field,
                    Order = sc.Direction == SortDirection.Asc ? SortOrder.Asc : SortOrder.Desc
                }
            })
            .ToList();
    }

    // ── Aggregations ──────────────────────────────────────────────────────────

    private static IDictionary<string, EsAgg.Aggregation>? BuildAggregations(IReadOnlyList<Aggregation>? aggregations)
    {
        if (aggregations is null || aggregations.Count == 0)
            return null;

        var dict = new Dictionary<string, EsAgg.Aggregation>();

        foreach (var agg in aggregations)
        {
            var key = agg.Name ?? $"{agg.Type.ToString().ToLower()}_{agg.Field.ToLower()}";
            dict[key] = agg.Type switch
            {
                AggregationType.Count    => new EsAgg.Aggregation { ValueCount  = new EsAgg.ValueCountAggregation  { Field = agg.Field } },
                AggregationType.Sum      => new EsAgg.Aggregation { Sum         = new EsAgg.SumAggregation         { Field = agg.Field } },
                AggregationType.Avg      => new EsAgg.Aggregation { Avg         = new EsAgg.AverageAggregation     { Field = agg.Field } },
                AggregationType.Min      => new EsAgg.Aggregation { Min         = new EsAgg.MinAggregation         { Field = agg.Field } },
                AggregationType.Max      => new EsAgg.Aggregation { Max         = new EsAgg.MaxAggregation         { Field = agg.Field } },
                AggregationType.Terms    => new EsAgg.Aggregation { Terms       = new EsAgg.TermsAggregation       { Field = agg.Field } },
                AggregationType.Distinct => new EsAgg.Aggregation { Cardinality = new EsAgg.CardinalityAggregation { Field = agg.Field } },
                AggregationType.GroupBy  => new EsAgg.Aggregation { Terms       = new EsAgg.TermsAggregation       { Field = agg.Field, Size = 10 } },
                _ => throw new CompilerException($"Unknown aggregation type: {agg.Type}")
            };
        }

        return dict;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a comma-separated value string and trims each element.
    /// Used for <see cref="FilterOperator.In"/> and <see cref="FilterOperator.NotIn"/> values.
    /// </summary>
    private static IReadOnlyList<string> SplitValues(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Maps a well-known relative-period token to an (gte, lte) pair of ES date-math strings.
    /// </summary>
    /// <exception cref="CompilerException">Thrown when the period token is unrecognised.</exception>
    private static (string Gte, string Lte) MapRelativePeriod(string period) => period switch
    {
        "today"        => ("now/d",       "now+1d/d"),
        "yesterday"    => ("now-1d/d",    "now/d"),
        "last_7_days"  => ("now-7d/d",    "now"),
        "last_30_days" => ("now-30d/d",   "now"),
        "this_month"   => ("now/M",       "now+1M/M"),
        "this_year"    => ("now/y",       "now+1y/y"),
        "last_year"    => ("now-1y/y",    "now/y"),
        _              => throw new CompilerException($"Unknown relative period: {period}")
    };
}
