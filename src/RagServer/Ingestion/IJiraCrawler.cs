using RagServer.Infrastructure.Docs;

namespace RagServer.Ingestion;

public interface IJiraCrawler
{
    Task<IReadOnlyList<DocumentChunk>> CrawlAsync(bool fullSync, CancellationToken ct);
}
