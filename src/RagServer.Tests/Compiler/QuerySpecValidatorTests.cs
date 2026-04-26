using RagServer.Compiler;
using Xunit;

namespace RagServer.Tests.Compiler;

public class QuerySpecValidatorTests
{
    private static readonly QuerySpecValidator Validator = new();

    private static readonly IReadOnlySet<string> KnownEntities =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Trade", "Settlement", "Counterparty" };

    private static QuerySpec ValidSpec() =>
        new("Trade",
            [new Filter("Status", FilterOperator.Eq, "ACTIVE")],
            null,
            [new SortClause("TradeDate", SortDirection.Desc)],
            [new Aggregation(AggregationType.Count, "TradeId", null)],
            10);

    [Fact]
    public void Given_ValidSpec_When_Validated_Then_IsValid()
    {
        var result = Validator.Validate(ValidSpec(), KnownEntities);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Given_EmptyEntity_When_Validated_Then_HasError()
    {
        var spec = ValidSpec() with { Entity = "" };

        var result = Validator.Validate(spec, KnownEntities);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Given_UnknownEntity_When_Validated_Then_HasError()
    {
        var spec = ValidSpec() with { Entity = "UnknownEntity" };

        var result = Validator.Validate(spec, KnownEntities);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("UnknownEntity"));
    }

    [Fact]
    public void Given_InvalidTimeRange_FromAfterTo_When_Validated_Then_HasError()
    {
        var spec = ValidSpec() with
        {
            TimeRange = new TimeRange("TradeDate", "2025-12-31", "2025-01-01")
        };

        var result = Validator.Validate(spec, KnownEntities);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("TimeRange"));
    }

    [Fact]
    public void Given_FilterWithEmptyField_When_Validated_Then_HasError()
    {
        var spec = ValidSpec() with
        {
            Filters = [new Filter("", FilterOperator.Eq, "ACTIVE")]
        };

        var result = Validator.Validate(spec, KnownEntities);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Filter.Field"));
    }

    [Fact]
    public void Given_EmptySortField_When_Validated_Then_HasError()
    {
        var spec = ValidSpec() with
        {
            Sort = [new SortClause("", SortDirection.Asc)]
        };

        var result = Validator.Validate(spec, KnownEntities);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SortClause.Field"));
    }

    [Fact]
    public void Given_EmptyAggField_When_Validated_Then_HasError()
    {
        var spec = ValidSpec() with
        {
            Aggregations = [new Aggregation(AggregationType.Sum, "", null)]
        };

        var result = Validator.Validate(spec, KnownEntities);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Aggregation.Field"));
    }

    [Fact]
    public void Given_NullFiltersAndAggs_When_Validated_Then_IsValid()
    {
        var spec = new QuerySpec("Trade", [], null, [], [], null);

        var result = Validator.Validate(spec, KnownEntities);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Given_IsNullFilterWithEmptyValue_When_Validated_Then_IsValid()
    {
        var spec = new QuerySpec("Trade",
            [new Filter("Status", FilterOperator.IsNull, "")],
            null, [], [], null);

        var result = Validator.Validate(spec, KnownEntities);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Given_EqFilterWithEmptyValue_When_Validated_Then_HasError()
    {
        var spec = new QuerySpec("Trade",
            [new Filter("Status", FilterOperator.Eq, "")],
            null, [], [], null);

        var result = Validator.Validate(spec, KnownEntities);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Value") && e.Contains("Eq"));
    }
}
