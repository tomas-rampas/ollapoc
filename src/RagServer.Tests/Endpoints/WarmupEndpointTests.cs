using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using RagServer.Infrastructure;
using Xunit;

namespace RagServer.Tests.Endpoints;

/// <summary>
/// Minimal <see cref="IWebHostEnvironment"/> stub used by warmup endpoint tests.
/// </summary>
file sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath        { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string EnvironmentName    { get; set; } = "Development";
    public string ApplicationName    { get; set; } = "RagServer";
    public string ContentRootPath    { get; set; } = string.Empty;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}

public class WarmupEndpointTests
{
    /// <summary>
    /// Creates a temp wwwroot directory with the production DemoQueries.json content (18 queries),
    /// constructs a <see cref="DemoQueriesService"/>, and returns it together with the temp path.
    /// </summary>
    private static (DemoQueriesService Svc, string TempDir) BuildServiceWithRealJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        var json = """
            [
              { "text": "What is a counterparty in financial services?", "pipeline": "Docs" },
              { "text": "How does KYC onboarding work?", "pipeline": "Docs" },
              { "text": "What is a DVP settlement instruction?", "pipeline": "Docs" },
              { "text": "What are trading books and banking books?", "pipeline": "Docs" },
              { "text": "How is FATCA status determined for a counterparty?", "pipeline": "Docs" },
              { "text": "What is a Critical Data Element?", "pipeline": "Docs" },
              { "text": "What are the mandatory attributes for Book?", "pipeline": "Metadata" },
              { "text": "Who is the data owner of Legal Name for Client Account?", "pipeline": "Metadata" },
              { "text": "What rules are defined for Counterparty?", "pipeline": "Metadata" },
              { "text": "What are the mandatory rules for Settlement Instruction?", "pipeline": "Metadata" },
              { "text": "What critical data elements does Currency have?", "pipeline": "Metadata" },
              { "text": "What relationships does Counterparty have?", "pipeline": "Metadata" },
              { "text": "Show me the top 10 trades with status FAILED sorted by amount descending", "pipeline": "Data" },
              { "text": "How many trades were settled last month?", "pipeline": "Data" },
              { "text": "List all counterparties with more than 5 active trades", "pipeline": "Data" },
              { "text": "What were the total notional amounts by currency last quarter?", "pipeline": "Data" },
              { "text": "Show me all settlement failures in the last 7 days", "pipeline": "Data" },
              { "text": "Find all open trades where notional exceeds 1 million", "pipeline": "Data" }
            ]
            """;
        File.WriteAllText(Path.Combine(tempDir, "DemoQueries.json"), json);

        var env = new FakeWebHostEnvironment { WebRootPath = tempDir };
        return (new DemoQueriesService(env, NullLogger<DemoQueriesService>.Instance), tempDir);
    }

    [Fact]
    public void Given_DemoQueries_When_GetQueries_Called_Then_ReturnsAllEighteenQueries()
    {
        // Arrange
        var (svc, tempDir) = BuildServiceWithRealJson();
        try
        {
            // Act
            var queries = svc.GetQueries();

            // Assert
            Assert.Equal(18, queries.Count);

            var docCount  = queries.Count(q => q.Pipeline == "Docs");
            var metaCount = queries.Count(q => q.Pipeline == "Metadata");
            var dataCount = queries.Count(q => q.Pipeline == "Data");

            Assert.Equal(6, docCount);
            Assert.Equal(6, metaCount);
            Assert.Equal(6, dataCount);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Given_EmptyQueryList_When_LoopExecuted_Then_WarmupCountIsZero()
    {
        // Arrange — empty wwwroot (no DemoQueries.json) → service returns empty list
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var env = new FakeWebHostEnvironment { WebRootPath = tempDir };
            var svc = new DemoQueriesService(env, NullLogger<DemoQueriesService>.Instance);

            // Act — simulate the warmup loop from the endpoint
            var queries = svc.GetQueries();
            var results = new List<object>(queries.Count);
            foreach (var q in queries)
                results.Add(new { query = q.Text, pipeline = q.Pipeline });

            // Assert
            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
