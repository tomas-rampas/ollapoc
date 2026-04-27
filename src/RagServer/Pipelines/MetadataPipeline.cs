using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagServer.Options;
using RagServer.Telemetry;
using RagServer.Tools;

namespace RagServer.Pipelines;

/// <summary>
/// UC-2 Metadata pipeline: M.E.AI function-calling loop over CatalogTools.
/// Registers all five catalog tools, drives the model through up to MetadataMaxTurns
/// turns, dispatches each FunctionCallContent to the corresponding AIFunction, and
/// streams the final assistant answer as SSE data events.
/// </summary>
public sealed class MetadataPipeline(
    CatalogTools catalogTools,
    [FromKeyedServices("chat")] IChatClient chatClient,
    IOptions<RagOptions> opts,
    ILogger<MetadataPipeline> logger,
    IOptions<OllamaOptions> ollamaOpts)
{
    private static readonly JsonSerializerOptions StatsJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string SystemPrompt =
        "You are a catalog assistant. Use the provided tools to look up entity information. " +
        "Answer ONLY from tool results. Do not invent information.";

    public async Task ExecuteAsync(string query, HttpResponse response, CancellationToken ct)
    {
        using var activity = RagActivitySource.Source.StartActivity("rag.metadata_pipeline");
        var sw = Stopwatch.StartNew();

        // Register all 7 catalog tools via AIFunctionFactory.Create(Delegate)
        AIFunction[] aiFunctions =
        [
            AIFunctionFactory.Create(catalogTools.ResolveEntityAsync),
            AIFunctionFactory.Create(catalogTools.GetEntityAttributesAsync),
            AIFunctionFactory.Create(catalogTools.GetChildAttributesAsync),
            AIFunctionFactory.Create(catalogTools.GetEntityRulesAsync),
            AIFunctionFactory.Create(catalogTools.GetEntityExtensionsAsync),
            AIFunctionFactory.Create(catalogTools.ListCDEAsync),
            AIFunctionFactory.Create(catalogTools.GetEntityRelationshipsAsync),
        ];

        // Build a lookup by function name for fast dispatch
        var functionMap = aiFunctions.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

        var chatOpts = new ChatOptions
        {
            Tools = [.. aiFunctions.Cast<AITool>()],
            ToolMode = ChatToolMode.Auto,
            // No MaxOutputTokens here: Qwen3 think blocks can be hundreds of tokens
            // and truncating them mid-block causes the closing </think> tag to be missing,
            // which means StripThink cannot remove the block and raw reasoning leaks to the user.
            // MetadataMaxTurns already bounds total execution cost.
        };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, query)
        };

        var toolsUsed = new List<string>();
        var maxTurns = opts.Value.MetadataMaxTurns;

        for (var turn = 0; turn < maxTurns; turn++)
        {
            var chatResponse = await chatClient.GetResponseAsync(messages, chatOpts, ct);
            messages.AddRange(chatResponse.Messages);

            // Collect function call requests from this response turn
            var calls = chatResponse.Messages
                .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
                .Where(c => !c.InformationalOnly)
                .ToList();

            if (calls.Count == 0)
                break; // No more tool calls — model produced a final answer

            // Dispatch each tool call and append results as a Tool role message
            foreach (var call in calls)
            {
                toolsUsed.Add(call.Name);

                object? result;
                if (functionMap.TryGetValue(call.Name, out var aiFunc))
                {
                    var args = call.Arguments is not null
                        ? new AIFunctionArguments(call.Arguments)
                        : new AIFunctionArguments();
                    try
                    {
                        result = await aiFunc.InvokeAsync(args, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Tool '{Name}' threw during dispatch", call.Name);
                        result = new { error = ex.Message };
                    }
                }
                else
                {
                    logger.LogWarning("Model requested unknown tool '{Name}'", call.Name);
                    result = new { error = $"Unknown tool '{call.Name}'. Available: {string.Join(", ", functionMap.Keys)}" };
                }

                messages.Add(new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(call.CallId, result)]));
            }
        }

        activity?.SetTag("rag.tool_calls_count", toolsUsed.Count);

        // Stream the final answer: last assistant message that contains no function calls
        var finalAnswer = messages
            .LastOrDefault(m => m.Role == ChatRole.Assistant &&
                                !m.Contents.Any(c => c is FunctionCallContent));

        var answerText = "";
        if (finalAnswer is not null)
        {
            foreach (var content in finalAnswer.Contents.OfType<TextContent>())
            {
                var text = StripThink(content.Text ?? "");
                if (!string.IsNullOrEmpty(text))
                {
                    answerText += text;
                    await response.WriteAsync($"data: {EscapeSse(text)}\n\n", ct);
                    await response.Body.FlushAsync(ct);
                }
            }
        }
        else
        {
            activity?.SetTag("rag.metadata.no_final_answer", true);
            const string fallback = "I could not produce a final answer within the allowed tool-call limit.";
            answerText = fallback;
            await response.WriteAsync($"data: {fallback}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }

        // Always emit the tools_used metadata event
        var toolsJson = JsonSerializer.Serialize(toolsUsed.Distinct().ToList());
        await response.WriteAsync($"event: tools_used\ndata: {toolsJson}\n\n", ct);
        await response.Body.FlushAsync(ct);

        // Emit stats event
        sw.Stop();
        var tokenCount = answerText.Length > 0 ? Math.Max(1, answerText.Length / 4) : (int?)null;
        var latencyMs  = sw.ElapsedMilliseconds;
        var tps        = (latencyMs > 0 && tokenCount.HasValue)
            ? Math.Round((double)tokenCount.Value / latencyMs * 1000.0, 1) : (double?)null;

        RagMetrics.RequestDurationMs.Record(latencyMs,
            new KeyValuePair<string, object?>("pipeline", "Metadata"),
            new KeyValuePair<string, object?>("model", ollamaOpts.Value.ChatModel));
        if (tps.HasValue)
            RagMetrics.TokensPerSecond.Record((float)tps.Value,
                new KeyValuePair<string, object?>("pipeline", "Metadata"));

        var stats = new PipelineResult(
            Pipeline:       "Metadata",
            LatencyMs:      latencyMs,
            ModelName:      ollamaOpts.Value.ChatModel,
            TokensGenerated: tokenCount,
            TokensPerSecond: tps,
            ToolCallCount:  toolsUsed.Count);

        var statsJson = JsonSerializer.Serialize(stats, StatsJsonOpts);
        await response.WriteAsync($"event: stats\ndata: {statsJson}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static readonly System.Text.RegularExpressions.Regex ThinkPattern =
        new(@"<think>[\s\S]*?</think>\s*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string StripThink(string text)
    {
        // Strip complete <think>...</think> blocks
        var stripped = ThinkPattern.Replace(text, "").TrimStart();
        // If an unclosed <think> tag remains (model was truncated mid-think), discard everything after it
        var idx = stripped.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            stripped = stripped[..idx].TrimEnd();
        return stripped;
    }

    private static string EscapeSse(string text)
    {
        // Normalise all line endings to LF, then encode for SSE multi-line data
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return text.Replace("\n", "\ndata: ");
    }
}
