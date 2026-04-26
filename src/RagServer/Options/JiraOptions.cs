namespace RagServer.Options;

public sealed class JiraOptions
{
    public string BaseUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string[] Projects { get; set; } = [];
    public string IncrementalCron { get; set; } = "0 */6 * * *";
    public int RateLimitRps { get; set; } = 3;
}
