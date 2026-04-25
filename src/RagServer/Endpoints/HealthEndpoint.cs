using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.AI;
using RagServer.Telemetry;

namespace RagServer.Endpoints;

public static class HealthEndpoint
{
    private static readonly TimeSpan LlmHealthTimeout = TimeSpan.FromSeconds(10);

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Basic liveness — unauthenticated, no external calls
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }))
           .AllowAnonymous();

        // LLM readiness — bounded timeout, no internal detail exposed to caller
        app.MapGet("/health/llm", async (
            [FromKeyedServices("chat")] IChatClient chatClient,
            CancellationToken ct) =>
        {
            using var activity = RagActivitySource.Source.StartActivity("health.llm");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(LlmHealthTimeout);

            try
            {
                await chatClient.GetResponseAsync("ping", cancellationToken: cts.Token);
                return Results.Ok(new { status = "ok" });
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.Json(new { status = "timeout" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception)
            {
                // Do not expose exception.Message — may contain internal URLs / credentials
                return Results.Json(new { status = "error", detail = "LLM service unavailable" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();

        // ES readiness — no internal topology details exposed to caller
        app.MapGet("/health/es", async (ElasticsearchClient es, CancellationToken ct) =>
        {
            using var activity = RagActivitySource.Source.StartActivity("health.es");
            try
            {
                var resp = await es.PingAsync(ct);
                return resp.IsValidResponse
                    ? Results.Ok(new { status = "ok" })
                    : Results.Json(new { status = "error", detail = "Elasticsearch unavailable" },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception)
            {
                return Results.Json(new { status = "error", detail = "Elasticsearch unavailable" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();
    }
}
