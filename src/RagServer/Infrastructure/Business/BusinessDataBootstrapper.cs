using Microsoft.EntityFrameworkCore;

namespace RagServer.Infrastructure.Business;

/// <summary>
/// Applies EF Core migrations (or EnsureCreated for in-memory) for <see cref="BusinessDataContext"/> at startup.
/// Swallows exceptions so the app starts even if the database is temporarily unavailable.
/// </summary>
public sealed class BusinessDataBootstrapper(
    IServiceScopeFactory scopeFactory,
    ILogger<BusinessDataBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BusinessDataContext>();

            // EnsureCreated works for both InMemory and SQL Server when no migrations exist yet.
            // Once migrations are added, replace with db.Database.MigrateAsync(ct).
            await db.Database.EnsureCreatedAsync(ct);
            logger.LogInformation("BusinessDataBootstrapper: business data schema ready");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BusinessDataBootstrapper failed — app will start but business tables may be missing");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
