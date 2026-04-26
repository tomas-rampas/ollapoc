namespace RagServer.Infrastructure.Docs;

public sealed record RetrievedChunk(
    string Content,
    string Title,
    string Url,
    string SourceType,
    float Score);
