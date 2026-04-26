using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace RagServer.Infrastructure;

public sealed record DemoQuery(string Text, string Pipeline);

public sealed class DemoQueriesService
{
    private readonly IReadOnlyList<DemoQuery> _queries;

    public DemoQueriesService(IWebHostEnvironment env, ILogger<DemoQueriesService> logger)
    {
        _queries = Load(env.WebRootPath, logger);
    }

    public IReadOnlyList<DemoQuery> GetQueries() => _queries;

    private static IReadOnlyList<DemoQuery> Load(string webRootPath, ILogger logger)
    {
        var path = Path.Combine(webRootPath, "DemoQueries.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("DemoQueries.json not found at {Path} — warmup will be skipped", path);
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<DemoQuery>>(json, opts) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse DemoQueries.json at {Path}", path);
            return [];
        }
    }
}
