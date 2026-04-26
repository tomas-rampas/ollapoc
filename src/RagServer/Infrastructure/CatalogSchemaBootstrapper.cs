using Microsoft.EntityFrameworkCore;
using RagServer.Infrastructure.Catalog;

namespace RagServer.Infrastructure;

public sealed class CatalogSchemaBootstrapper(
    IServiceScopeFactory scopeFactory,
    ILogger<CatalogSchemaBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        try
        {
            await db.Database.EnsureCreatedAsync(ct);
            logger.LogInformation("CatalogSchemaBootstrapper: schema ready");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CatalogSchemaBootstrapper failed — continuing without SQL Server catalog");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
