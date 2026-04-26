using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RagServer.Infrastructure;
using Xunit;

namespace RagServer.Tests.Infrastructure;

public class IndexBootstrapperTests
{
    // Port 19999 is chosen because nothing should be listening there in CI or dev.
    // ThrowExceptions() makes the client throw on transport errors so the catch block fires.
    private static ElasticsearchClient BuildUnreachableClient()
        => new(new ElasticsearchClientSettings(new Uri("http://localhost:19999"))
            .ThrowExceptions());

    [Fact]
    public async Task StartAsync_DoesNotThrow_WhenEsUnavailable()
    {
        var es = BuildUnreachableClient();
        var bootstrapper = new IndexBootstrapper(es, NullLogger<IndexBootstrapper>.Instance);

        var exception = await Record.ExceptionAsync(() =>
            bootstrapper.StartAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow_WhenEsReturnsError()
    {
        // Different unreachable port — same expectation: catch block swallows the error
        var es = new ElasticsearchClient(
            new ElasticsearchClientSettings(new Uri("http://127.0.0.1:19998"))
                .ThrowExceptions());
        var bootstrapper = new IndexBootstrapper(es, NullLogger<IndexBootstrapper>.Instance);

        var exception = await Record.ExceptionAsync(() =>
            bootstrapper.StartAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task StopAsync_ReturnsCompletedTask()
    {
        var es = BuildUnreachableClient();
        var bootstrapper = new IndexBootstrapper(es, NullLogger<IndexBootstrapper>.Instance);

        var task = bootstrapper.StopAsync(CancellationToken.None);

        Assert.True(task.IsCompleted);
        await task; // should not throw
    }

    [Fact]
    public async Task StartAsync_LogsWarning_WhenExceptionOccurs()
    {
        // ThrowExceptions() ensures the catch block in StartAsync fires on a bad URL
        var es = BuildUnreachableClient();
        var mockLogger = new Mock<ILogger<IndexBootstrapper>>();

        var bootstrapper = new IndexBootstrapper(es, mockLogger.Object);
        await bootstrapper.StartAsync(CancellationToken.None);

        // The catch block calls logger.LogWarning(ex, ...) which dispatches to ILogger.Log at Warning level
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
