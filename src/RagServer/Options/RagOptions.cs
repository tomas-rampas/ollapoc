namespace RagServer.Options;

public sealed class RagOptions
{
    public int QueueMaxDepth { get; set; } = 50;
    public int EmbeddingCacheSize { get; set; } = 1000;
    public int MaxOutputTokens { get; set; } = 512;
}
