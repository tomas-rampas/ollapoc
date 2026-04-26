using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;
using RagServer.Options;

namespace RagServer.Ingestion;

public sealed class IngestionScheduler(
    IConfluenceCrawler confluenceCrawler,
    IJiraCrawler jiraCrawler,
    DocumentEmbedder embedder,
    IOptions<ConfluenceOptions> confluenceOpts,
    IOptions<JiraOptions> jiraOpts,
    ILogger<IngestionScheduler> logger) : IHostedService, IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _bg;

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _bg  = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_cts is not null) await _cts.CancelAsync();
        if (_bg is not null)
            try { await _bg; } catch (OperationCanceledException) { }
    }

    public void Dispose() => _cts?.Dispose();

    public async Task RunIngestionAsync(bool fullSync, CancellationToken ct)
    {
        logger.LogInformation("Ingestion start (fullSync={FullSync})", fullSync);
        try
        {
            var cfChunks = await confluenceCrawler.CrawlAsync(fullSync, ct);
            if (cfChunks.Count > 0)
                await embedder.EmbedAndUpsertAsync(cfChunks, ct);

            var jiraChunks = await jiraCrawler.CrawlAsync(fullSync, ct);
            if (jiraChunks.Count > 0)
                await embedder.EmbedAndUpsertAsync(jiraChunks, ct);

            logger.LogInformation("Ingestion done: {Cf} confluence + {Jira} jira chunks",
                cfChunks.Count, jiraChunks.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ingestion run failed");
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var cfSchedule   = CrontabSchedule.TryParse(confluenceOpts.Value.IncrementalCron);
        var jiraSchedule = CrontabSchedule.TryParse(jiraOpts.Value.IncrementalCron);

        while (!ct.IsCancellationRequested)
        {
            var now  = DateTime.UtcNow;
            var next = new[]
            {
                cfSchedule?.GetNextOccurrence(now)   ?? now.AddHours(6),
                jiraSchedule?.GetNextOccurrence(now) ?? now.AddHours(6)
            }.Min();

            var delay = next - now;
            if (delay > TimeSpan.Zero)
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { return; }

            await RunIngestionAsync(fullSync: false, ct);
        }
    }
}
