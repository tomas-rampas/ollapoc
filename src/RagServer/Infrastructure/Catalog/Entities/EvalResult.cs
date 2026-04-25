namespace RagServer.Infrastructure.Catalog.Entities;

public class EvalResult
{
    public int    Id          { get; set; }
    public int    EvalQueryId { get; set; }
    public string RunId       { get; set; } = "";
    public string Response    { get; set; } = "";
    public string Pipeline    { get; set; } = "";
    public int    LatencyMs   { get; set; }
    public bool   Passed      { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
