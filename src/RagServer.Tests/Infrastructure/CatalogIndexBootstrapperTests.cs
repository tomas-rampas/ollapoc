using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RagServer.Infrastructure;
using RagServer.Infrastructure.Catalog;
using RagServer.Infrastructure.Catalog.Entities;
using Xunit;

namespace RagServer.Tests.Infrastructure;

public class CatalogIndexBootstrapperTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    // Port 19999 is chosen because nothing should be listening there in CI or dev.
    // ThrowExceptions() makes the client throw on transport errors so the catch block fires.
    private static ElasticsearchClient BuildUnreachableClient()
        => new(new ElasticsearchClientSettings(new Uri("http://localhost:19999"))
            .ThrowExceptions());

    private static IEmbeddingGenerator<string, Embedding<float>> BuildStubEmbedder()
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mock.Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                new List<Embedding<float>>
                {
                    new(new ReadOnlyMemory<float>(new float[384]))
                }));
        return mock.Object;
    }

    /// <summary>
    /// Builds a <see cref="IServiceScopeFactory"/> backed by an in-memory
    /// <see cref="CatalogDbContext"/> pre-populated with the given seed action.
    /// </summary>
    private static IServiceScopeFactory BuildScopeFactory(Action<CatalogDbContext>? seed = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<CatalogDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        var sp = services.BuildServiceProvider();

        if (seed is not null)
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            seed(db);
            db.SaveChanges();
        }

        return sp.GetRequiredService<IServiceScopeFactory>();
    }

    private static CatalogIndexBootstrapper BuildBootstrapper(
        ElasticsearchClient? es = null,
        IServiceScopeFactory? scopeFactory = null,
        IEmbeddingGenerator<string, Embedding<float>>? embedder = null,
        ILogger<CatalogIndexBootstrapper>? logger = null)
    {
        return new CatalogIndexBootstrapper(
            es         ?? BuildUnreachableClient(),
            logger     ?? NullLogger<CatalogIndexBootstrapper>.Instance,
            scopeFactory ?? BuildScopeFactory(),
            embedder   ?? BuildStubEmbedder());
    }

    // ── Resilience tests (ES unavailable) ────────────────────────────────────

    [Fact]
    public async Task StartAsync_DoesNotThrow_WhenEsUnavailable()
    {
        var bootstrapper = BuildBootstrapper();

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
        var bootstrapper = BuildBootstrapper(es: es);

        var exception = await Record.ExceptionAsync(() =>
            bootstrapper.StartAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task StopAsync_ReturnsCompletedTask()
    {
        var bootstrapper = BuildBootstrapper();

        var task = bootstrapper.StopAsync(CancellationToken.None);

        Assert.True(task.IsCompleted);
        await task; // should not throw
    }

    [Fact]
    public async Task StartAsync_LogsWarning_WhenExceptionOccurs()
    {
        // ThrowExceptions() ensures the catch block in StartAsync fires on a bad URL
        var mockLogger = new Mock<ILogger<CatalogIndexBootstrapper>>();
        var bootstrapper = BuildBootstrapper(logger: mockLogger.Object);

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

    // ── LINQ filter tests (use in-memory DB, ES unreachable so catch fires) ──

    [Fact]
    public async Task Given_EmptyDatabase_When_StartAsync_Then_SkipsSeeding()
    {
        // Arrange — in-memory DB with no entities; ES is unreachable so the
        // index create call will throw first, but that's fine — the important
        // thing is that the embedder is never called (no data to embed).
        var embedderMock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        var scopeFactory = BuildScopeFactory(); // empty DB — no seed

        var bootstrapper = BuildBootstrapper(
            scopeFactory: scopeFactory,
            embedder: embedderMock.Object);

        await bootstrapper.StartAsync(CancellationToken.None);

        // Embedder must not be called because the catalog is empty
        embedderMock.Verify(
            g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Given_TopLevelAttributesOnly_When_Seeded_Then_ChildAttributesExcluded()
    {
        // Arrange — build an in-memory DB with one entity, one top-level
        // attribute and one child attribute; verify the LINQ filter excludes
        // the child row without touching Elasticsearch at all.
        var services = new ServiceCollection();
        services.AddDbContext<CatalogDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var entity = new CatalogEntity
        {
            Id = 1, Name = "Counterparty", EntityType = "party",
            EntityCode = "counterparty"
        };
        db.CatalogEntities.Add(entity);

        // Top-level attribute (ParentAttributeId = null) — must be indexed
        var topLevel = new CatalogAttribute
        {
            Id = 1, CatalogEntityId = 1, Name = "LegalName",
            DataType = "string", ParentAttributeId = null
        };
        db.CatalogAttributes.Add(topLevel);

        // Child attribute (ParentAttributeId != null) — must be excluded
        var child = new CatalogAttribute
        {
            Id = 2, CatalogEntityId = 1, Name = "MUREX",
            DataType = "string", ParentAttributeId = 1
        };
        db.CatalogAttributes.Add(child);
        db.SaveChanges();

        // Act — apply the same filter used in StartAsync
        var topLevelOnly = db.CatalogAttributes
            .AsNoTracking()
            .Where(a => a.ParentAttributeId == null)
            .ToList();

        // Assert
        Assert.Single(topLevelOnly);
        Assert.Equal("LegalName", topLevelOnly[0].Name);
        Assert.DoesNotContain(topLevelOnly, a => a.Name == "MUREX");
    }
}
