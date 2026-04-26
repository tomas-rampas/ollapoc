using RagServer.Ingestion;

namespace RagServer.Endpoints;

public static class AdminEndpoint
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/reindex", async (
            string? source,
            IngestionScheduler scheduler,
            CancellationToken ct) =>
        {
            if (source is null or not ("confluence" or "jira" or "all"))
                return Results.BadRequest("source must be 'confluence', 'jira', or 'all'");

            // Fire and forget — return 202 immediately
            _ = Task.Run(() => scheduler.RunIngestionAsync(fullSync: true, CancellationToken.None));
            return Results.Accepted($"/admin/reindex", new { source, status = "queued" });
        });

        return app;
    }
}
