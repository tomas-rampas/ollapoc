namespace RagServer.Options;

public sealed class RagOptions
{
    public int QueueMaxDepth { get; set; } = 50;
    public int EmbeddingCacheSize { get; set; } = 1000;
    public int MaxOutputTokens { get; set; } = 512;
    public int DocsTopK { get; set; } = 5;
    public int RrfRankConstant { get; set; } = 60;
    public int MetadataMaxTurns { get; set; } = 10;
    public int CatalogTermsTopK { get; set; } = 3;
    public int DataMaxRetries { get; set; } = 1;
    public int DataIrMaxTokens { get; set; } = 500;
}
