namespace RagServer.Infrastructure.Docs;

public sealed record DocumentChunk(
    string Content,
    string Title,
    string Url,
    string SourceType,
    string SourceId,
    string SpaceOrProject,
    DateTime LastModified,
    int ChunkIndex);
