using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RagServer.Infrastructure.Docs;
using RagServer.Options;

namespace RagServer.Ingestion;

public sealed class ConfluenceCrawler(
    HttpClient http,
    IOptions<ConfluenceOptions> opts,
    ConfluenceContentNormaliser normaliser,
    TextChunker chunker,
    ILogger<ConfluenceCrawler> logger) : IConfluenceCrawler
{
    private static readonly ResiliencePipeline<HttpResponseMessage> _retry =
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential
            })
            .Build();

    public async Task<IReadOnlyList<DocumentChunk>> CrawlAsync(bool fullSync, CancellationToken ct)
    {
        var o = opts.Value;
        if (string.IsNullOrEmpty(o.BaseUrl) || string.IsNullOrEmpty(o.ApiToken))
        {
            logger.LogWarning("Confluence not configured — skipping crawl");
            return [];
        }

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{o.Username}:{o.ApiToken}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

        var spaces = o.Spaces.Length > 0 ? o.Spaces : await GetAllSpacesAsync(o, ct);
        var chunks = new List<DocumentChunk>();

        foreach (var space in spaces)
        {
            var start = 0;
            const int limit = 25;
            while (!ct.IsCancellationRequested)
            {
                var url = $"{o.BaseUrl}/rest/api/content" +
                          $"?spaceKey={Uri.EscapeDataString(space)}" +
                          $"&expand=body.storage,version&start={start}&limit={limit}";

                var resp = await _retry.ExecuteAsync(async t => await http.GetAsync(url, t), ct);
                resp.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var results = doc.RootElement.GetProperty("results");

                foreach (var page in results.EnumerateArray())
                {
                    var pageId  = page.GetProperty("id").GetString() ?? "";
                    var title   = page.GetProperty("title").GetString() ?? "";
                    var pageUrl = $"{o.BaseUrl}/pages/{pageId}";
                    var lastMod = DateTime.UtcNow;

                    if (page.TryGetProperty("version", out var ver) &&
                        ver.TryGetProperty("when", out var when))
                        DateTime.TryParse(when.GetString(), out lastMod);

                    var html = "";
                    if (page.TryGetProperty("body", out var body) &&
                        body.TryGetProperty("storage", out var storage) &&
                        storage.TryGetProperty("value", out var value))
                        html = value.GetString() ?? "";

                    var text = normaliser.Normalise(html);
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    int idx = 0;
                    foreach (var chunk in chunker.Chunk(text))
                        chunks.Add(new DocumentChunk(chunk, title, pageUrl, "confluence",
                            pageId, space, lastMod, idx++));
                }

                if (results.GetArrayLength() < limit) break;
                start += limit;
                await Task.Delay(TimeSpan.FromSeconds(1.0 / o.RateLimitRps), ct);
            }
        }

        logger.LogInformation("Confluence crawl: {Count} chunks from {Spaces} spaces",
            chunks.Count, spaces.Length);
        return chunks;
    }

    private async Task<string[]> GetAllSpacesAsync(ConfluenceOptions o, CancellationToken ct)
    {
        var resp = await http.GetAsync($"{o.BaseUrl}/rest/api/space?limit=50", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return [.. doc.RootElement.GetProperty("results").EnumerateArray()
            .Select(s => s.GetProperty("key").GetString() ?? "")
            .Where(k => k.Length > 0)];
    }
}
