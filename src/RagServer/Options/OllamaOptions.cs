namespace RagServer.Options;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://ollama:11434";
    public string ChatModel { get; set; } = "qwen3:8b";
    public string EmbeddingModel { get; set; } = "bge-small-en-v1.5";
}
