using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RagServer.Options;
using RagServer.Telemetry;

namespace RagServer.Router;

public sealed class IntentRouter
{
    // Metadata checked first: domain-specific signals are stronger than generic question words
    // (e.g. "What are the critical data elements?" should route to Metadata, not Docs)
    private static readonly (Regex Pattern, PipelineKind Kind)[] Rules =
    [
        (new Regex(@"\b(rules?|validations?|constraints?|mandatory\s+(field|attribute|rule)|data\s+owner|governance\s+owner|attributes?|fields?|columns?|schema|cde|critical data elements?|entity|entities|metadata)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), PipelineKind.Metadata),
        (new Regex(@"\b(give me all|list all|show me|find all|records?|count of|fetch|get all|top \d+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), PipelineKind.Data),
        (new Regex(@"\b(how|what is|what are|explain|describe|definition|purpose|guide|tutorial|overview)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled), PipelineKind.Docs),
    ];

    private readonly IChatClient _chatClient;
    private readonly int _cacheCapacity;

    // Thread-safe cache; survives across requests when registered as Singleton
    private readonly ConcurrentDictionary<string, PipelineKind> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public IntentRouter(
        [FromKeyedServices("chat")] IChatClient chatClient,
        IOptions<RagOptions> ragOpts)
    {
        _chatClient = chatClient;
        _cacheCapacity = ragOpts.Value.EmbeddingCacheSize;
    }

    public async Task<PipelineKind> RouteAsync(string query, CancellationToken ct = default)
    {
        using var activity = RagActivitySource.Source.StartActivity("rag.route");

        // 1. Rule-based tier
        foreach (var (pattern, kind) in Rules)
        {
            if (pattern.IsMatch(query))
            {
                activity?.SetTag("rag.route.rule_matched", true);
                activity?.SetTag("rag.pipeline", kind.ToString().ToLower());
                return kind;
            }
        }

        activity?.SetTag("rag.route.rule_matched", false);

        // 2. Cache
        if (_cache.TryGetValue(query, out var cached))
        {
            activity?.SetTag("rag.route.cache_hit", true);
            activity?.SetTag("rag.pipeline", cached.ToString().ToLower());
            return cached;
        }

        // 3. Model fallback
        var prompt = $"""
            Classify the following query as exactly one of: docs, metadata, data.
            Reply with only the single word.
            <user_query>{query}</user_query>
            """;

        var result = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);
        var kind2 = result.Text?.Trim().ToLowerInvariant() switch
        {
            "metadata" => PipelineKind.Metadata,
            "data"     => PipelineKind.Data,
            _          => PipelineKind.Docs
        };

        // TryAdd is thread-safe; bounded by evicting nothing (simple LRU approximation for POC)
        if (_cache.Count < _cacheCapacity)
            _cache.TryAdd(query, kind2);

        activity?.SetTag("rag.route.cache_hit", false);
        activity?.SetTag("rag.pipeline", kind2.ToString().ToLower());
        return kind2;
    }
}
