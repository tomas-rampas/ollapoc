using System.Diagnostics.Metrics;

namespace RagServer.Telemetry;

/// <summary>
/// Custom OTel metrics instruments for the RAG server.
/// The <see cref="Meter"/> is exposed so Program.cs can register
/// the queue-depth observable gauge without coupling this class to
/// <see cref="RagServer.Infrastructure.LlmRequestQueue"/>.
/// </summary>
public static class RagMetrics
{
    private static readonly Meter _meter = new("RagServer");

    /// <summary>The shared meter instance, used to register additional instruments in Program.cs.</summary>
    public static Meter Meter => _meter;

    /// <summary>End-to-end request latency per pipeline (tags: pipeline, model).</summary>
    public static readonly Histogram<long> RequestDurationMs =
        _meter.CreateHistogram<long>("rag.request_duration_ms", "ms", "End-to-end request latency");

    /// <summary>Tokens generated per second (tags: pipeline, model).</summary>
    public static readonly Histogram<float> TokensPerSecond =
        _meter.CreateHistogram<float>("rag.tokens_per_second", "tok/s", "LLM generation throughput");

    /// <summary>IR validated on first LLM attempt (tags: model).</summary>
    public static readonly Counter<long> IrFirstTrySuccess =
        _meter.CreateCounter<long>("rag.ir_first_try_success", "1", "IR validated on first LLM attempt");

    /// <summary>Elasticsearch search duration (tags: pipeline).</summary>
    public static readonly Histogram<long> EsSearchDurationMs =
        _meter.CreateHistogram<long>("rag.es_search_duration_ms", "ms", "Elasticsearch search duration");
}
