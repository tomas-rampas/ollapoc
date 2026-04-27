using System.Text;
using Microsoft.Data.SqlClient;

namespace RagServer.Compiler;

/// <summary>
/// Compiles a <see cref="QuerySpec"/> IR into a parameterized SQL SELECT command
/// targeting the SQL Server business-data tables in <c>BusinessDataContext</c>.
/// <para>
/// All filter values are bound as <see cref="SqlParameter"/> — never concatenated
/// into the SQL string. This prevents SQL injection regardless of filter input.
/// </para>
/// </summary>
public sealed class QuerySpecToSqlCompiler
{
    // Maps canonical entity names (case-insensitive) to SQL table names.
    private static readonly Dictionary<string, string> EntityToTable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Counterparty"]          = "Counterparties",
            ["Country"]               = "Countries",
            ["Currency"]              = "Currencies",
            ["Book"]                  = "Books",
            ["Settlement"]            = "Settlements",
            ["ClientAccount"]         = "ClientAccounts",
            ["SettlementInstruction"] = "SettlementInstructions",
            ["Region"]                = "Regions",
            ["Location"]              = "Locations",
            ["UkSicCode"]             = "UkSicCodes",
            ["NaceCode"]              = "NaceCodes",
        };

    // Static schema used for LLM prompt context — keyed by entity name.
    // Values are (column_name, data_type) tuples using the same snake_case column names
    // defined in the EF entity classes via [Column("...")] attributes.
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Column, string DataType)>> Schema =
        new Dictionary<string, IReadOnlyList<(string, string)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Counterparty"] =
            [
                ("counterparty_id",       "string"),
                ("lei",                   "string"),
                ("legal_name",            "string"),
                ("short_name",            "string"),
                ("entity_type",           "string"),
                ("incorporation_country", "string"),
                ("status",                "string"),
            ],
            ["Country"] =
            [
                ("country_code",    "string"),
                ("country_name",    "string"),
                ("iso_alpha3",      "string"),
                ("fatf_status",     "string"),
                ("sanctions_status","string"),
            ],
            ["Currency"] =
            [
                ("currency_code",  "string"),
                ("currency_name",  "string"),
                ("decimal_places", "int"),
                ("is_deliverable", "bool"),
                ("is_active",      "bool"),
            ],
            ["Book"] =
            [
                ("book_id",        "string"),
                ("book_code",      "string"),
                ("legal_entity",   "string"),
                ("asset_class",    "string"),
                ("status",         "string"),
                ("booking_system", "string"),
            ],
            ["Settlement"] =
            [
                ("settlement_id",   "string"),
                ("counterparty_id", "string"),
                ("amount",          "decimal"),
                ("currency",        "string"),
                ("status",          "string"),
                ("settlement_date", "string"),
            ],
            ["ClientAccount"] =
            [
                ("account_id",      "string"),
                ("counterparty_id", "string"),
                ("account_number",  "string"),
                ("currency",        "string"),
                ("status",          "string"),
                ("account_type",    "string"),
            ],
            ["SettlementInstruction"] =
            [
                ("instruction_id",  "string"),
                ("counterparty_id", "string"),
                ("currency",        "string"),
                ("instruction_type","string"),
                ("status",          "string"),
            ],
            ["Region"] =
            [
                ("region_id",    "string"),
                ("region_name",  "string"),
                ("country",      "string"),
                ("is_uk_region", "bool"),
            ],
            ["Location"] =
            [
                ("location_id",   "string"),
                ("location_name", "string"),
                ("address_line1", "string"),
                ("city",          "string"),
                ("postcode",      "string"),
                ("region_id",     "string"),
                ("country",       "string"),
                ("business_type", "string"),
                ("status",        "string"),
                ("sic_code",      "string"),
                ("nace_code",     "string"),
            ],
            ["UkSicCode"] =
            [
                ("sic_code",            "string"),
                ("description",         "string"),
                ("section",             "string"),
                ("section_description", "string"),
                ("division",            "string"),
                ("group_code",          "string"),
                ("class_code",          "string"),
            ],
            ["NaceCode"] =
            [
                ("nace_code",   "string"),
                ("description", "string"),
                ("section",     "string"),
                ("division",    "string"),
                ("group_code",  "string"),
            ],
        };

    /// <summary>
    /// Returns <c>true</c> when <paramref name="entityName"/> maps to a known SQL table.
    /// Case-insensitive.
    /// </summary>
    public static bool IsKnownEntity(string entityName) =>
        EntityToTable.ContainsKey(entityName);

    /// <summary>
    /// Compiles <paramref name="spec"/> into a <see cref="SqlQueryResult"/> containing
    /// a parameterized SQL string and the corresponding <see cref="SqlParameter"/> array.
    /// </summary>
    /// <exception cref="CompilerException">
    /// Thrown when the entity name is not in <see cref="EntityToTable"/> or an
    /// unsupported filter operator is encountered.
    /// </exception>
    public SqlQueryResult Compile(QuerySpec spec)
    {
        if (!EntityToTable.TryGetValue(spec.Entity, out var table))
            throw new CompilerException($"Entity '{spec.Entity}' has no mapped SQL table.");

        var parameters = new List<SqlParameter>();
        var paramIndex = 0;

        // ── SELECT clause ─────────────────────────────────────────────────────

        var hasAggregations = spec.Aggregations is { Count: > 0 };
        string selectClause;

        if (hasAggregations)
        {
            // Build SELECT list from aggregations
            var aggParts = new List<string>();
            foreach (var agg in spec.Aggregations!)
            {
                var safeField = SanitizeIdentifier(agg.Field);
                aggParts.Add(agg.Type switch
                {
                    AggregationType.Count    => $"COUNT(*) AS [{agg.Name ?? "count"}]",
                    AggregationType.Sum      => $"SUM([{safeField}]) AS [{agg.Name ?? $"sum_{safeField}"}]",
                    AggregationType.Avg      => $"AVG([{safeField}]) AS [{agg.Name ?? $"avg_{safeField}"}]",
                    AggregationType.Min      => $"MIN([{safeField}]) AS [{agg.Name ?? $"min_{safeField}"}]",
                    AggregationType.Max      => $"MAX([{safeField}]) AS [{agg.Name ?? $"max_{safeField}"}]",
                    AggregationType.Terms    => $"[{safeField}], COUNT(*) AS [count]",
                    AggregationType.GroupBy  => $"[{safeField}], COUNT(*) AS [count]",
                    AggregationType.Distinct => $"COUNT(DISTINCT [{safeField}]) AS [{agg.Name ?? $"distinct_{safeField}"}]",
                    _ => throw new CompilerException($"Unknown aggregation type: {agg.Type}")
                });
            }
            selectClause = string.Join(", ", aggParts);
        }
        else
        {
            selectClause = "*";
        }

        var sb = new StringBuilder();

        // TOP clause requires an ORDER BY when combined with paging — we add ORDER BY (SELECT NULL)
        // for determinism when there is a limit but no explicit sort.
        if (spec.Limit.HasValue && !hasAggregations)
            sb.Append($"SELECT TOP {spec.Limit.Value} {selectClause} FROM [{table}]");
        else
            sb.Append($"SELECT {selectClause} FROM [{table}]");

        // ── WHERE clause ──────────────────────────────────────────────────────

        var conditions = new List<string>();

        foreach (var f in spec.Filters ?? [])
        {
            var col = SanitizeIdentifier(f.Field);

            switch (f.Operator)
            {
                case FilterOperator.Eq:
                {
                    var p = AddParam(parameters, ref paramIndex, f.Value);
                    conditions.Add($"[{col}] = {p}");
                    break;
                }
                case FilterOperator.Neq:
                {
                    var p = AddParam(parameters, ref paramIndex, f.Value);
                    conditions.Add($"[{col}] <> {p}");
                    break;
                }
                case FilterOperator.Gt:
                {
                    var p = AddParam(parameters, ref paramIndex, f.Value);
                    conditions.Add($"[{col}] > {p}");
                    break;
                }
                case FilterOperator.Gte:
                {
                    var p = AddParam(parameters, ref paramIndex, f.Value);
                    conditions.Add($"[{col}] >= {p}");
                    break;
                }
                case FilterOperator.Lt:
                {
                    var p = AddParam(parameters, ref paramIndex, f.Value);
                    conditions.Add($"[{col}] < {p}");
                    break;
                }
                case FilterOperator.Lte:
                {
                    var p = AddParam(parameters, ref paramIndex, f.Value);
                    conditions.Add($"[{col}] <= {p}");
                    break;
                }
                case FilterOperator.Contains:
                {
                    // LIKE '%value%' — the % wildcards are in the parameter value, not the SQL text
                    var p = AddParam(parameters, ref paramIndex, $"%{f.Value}%");
                    conditions.Add($"[{col}] LIKE {p}");
                    break;
                }
                case FilterOperator.In:
                {
                    var values = SplitValues(f.Value);
                    var inParams = values.Select(v =>
                    {
                        var p = AddParam(parameters, ref paramIndex, v);
                        return p;
                    }).ToList();
                    conditions.Add($"[{col}] IN ({string.Join(", ", inParams)})");
                    break;
                }
                case FilterOperator.NotIn:
                {
                    var values = SplitValues(f.Value);
                    var inParams = values.Select(v =>
                    {
                        var p = AddParam(parameters, ref paramIndex, v);
                        return p;
                    }).ToList();
                    conditions.Add($"[{col}] NOT IN ({string.Join(", ", inParams)})");
                    break;
                }
                case FilterOperator.IsNull:
                    conditions.Add($"[{col}] IS NULL");
                    break;

                case FilterOperator.IsNotNull:
                    conditions.Add($"[{col}] IS NOT NULL");
                    break;

                default:
                    throw new CompilerException($"Unsupported filter operator: {f.Operator}");
            }
        }

        if (spec.TimeRange is not null)
        {
            var trCol = SanitizeIdentifier(spec.TimeRange.Field);

            if (spec.TimeRange.RelativePeriod is not null)
            {
                var (fromDt, toDt) = ResolveRelativePeriod(spec.TimeRange.RelativePeriod);
                var pFrom = AddParam(parameters, ref paramIndex, fromDt.ToString("yyyy-MM-dd"));
                var pTo   = AddParam(parameters, ref paramIndex, toDt.ToString("yyyy-MM-dd"));
                conditions.Add($"[{trCol}] BETWEEN {pFrom} AND {pTo}");
            }
            else
            {
                if (spec.TimeRange.From is not null)
                {
                    var p = AddParam(parameters, ref paramIndex, spec.TimeRange.From);
                    conditions.Add($"[{trCol}] >= {p}");
                }
                if (spec.TimeRange.To is not null)
                {
                    var p = AddParam(parameters, ref paramIndex, spec.TimeRange.To);
                    conditions.Add($"[{trCol}] <= {p}");
                }
            }
        }

        if (conditions.Count > 0)
            sb.Append(" WHERE ").Append(string.Join(" AND ", conditions));

        // ── GROUP BY clause (for aggregation types that need it) ──────────────

        if (hasAggregations)
        {
            var groupByFields = new List<string>();
            foreach (var agg in spec.Aggregations!)
            {
                if (agg.Type is AggregationType.Terms or AggregationType.GroupBy)
                    groupByFields.Add($"[{SanitizeIdentifier(agg.Field)}]");
            }
            if (groupByFields.Count > 0)
                sb.Append(" GROUP BY ").Append(string.Join(", ", groupByFields));
        }

        // ── ORDER BY clause ───────────────────────────────────────────────────

        if (spec.Sort is { Count: > 0 })
        {
            var sortParts = spec.Sort.Select(s =>
                $"[{SanitizeIdentifier(s.Field)}] {(s.Direction == SortDirection.Asc ? "ASC" : "DESC")}");
            sb.Append(" ORDER BY ").Append(string.Join(", ", sortParts));
        }
        else if (spec.Limit.HasValue && !hasAggregations)
        {
            // TOP requires ORDER BY to be deterministic
            sb.Append(" ORDER BY (SELECT NULL)");
        }

        return new SqlQueryResult(sb.ToString(), [.. parameters]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string AddParam(List<SqlParameter> parameters, ref int index, string value)
    {
        var name = $"@p{index++}";
        parameters.Add(new SqlParameter(name, value));
        return name;
    }

    private static IReadOnlyList<string> SplitValues(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Strips any characters from an identifier that could escape the square-bracket quoting
    /// used in the generated SQL. Square brackets themselves are the only dangerous characters
    /// inside <c>[identifier]</c> syntax.
    /// </summary>
    private static string SanitizeIdentifier(string identifier) =>
        identifier.Replace("]", "").Replace("[", "").Replace("\0", "");

    /// <summary>
    /// Resolves a well-known relative-period token to a (from, to) <see cref="DateTime"/> pair.
    /// The returned values use UTC midnight so date comparisons are consistent.
    /// </summary>
    private static (DateTime From, DateTime To) ResolveRelativePeriod(string period)
    {
        var today = DateTime.UtcNow.Date;
        return period switch
        {
            "today"        => (today,                         today.AddDays(1).AddTicks(-1)),
            "yesterday"    => (today.AddDays(-1),             today.AddTicks(-1)),
            "last_7_days"  => (today.AddDays(-7),             today.AddDays(1).AddTicks(-1)),
            "last_30_days" => (today.AddDays(-30),            today.AddDays(1).AddTicks(-1)),
            "this_month"   => (new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1).AddTicks(-1)),
            "this_year"    => (new DateTime(today.Year, 1, 1),           new DateTime(today.Year + 1, 1, 1).AddTicks(-1)),
            "last_year"    => (new DateTime(today.Year - 1, 1, 1),       new DateTime(today.Year, 1, 1).AddTicks(-1)),
            _              => throw new CompilerException($"Unknown relative period: {period}")
        };
    }
}

/// <summary>
/// The result of <see cref="QuerySpecToSqlCompiler.Compile"/> — a safe parameterized SQL
/// SELECT command with its associated <see cref="Microsoft.Data.SqlClient.SqlParameter"/> array.
/// </summary>
public record SqlQueryResult(string Sql, SqlParameter[] Parameters);
