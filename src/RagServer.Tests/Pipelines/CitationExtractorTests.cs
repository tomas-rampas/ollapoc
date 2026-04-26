using RagServer.Infrastructure.Docs;
using RagServer.Pipelines;
using Xunit;

namespace RagServer.Tests.Pipelines;

public class CitationExtractorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RetrievedChunk MakeChunk(int n)
        => new($"Content {n}", $"Title {n}", $"https://example.com/page{n}", "confluence", 0.9f);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_SingleCitation_ReturnsCitation()
    {
        var chunks = new[] { MakeChunk(1) };

        var citations = CitationExtractor.Extract("Answer text [1] here.", chunks);

        Assert.Single(citations);
        Assert.Equal(1, citations[0].Index);
        Assert.Equal(chunks[0].Url, citations[0].Url);
        Assert.Equal(chunks[0].Title, citations[0].Title);
    }

    [Fact]
    public void Extract_MultipleCitations_ReturnsAll()
    {
        var chunks = new[] { MakeChunk(1), MakeChunk(2) };

        var citations = CitationExtractor.Extract("See [1] and [2] for more.", chunks);

        Assert.Equal(2, citations.Count);
        Assert.Contains(citations, c => c.Index == 1);
        Assert.Contains(citations, c => c.Index == 2);
    }

    [Fact]
    public void Extract_Duplicate_Deduplicated()
    {
        var chunks = new[] { MakeChunk(1) };

        var citations = CitationExtractor.Extract("[1] and again [1].", chunks);

        Assert.Single(citations);
        Assert.Equal(1, citations[0].Index);
    }

    [Fact]
    public void Extract_OutOfRange_Skipped()
    {
        var chunks = new[] { MakeChunk(1), MakeChunk(2) };

        var citations = CitationExtractor.Extract("See [5] for details.", chunks);

        Assert.Empty(citations);
    }

    [Fact]
    public void Extract_NoMarkers_ReturnsEmpty()
    {
        var chunks = new[] { MakeChunk(1), MakeChunk(2) };

        var citations = CitationExtractor.Extract("no citations here", chunks);

        Assert.Empty(citations);
    }

    [Fact]
    public void Extract_Empty_Input_ReturnsEmpty()
    {
        var citations = CitationExtractor.Extract(string.Empty, Array.Empty<RetrievedChunk>());

        Assert.Empty(citations);
    }
}
