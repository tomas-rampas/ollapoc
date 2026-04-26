namespace RagServer.Options;

public sealed class AbTestOptions
{
    public string ModelA { get; set; } = "qwen3:8b";
    public string ModelB { get; set; } = "qwen3:14b";
    public bool Enabled  { get; set; } = false;
}
