using RagServer.Compiler;
using Xunit;

namespace RagServer.Tests.Compiler;

public class QuerySpecToSqlCompilerTests
{
    private static readonly QuerySpecToSqlCompiler Compiler = new();

    private static QuerySpec MakeSpec(
        string entity = "Counterparty",
        IReadOnlyList<Filter>? filters = null,
        TimeRange? timeRange = null,
        IReadOnlyList<SortClause>? sort = null,
        IReadOnlyList<Aggregation>? aggregations = null,
        int? limit = null) =>
        new(entity, filters ?? [], timeRange, sort ?? [], aggregations ?? [], limit);

    // ── Basic ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Given_NoFilters_When_Compiled_Then_SelectAllFromTable()
    {
        var spec   = MakeSpec();
        var result = Compiler.Compile(spec);

        Assert.Contains("SELECT * FROM [Counterparties]", result.Sql);
        Assert.Empty(result.Parameters);
    }

    [Fact]
    public void Given_UnknownEntity_When_Compiled_Then_ThrowsCompilerException()
    {
        var spec = MakeSpec(entity: "Ghost");
        Assert.Throws<CompilerException>(() => Compiler.Compile(spec));
    }

    [Fact]
    public void IsKnownEntity_Counterparty_ReturnsTrue()
    {
        Assert.True(QuerySpecToSqlCompiler.IsKnownEntity("Counterparty"));
        Assert.True(QuerySpecToSqlCompiler.IsKnownEntity("counterparty")); // case-insensitive
    }

    [Fact]
    public void IsKnownEntity_Unknown_ReturnsFalse()
    {
        Assert.False(QuerySpecToSqlCompiler.IsKnownEntity("Trade"));
    }

    // ── Single Eq filter ──────────────────────────────────────────────────────

