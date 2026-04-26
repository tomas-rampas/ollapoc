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

public sealed class JiraCrawler(
    HttpClient http,
    IOptions<JiraOptions> opts,
    AdfNormaliser normaliser,
    TextChunker chunker,
    ILogger<JiraCrawler> logger) : IJiraCrawler
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
            logger.LogWarning("Jira not configured — skipping crawl");
            return [];
        }

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{o.Username}:{o.ApiToken}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        var projectClause = o.Projects.Length > 0
            ? $"project in ({string.Join(",", o.Projects)}) AND "
            : "";
        var jql = fullSync
            ? $"{projectClause}ORDER BY updated DESC"
            : $"{projectClause}updated >= \"{DateTime.UtcNow.AddHours(-6):yyyy-MM-dd HH:mm}\" ORDER BY updated DESC";

        var chunks = new List<DocumentChunk>();
        var startAt = 0;
        const int maxResults = 50;

        while (!ct.IsCancellationRequested)
        {
            var url = $"{o.BaseUrl}/rest/api/3/search" +
                      $"?jql={Uri.EscapeDataString(jql)}" +
                      $"&startAt={startAt}&maxResults={maxResults}" +
                      "&fields=summary,description,updated,project";

            var resp = await _retry.ExecuteAsync(async t => await http.GetAsync(url, t), ct);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var issues = doc.RootElement.GetProperty("issues");

            foreach (var issue in issues.EnumerateArray())
            {
                var key    = issue.GetProperty("key").GetString() ?? "";
                var fields = issue.GetProperty("fields");
                var summary = fields.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                var issueUrl = $"{o.BaseUrl}/browse/{key}";
                var project = "";
                if (fields.TryGetProperty("project", out var projEl) &&
                    projEl.TryGetProperty("key", out var pk))
                    project = pk.GetString() ?? "";

                var lastMod = DateTime.UtcNow;
                if (fields.TryGetProperty("updated", out var upd))
                    DateTime.TryParse(upd.GetString(), out lastMod);

                var text = summary;
                if (fields.TryGetProperty("description", out var desc))
                {
                    text += desc.ValueKind == JsonValueKind.Object
                        ? "\n" + normaliser.Normalise(desc)
                        : desc.ValueKind == JsonValueKind.String
                            ? "\n" + (desc.GetString() ?? "")
                            : "";
                }

                if (string.IsNullOrWhiteSpace(text)) continue;

                int idx = 0;
                foreach (var chunk in chunker.Chunk(text))
                    chunks.Add(new DocumentChunk(chunk, summary, issueUrl, "jira",
                        key, project, lastMod, idx++));
            }

            var total = doc.RootElement.GetProperty("total").GetInt32();
            startAt += maxResults;
            if (startAt >= total) break;
            await Task.Delay(TimeSpan.FromSeconds(1.0 / o.RateLimitRps), ct);
        }

        logger.LogInformation("Jira crawl: {Count} chunks", chunks.Count);
        return chunks;
    }
}
