using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RagServer.Infrastructure;
using RagServer.Options;
using RagServer.Pipelines;
using RagServer.Router;
using RagServer.Telemetry;

namespace RagServer.Endpoints;

public static class ChatEndpoint
{
    private const int MaxMessageLength = 4000;

    public static async Task Handle(
        HttpContext ctx,
        [FromBody] ChatRequest req,
        [FromKeyedServices("chat")] IChatClient chatClient,
        IntentRouter router,
        LlmRequestQueue queue,
        DocsPipeline docsPipeline,
        IOptions<RagOptions> ragOpts)
    {
        // Validate input before committing to any response
        if (string.IsNullOrEmpty(req.Message))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsync("Message is required.");
            return;
        }

        if (req.Message.Length > MaxMessageLength)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsync($"Message exceeds {MaxMessageLength} character limit.");
            return;
        }

        using var activity = RagActivitySource.Source.StartActivity("rag.chat");

        try
        {
            // Enqueue BEFORE setting SSE headers so a full queue can still return a real 429
            await queue.EnqueueAsync(async ct =>
            {
                // Routing runs inside the queue so all LLM calls (routing + chat) are serialised
                var pipeline = await router.RouteAsync(req.Message, ct);
                activity?.SetTag("rag.pipeline", pipeline.ToString().ToLower());

                // Headers committed here — queue slot is held so 429 cannot occur after this point
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers.Connection = "keep-alive";

                await ctx.Response.WriteAsync($"event: pipeline\ndata: {pipeline}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);

                if (pipeline == PipelineKind.Docs)
                {
                    await docsPipeline.ExecuteAsync(req.Message, ctx.Response, ct);
                    await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
                else
                {
                    var opts = new ChatOptions { MaxOutputTokens = ragOpts.Value.MaxOutputTokens };
                    await foreach (var update in chatClient.GetStreamingResponseAsync(req.Message, opts, ct))
                    {
                        if (update.Text is not null)
                        {
                            // Escape embedded newlines so they don't break the SSE frame boundary
                            var safeText = EscapeSse(update.Text);
                            await ctx.Response.WriteAsync($"data: {safeText}\n\n", ct);
                            await ctx.Response.Body.FlushAsync(ct);
                        }
                    }
                    await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
                return (object?)null;
            }, ctx.RequestAborted);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            // Queue was full before headers were sent — return a real 429
            ctx.Response.StatusCode = 429;
            await ctx.Response.WriteAsync("Server busy. Please try again in a moment.");
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — stream is already closed; nothing to do
        }
        catch (Exception ex)
        {
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync("Internal server error.");
            }
            else
            {
                // Headers already sent; signal error via SSE then close
                var safeMsg = EscapeSse(ex.Message);
                await ctx.Response.WriteAsync($"event: error\ndata: {safeMsg}\n\n");
                await ctx.Response.WriteAsync("data: [DONE]\n\n");
                await ctx.Response.Body.FlushAsync();
            }
        }
    }

    /// <summary>
    /// Escapes embedded CR/LF characters so they don't break SSE frame boundaries.
    /// Normalises CRLF/CR to LF first, then replaces each LF with a data: continuation prefix.
    /// </summary>
    private static string EscapeSse(string text) =>
        text.Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", "\ndata: ");

    public record ChatRequest(string Message);
}
