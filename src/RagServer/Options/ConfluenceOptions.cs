namespace RagServer.Options;

public sealed class ConfluenceOptions
{
    public string BaseUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string[] Spaces { get; set; } = [];
    public string IncrementalCron { get; set; } = "0 */6 * * *";
    public string FullSyncCron { get; set; } = "0 2 * * 0";
    public int RateLimitRps { get; set; } = 3;
}
