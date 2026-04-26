using System.Text.Json.Serialization;

namespace RagServer.Pipelines;

public record PipelineResult(
    string Pipeline,
    long LatencyMs,
    string? ModelName = null,
    int? TokensGenerated = null,
    double? TokensPerSecond = null,
    int? ToolCallCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IrValidFirstTry = null,
    int? TotalResultRows = null);
