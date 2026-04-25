namespace RagServer.Options;

public sealed class ElasticsearchOptions
{
    public string Url      { get; set; } = "http://elasticsearch:9200";
    public string Username { get; set; } = "elastic";
    /// <summary>
    /// Must be supplied via ES_PASSWORD environment variable.
    /// Defaults to empty string — application will use whatever Elasticsearch accepts,
    /// but a startup warning is logged if the password matches the well-known bootstrap value.
    /// </summary>
    public string Password { get; set; } = string.Empty;
}
