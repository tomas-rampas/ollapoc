namespace RagServer.Options;

public sealed class ConfluenceOptions
{
    public string BaseUrl { get; set; } = "";
    /// <summary>
    /// Browser-accessible base URL for citation links. Defaults to BaseUrl when empty.
    /// Set to the host-accessible address (e.g. http://localhost:8090) when BaseUrl
    /// uses a Docker-internal hostname that is not reachable from the browser.
    /// </summary>
    public string PublicUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string[] Spaces { get; set; } = [];
    public string IncrementalCron { get; set; } = "0 */6 * * *";
    public string FullSyncCron { get; set; } = "0 2 * * 0";
    public int RateLimitRps { get; set; } = 3;
}
