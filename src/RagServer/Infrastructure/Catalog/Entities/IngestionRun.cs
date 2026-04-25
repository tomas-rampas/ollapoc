namespace RagServer.Infrastructure.Catalog.Entities;

public class IngestionRun
{
    public int    Id         { get; set; }
    public string Source     { get; set; } = "";
    public string Status     { get; set; } = "running";
    public int    ItemsCount { get; set; }
    public string? Error     { get; set; }
    public DateTimeOffset StartedAt  { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
}
