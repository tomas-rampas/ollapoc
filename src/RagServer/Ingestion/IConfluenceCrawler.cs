using RagServer.Infrastructure.Docs;

namespace RagServer.Ingestion;

public interface IConfluenceCrawler
{
    Task<IReadOnlyList<DocumentChunk>> CrawlAsync(bool fullSync, CancellationToken ct);
}
