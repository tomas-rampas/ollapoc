using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using RagServer.Infrastructure;
using Xunit;

namespace RagServer.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="IWebHostEnvironment"/> stub — only <see cref="WebRootPath"/> is used
/// by <see cref="DemoQueriesService"/>.
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

public class DemoQueriesServiceTests
{
    [Fact]
    public void Given_ValidJson_When_GetQueries_Then_Returns9Queries()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var json = """
                [
                  { "text": "Query 1", "pipeline": "Docs" },
                  { "text": "Query 2", "pipeline": "Docs" },
                  { "text": "Query 3", "pipeline": "Docs" },
                  { "text": "Query 4", "pipeline": "Metadata" },
                  { "text": "Query 5", "pipeline": "Metadata" },
                  { "text": "Query 6", "pipeline": "Metadata" },
                  { "text": "Query 7", "pipeline": "Data" },
                  { "text": "Query 8", "pipeline": "Data" },
                  { "text": "Query 9", "pipeline": "Data" }
                ]
                """;
            File.WriteAllText(Path.Combine(tempDir, "DemoQueries.json"), json);

            var env = new FakeWebHostEnvironment { WebRootPath = tempDir };

            // Act
            var svc = new DemoQueriesService(env, NullLogger<DemoQueriesService>.Instance);
            var queries = svc.GetQueries();

            // Assert
            Assert.Equal(9, queries.Count);
            Assert.Equal("Query 1", queries[0].Text);
            Assert.Equal("Docs", queries[0].Pipeline);
            Assert.Equal("Query 9", queries[8].Text);
            Assert.Equal("Data", queries[8].Pipeline);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Given_MissingFile_When_GetQueries_Then_ReturnsEmpty()
    {
        // Arrange — point to a directory that does not contain DemoQueries.json
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var env = new FakeWebHostEnvironment { WebRootPath = tempDir };

            // Act
            var svc = new DemoQueriesService(env, NullLogger<DemoQueriesService>.Instance);
            var queries = svc.GetQueries();

            // Assert
            Assert.Empty(queries);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
