namespace RagServer.Options;

public sealed class DemoOptions
{
    public bool DemoMode         { get; set; } = false;
    public bool DemoStatsEnabled { get; set; } = true;
    public bool DemoDebugPanel   { get; set; } = false;
    public string[] DemoPreWarmedQueries { get; set; } = [];
}
