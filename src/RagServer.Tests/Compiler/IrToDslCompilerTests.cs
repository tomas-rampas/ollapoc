using Elastic.Clients.Elasticsearch.QueryDsl;
using RagServer.Compiler;
using Xunit;

namespace RagServer.Tests.Compiler;

public class IrToDslCompilerTests
{
    private static readonly IrToDslCompiler Compiler = new();

    private static QuerySpec SimpleSpec(
        string entity = "Trade",
        IReadOnlyList<Filter>? filters = null,
        TimeRange? timeRange = null,
        IReadOnlyList<SortClause>? sort = null,
        IReadOnlyList<Aggregation>? aggregations = null,
        int? limit = null) =>
        new(entity, filters ?? [], timeRange, sort ?? [], aggregations ?? [], limit);

    [Fact]
    public void Given_SimpleEqFilter_When_Compiled_Then_GeneratesTermQuery()
    {
        var spec = SimpleSpec(filters: [new Filter("Status", FilterOperator.Eq, "ACTIVE")]);

        var request = Compiler.Compile(spec);

        Assert.NotNull(request.Query);
        // Single Eq filter → Bool.Must with one TermQuery
        var boolQ = request.Query!.Bool;
        Assert.NotNull(boolQ);
        Assert.NotNull(boolQ!.Must);
        Assert.Single(boolQ.Must!);
        Assert.NotNull(boolQ.Must!.First().Term);
    }

    [Fact]
    public void Given_TimeRange_When_Compiled_Then_GeneratesDateRangeQuery()
    {
        var spec = SimpleSpec(timeRange: new TimeRange("TradeDate", "2025-01-01", "2025-12-31"));

        var request = Compiler.Compile(spec);

        Assert.NotNull(request.Query);
        var boolQ = request.Query!.Bool;
        Assert.NotNull(boolQ);
        var rangeClause = boolQ!.Must?.FirstOrDefault(q => q.Range is not null);
        Assert.NotNull(rangeClause);
    }

    [Fact]
    public void Given_ContainsFilter_When_Compiled_Then_GeneratesMatchQuery()
    {
        var spec = SimpleSpec(filters: [new Filter("Description", FilterOperator.Contains, "equity")]);

        var request = Compiler.Compile(spec);

        var boolQ = request.Query!.Bool;
        Assert.NotNull(boolQ);
        var matchClause = boolQ!.Must?.FirstOrDefault(q => q.Match is not null);
        Assert.NotNull(matchClause);
        Assert.Equal("equity", matchClause!.Match!.Query);
    }

    [Fact]
    public void Given_InFilter_When_Compiled_Then_GeneratesTermsQuery()
    {
        var spec = SimpleSpec(filters: [new Filter("Status", FilterOperator.In, "ACTIVE,PENDING")]);

        var request = Compiler.Compile(spec);

        var boolQ = request.Query!.Bool;
        Assert.NotNull(boolQ);
        var termsClause = boolQ!.Must?.FirstOrDefault(q => q.Terms is not null);
        Assert.NotNull(termsClause);
    }

    [Fact]
    public void Given_MultipleFilters_When_Compiled_Then_GeneratesBoolMust()
    {
        var spec = SimpleSpec(filters:
        [
            new Filter("Status", FilterOperator.Eq, "ACTIVE"),
            new Filter("Currency", FilterOperator.Eq, "USD"),
        ]);

        var request = Compiler.Compile(spec);

        var boolQ = request.Query!.Bool;
        Assert.NotNull(boolQ);
        Assert.NotNull(boolQ!.Must);
        Assert.Equal(2, boolQ.Must!.Count());
    }

    [Fact]
    public void Given_SortAsc_When_Compiled_Then_SortOptionsNotNull()
    {
        var spec = SimpleSpec(sort: [new SortClause("Notional", SortDirection.Asc)]);

        var request = Compiler.Compile(spec);

        Assert.NotNull(request.Sort);
        Assert.Single(request.Sort!);
    }

    [Fact]
    public void Given_CountAggregation_When_Compiled_Then_AggregationsNotNull()
    {
        var spec = SimpleSpec(aggregations: [new Aggregation(AggregationType.Count, "TradeId", "total")]);

        var request = Compiler.Compile(spec);

        Assert.NotNull(request.Aggregations);
        Assert.True(request.Aggregations!.ContainsKey("total"));
    }

    [Fact]
    public void Given_TermsAggregation_When_Compiled_Then_AggregationsNotNull()
    {
        var spec = SimpleSpec(aggregations: [new Aggregation(AggregationType.Terms, "Status", null)]);

        var request = Compiler.Compile(spec);

        Assert.NotNull(request.Aggregations);
        Assert.True(request.Aggregations!.ContainsKey("terms_status"));
    }

    [Fact]
    public void Given_Limit_When_Compiled_Then_SetsSize()
    {
        var spec = SimpleSpec(limit: 5);

        var request = Compiler.Compile(spec);

        Assert.Equal(5, request.Size);
    }

    [Fact]
    public void Given_NoFilters_When_Compiled_Then_QueryIsNotNull()
    {
        var spec = SimpleSpec();

        var request = Compiler.Compile(spec);

        Assert.NotNull(request.Query);
        Assert.NotNull(request.Query!.MatchAll);
    }

    [Fact]
    public void Given_GtFilter_When_Compiled_Then_GeneratesRangeQuery()
    {
        var spec = SimpleSpec(filters: [new Filter("Notional", FilterOperator.Gt, "1000000")]);

        var request = Compiler.Compile(spec);

        var boolQ = request.Query!.Bool;
        Assert.NotNull(boolQ);
        var rangeClause = boolQ!.Must?.FirstOrDefault(q => q.Range is not null);
        Assert.NotNull(rangeClause);
    }

    [Fact]
    public void Given_NeqFilter_When_Compiled_Then_GeneratesQueryWithMustNot()
    {
        var spec = SimpleSpec(filters: [new Filter("Status", FilterOperator.Neq, "CANCELLED")]);

        var request = Compiler.Compile(spec);

        var boolQ = request.Query!.Bool;
        Assert.NotNull(boolQ);
        Assert.NotNull(boolQ!.MustNot);
        var mustNotClause = boolQ.MustNot!.FirstOrDefault(q => q.Term is not null);
        Assert.NotNull(mustNotClause);
    }
}
