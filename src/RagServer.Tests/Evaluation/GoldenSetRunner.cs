using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using RagServer.Infrastructure.Catalog;
using RagServer.Infrastructure.Catalog.Entities;
using RagServer.Router;
using Xunit;

namespace RagServer.Tests.Evaluation;

public class GoldenSetRunner
{
    private static CatalogDbContext BuildInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"golden-{Guid.NewGuid()}")
            .Options;
        var db = new CatalogDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task SeedData_FiveEvalQueriesPresent()
    {
        await using var db = BuildInMemoryDb();
        var count = await db.EvalQueries.CountAsync();
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task GoldenSet_CanWriteEvalResults()
    {
        await using var db = BuildInMemoryDb();
        var queries = await db.EvalQueries.ToListAsync();

        foreach (var q in queries)
        {
            db.EvalResults.Add(new EvalResult
            {
                EvalQueryId = q.Id,
                RunId       = "smoke-run-01",
                Response    = "[placeholder]",
                Pipeline    = "docs",
                LatencyMs   = 0,
                Passed      = false,
                CreatedAt   = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
        var resultCount = await db.EvalResults.CountAsync();
        Assert.Equal(queries.Count, resultCount);
    }

    [Fact]
    public async Task GoldenSet_QueriesCoverAllUseCases()
    {
        await using var db = BuildInMemoryDb();
        var useCases = await db.EvalQueries
            .Select(q => q.UseCase)
            .Distinct()
            .ToListAsync();

        Assert.Contains("UC1", useCases);
        Assert.Contains("UC2", useCases);
        Assert.Contains("UC3", useCases);
    }
}
