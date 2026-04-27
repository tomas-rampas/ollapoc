using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using RagServer.Infrastructure;
using RagServer.Ingestion;
using RagServer.Router;

namespace RagServer.Endpoints;

public static class AdminEndpoint
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app, bool skipAuth = false)
    {
        var reindexRoute = app.MapPost("/admin/reindex", (
            string? source,
            [FromServices] IngestionScheduler scheduler) =>
        {
            if (source is null or not ("confluence" or "jira" or "all"))
                return Results.BadRequest("source must be 'confluence', 'jira', or 'all'");

            // Fire and forget — return 202 immediately
            _ = Task.Run(() => scheduler.RunIngestionAsync(fullSync: true, CancellationToken.None));
            return Results.Accepted($"/admin/reindex", new { source, status = "queued" });
        });

        // Warms the router's embedding cache (pre-populates route classifications)
        // and loads the chat model into VRAM by sending a minimal probe so the first
        // real demo query does not pay the model-load latency.
        var warmupRoute = app.MapPost("/demo/warmup", async (
            DemoQueriesService demoSvc,
            IntentRouter router,
            [FromKeyedServices("chat")] IChatClient llmClient,
            CancellationToken ct) =>
        {
            var queries = demoSvc.GetQueries();
            var results = new List<object>(queries.Count);

            foreach (var q in queries)
            {
                var pipeline = await router.RouteAsync(q.Text, ct);
                results.Add(new { query = q.Text, pipeline = pipeline.ToString() });
            }

            // Send a 1-token probe to ensure the model is loaded into VRAM
            await llmClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "ping")],
                new ChatOptions { MaxOutputTokens = 1 },
                ct);

            return Results.Ok(new { warmed = results.Count, queries = results });
        });

        if (!skipAuth)
        {
            reindexRoute.RequireAuthorization();
            warmupRoute.RequireAuthorization();
        }

        return app;
    }
}