    [Fact]
    public void Given_EqFilter_When_Compiled_Then_GeneratesParameterizedWhereClause()
    {
        var spec = MakeSpec(filters: [new Filter("status", FilterOperator.Eq, "Active")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("WHERE [status] = @p0", result.Sql);
        Assert.Single(result.Parameters);
        Assert.Equal("Active", result.Parameters[0].Value?.ToString());
    }

    [Fact]
    public void Given_NeqFilter_When_Compiled_Then_GeneratesNotEqualClause()
    {
        var spec = MakeSpec(filters: [new Filter("status", FilterOperator.Neq, "Cancelled")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[status] <> @p0", result.Sql);
        Assert.Single(result.Parameters);
    }

    // ── In filter ────────────────────────────────────────────────────────────

    [Fact]
    public void Given_InFilter_When_Compiled_Then_GeneratesParameterizedInClause()
    {
        var spec = MakeSpec(filters: [new Filter("status", FilterOperator.In, "Active,Pending")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[status] IN (@p0, @p1)", result.Sql);
        Assert.Equal(2, result.Parameters.Length);
        Assert.Equal("Active",  result.Parameters[0].Value?.ToString());
        Assert.Equal("Pending", result.Parameters[1].Value?.ToString());
    }

    [Fact]
    public void Given_NotInFilter_When_Compiled_Then_GeneratesNotInClause()
    {
        var spec = MakeSpec(filters: [new Filter("status", FilterOperator.NotIn, "Cancelled,Expired")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[status] NOT IN (@p0, @p1)", result.Sql);
        Assert.Equal(2, result.Parameters.Length);
    }

    // ── IsNull / IsNotNull ────────────────────────────────────────────────────

    [Fact]
    public void Given_IsNullFilter_When_Compiled_Then_GeneratesIsNullClauseWithNoParameter()
    {
        var spec = MakeSpec(filters: [new Filter("short_name", FilterOperator.IsNull, "")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[short_name] IS NULL", result.Sql);
        Assert.Empty(result.Parameters);
    }

    [Fact]
    public void Given_IsNotNullFilter_When_Compiled_Then_GeneratesIsNotNullClause()
    {
        var spec = MakeSpec(filters: [new Filter("lei", FilterOperator.IsNotNull, "")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[lei] IS NOT NULL", result.Sql);
        Assert.Empty(result.Parameters);
    }

    // ── Contains ─────────────────────────────────────────────────────────────

    [Fact]
    public void Given_ContainsFilter_When_Compiled_Then_GeneratesLikeWithWildcards()
    {
        var spec = MakeSpec(filters: [new Filter("legal_name", FilterOperator.Contains, "Barclays")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[legal_name] LIKE @p0", result.Sql);
        Assert.Single(result.Parameters);
        Assert.Equal("%Barclays%", result.Parameters[0].Value?.ToString());
    }

    // ── Range operators ───────────────────────────────────────────────────────

    [Fact]
    public void Given_GtFilter_When_Compiled_Then_GeneratesGreaterThanClause()
    {
        var spec = MakeSpec(entity: "Settlement", filters: [new Filter("amount", FilterOperator.Gt, "1000000")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[amount] > @p0", result.Sql);
        Assert.Single(result.Parameters);
    }

    [Fact]
    public void Given_GteFilter_When_Compiled_Then_GeneratesGreaterThanOrEqualClause()
    {
        var spec = MakeSpec(entity: "Settlement", filters: [new Filter("amount", FilterOperator.Gte, "500000")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[amount] >= @p0", result.Sql);
    }

    [Fact]
    public void Given_LtFilter_When_Compiled_Then_GeneratesLessThanClause()
    {
        var spec = MakeSpec(entity: "Settlement", filters: [new Filter("amount", FilterOperator.Lt, "100")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[amount] < @p0", result.Sql);
    }

    [Fact]
    public void Given_LteFilter_When_Compiled_Then_GeneratesLessThanOrEqualClause()
    {
        var spec = MakeSpec(entity: "Settlement", filters: [new Filter("amount", FilterOperator.Lte, "99")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[amount] <= @p0", result.Sql);
    }

    // ── Sort ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Given_AscSort_When_Compiled_Then_GeneratesOrderByAsc()
    {
        var spec = MakeSpec(sort: [new SortClause("legal_name", SortDirection.Asc)]);

        var result = Compiler.Compile(spec);

        Assert.Contains("ORDER BY [legal_name] ASC", result.Sql);
    }

    [Fact]
    public void Given_DescSort_When_Compiled_Then_GeneratesOrderByDesc()
    {
        var spec = MakeSpec(sort: [new SortClause("status", SortDirection.Desc)]);

        var result = Compiler.Compile(spec);

        Assert.Contains("ORDER BY [status] DESC", result.Sql);
    }

    // ── Limit ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Given_LimitSet_When_Compiled_Then_GeneratesSelectTopN()
    {
        var spec = MakeSpec(limit: 5);

        var result = Compiler.Compile(spec);

        Assert.Contains("SELECT TOP 5 * FROM [Counterparties]", result.Sql);
    }

    [Fact]
    public void Given_LimitWithNoSort_When_Compiled_Then_AddsOrderBySelectNull()
    {
        var spec = MakeSpec(limit: 10);

        var result = Compiler.Compile(spec);

        // Without explicit sort, ORDER BY (SELECT NULL) is appended for TOP determinism
        Assert.Contains("ORDER BY (SELECT NULL)", result.Sql);
    }

    [Fact]
    public void Given_LimitWithSort_When_Compiled_Then_DoesNotAddSelectNullOrderBy()
    {
        var spec = MakeSpec(limit: 10, sort: [new SortClause("legal_name", SortDirection.Asc)]);

        var result = Compiler.Compile(spec);

        Assert.DoesNotContain("(SELECT NULL)", result.Sql);
        Assert.Contains("ORDER BY [legal_name] ASC", result.Sql);
    }

    // ── TimeRange ─────────────────────────────────────────────────────────────

    [Fact]
    public void Given_TimeRangeWithFromAndTo_When_Compiled_Then_GeneratesRangeConditions()
    {
        var spec = MakeSpec(
            entity: "Settlement",
            timeRange: new TimeRange("settlement_date", "2024-01-01", "2024-12-31"));

        var result = Compiler.Compile(spec);

        Assert.Contains("[settlement_date] >= @p0", result.Sql);
        Assert.Contains("[settlement_date] <= @p1", result.Sql);
        Assert.Equal(2, result.Parameters.Length);
    }

    [Fact]
    public void Given_RelativePeriod_Today_When_Compiled_Then_GeneratesBetweenWithDates()
    {
        var spec = MakeSpec(
            entity: "Settlement",
            timeRange: new TimeRange("settlement_date", null, null, "today"));

        var result = Compiler.Compile(spec);

        Assert.Contains("[settlement_date] BETWEEN @p0 AND @p1", result.Sql);
        Assert.Equal(2, result.Parameters.Length);
        // Both parameters must be real dates, not ES date-math strings
        Assert.DoesNotContain("now/d", result.Parameters[0].Value?.ToString() ?? "");
    }

    [Fact]
    public void Given_RelativePeriod_LastYear_When_Compiled_Then_GeneratesCorrectDates()
    {
        var spec = MakeSpec(
            entity: "Settlement",
            timeRange: new TimeRange("settlement_date", null, null, "last_year"));

        var result = Compiler.Compile(spec);

        Assert.Contains("[settlement_date] BETWEEN @p0 AND @p1", result.Sql);
        var fromVal = result.Parameters[0].Value?.ToString() ?? "";
        // "last_year" starts Jan 1 of previous year
        Assert.StartsWith($"{DateTime.UtcNow.Year - 1}-01-01", fromVal);
    }

    [Fact]
    public void Given_UnknownRelativePeriod_When_Compiled_Then_ThrowsCompilerException()
    {
        var spec = MakeSpec(
            entity: "Settlement",
            timeRange: new TimeRange("settlement_date", null, null, "last_quarter"));

        Assert.Throws<CompilerException>(() => Compiler.Compile(spec));
    }

    // ── SQL injection guard ───────────────────────────────────────────────────

    [Fact]
    public void Given_FilterValueWithSqlInjection_When_Compiled_Then_ValueIsParameterizedNotInlined()
    {
        const string injection = "'; DROP TABLE Counterparties; --";
        var spec = MakeSpec(filters: [new Filter("legal_name", FilterOperator.Eq, injection)]);

        var result = Compiler.Compile(spec);

        // The raw injection string must NOT appear in the SQL text
        Assert.DoesNotContain(injection, result.Sql);
        // It must be in the parameter value instead
        Assert.Single(result.Parameters);
        Assert.Equal(injection, result.Parameters[0].Value?.ToString());
    }

    [Fact]
    public void Given_InFilterWithInjectionValues_When_Compiled_Then_AllValuesParameterized()
    {
        var spec = MakeSpec(filters: [new Filter("status", FilterOperator.In, "Active,'; DROP TABLE--")]);

        var result = Compiler.Compile(spec);

        // SQL string must only reference parameter placeholders, not raw values
        Assert.DoesNotContain("DROP TABLE", result.Sql);
        Assert.Equal(2, result.Parameters.Length);
        Assert.Equal("'; DROP TABLE--", result.Parameters[1].Value?.ToString());
    }

    // ── Aggregations ─────────────────────────────────────────────────────────

    [Fact]
    public void Given_CountAggregation_When_Compiled_Then_GeneratesCountStar()
    {
        var spec = MakeSpec(aggregations: [new Aggregation(AggregationType.Count, "counterparty_id", "total")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("COUNT(*) AS [total]", result.Sql);
    }

    [Fact]
    public void Given_SumAggregation_When_Compiled_Then_GeneratesSumColumn()
    {
        var spec = MakeSpec(entity: "Settlement", aggregations: [new Aggregation(AggregationType.Sum, "amount", "total_amount")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("SUM([amount]) AS [total_amount]", result.Sql);
    }

    [Fact]
    public void Given_TermsAggregation_When_Compiled_Then_GeneratesGroupBy()
    {
        var spec = MakeSpec(aggregations: [new Aggregation(AggregationType.Terms, "status", null)]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[status], COUNT(*) AS [count]", result.Sql);
        Assert.Contains("GROUP BY [status]", result.Sql);
    }

    [Fact]
    public void Given_DistinctAggregation_When_Compiled_Then_GeneratesCountDistinct()
    {
        var spec = MakeSpec(aggregations: [new Aggregation(AggregationType.Distinct, "entity_type", "unique_types")]);

        var result = Compiler.Compile(spec);

        Assert.Contains("COUNT(DISTINCT [entity_type]) AS [unique_types]", result.Sql);
    }

    // ── Multiple filters ──────────────────────────────────────────────────────

    [Fact]
    public void Given_MultipleFilters_When_Compiled_Then_CombinedWithAnd()
    {
        var spec = MakeSpec(filters:
        [
            new Filter("status",               FilterOperator.Eq, "Active"),
            new Filter("incorporation_country", FilterOperator.Eq, "GB"),
        ]);

        var result = Compiler.Compile(spec);

        Assert.Contains("[status] = @p0", result.Sql);
        Assert.Contains("[incorporation_country] = @p1", result.Sql);
        Assert.Contains("AND", result.Sql);
        Assert.Equal(2, result.Parameters.Length);
    }

    // ── Entity mapping ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Counterparty",          "Counterparties")]
    [InlineData("Country",               "Countries")]
    [InlineData("Currency",              "Currencies")]
    [InlineData("Book",                  "Books")]
    [InlineData("Settlement",            "Settlements")]
    [InlineData("ClientAccount",         "ClientAccounts")]
    [InlineData("SettlementInstruction", "SettlementInstructions")]
    [InlineData("Region",                "Regions")]
    [InlineData("Location",              "Locations")]
    [InlineData("UkSicCode",             "UkSicCodes")]
    [InlineData("NaceCode",              "NaceCodes")]
    public void Given_KnownEntity_When_Compiled_Then_MapsToCorrectTable(string entity, string expectedTable)
    {
        var spec   = MakeSpec(entity: entity);
        var result = Compiler.Compile(spec);

        Assert.Contains($"FROM [{expectedTable}]", result.Sql);
    }
}
