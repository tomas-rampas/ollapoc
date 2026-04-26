namespace RagServer.Options;

public sealed class IngestionOptions
{
    public int ChunkTargetTokens { get; set; } = 500;
    public int ChunkOverlapTokens { get; set; } = 50;
    public int BatchEmbedSize { get; set; } = 32;
}
