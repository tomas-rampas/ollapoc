using RagServer.Ingestion;
using RagServer.Options;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace RagServer.Tests.Ingestion;

public class TextChunkerTests
{
    private static TextChunker BuildChunker(int targetTokens = 500, int overlapTokens = 50)
        => new(Create(new IngestionOptions
        {
            ChunkTargetTokens = targetTokens,
            ChunkOverlapTokens = overlapTokens
        }));

    [Fact]
    public void Empty_Input_Returns_No_Chunks()
    {
        var chunker = BuildChunker();
        var result = chunker.Chunk("").ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void Whitespace_Only_Returns_No_Chunks()
    {
        var chunker = BuildChunker();
        var result = chunker.Chunk("   \t\n   ").ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void Short_Text_Below_Target_Returns_Single_Chunk()
    {
        // targetTokens=500 → targetWords≈376; this sentence is well under that
        var chunker = BuildChunker(targetTokens: 500, overlapTokens: 50);
        const string text = "This is a short sentence.";
        var result = chunker.Chunk(text).ToList();
        Assert.Single(result);
        Assert.Contains("short sentence", result[0]);
    }

    [Fact]
    public void Long_Text_Splits_At_Sentence_Boundary()
    {
        // targetTokens=20 → targetWords≈15
        // Build ~30 word sentences that force a split
        var chunker = BuildChunker(targetTokens: 20, overlapTokens: 0);
        // Each sentence is ~10 words; two sentences together exceed target ~15 words
        const string text =
            "The quick brown fox jumps over the lazy dog today. " +
            "A second sentence also has many interesting words here. " +
            "A third sentence completes this test input nicely.";

        var result = chunker.Chunk(text).ToList();
        Assert.True(result.Count >= 2, $"Expected >= 2 chunks but got {result.Count}");
    }

    [Fact]
    public void Overlap_Carries_Trailing_Sentences()
    {
        // targetTokens=20 → targetWords≈15, overlapTokens=13 → overlapWords≈9
        // Sentence A is ~8 words, Sentence B is ~8 words → chunk1 = [A, B], overlap carries B
        var chunker = BuildChunker(targetTokens: 20, overlapTokens: 13);
        const string sentenceA = "Alpha beta gamma delta epsilon zeta eta theta.";
        const string sentenceB = "Iota kappa lambda mu nu xi omicron pi.";
        const string sentenceC = "Rho sigma tau upsilon phi chi psi omega.";

        var text = $"{sentenceA} {sentenceB} {sentenceC}";
        var result = chunker.Chunk(text).ToList();

        Assert.True(result.Count >= 2, $"Expected >= 2 chunks but got {result.Count}");

        // The overlap means sentence B (which ended chunk 1) should appear in chunk 2
        var secondChunk = result[1];
        Assert.True(
            secondChunk.Contains("Iota") || secondChunk.Contains("kappa"),
            $"Expected overlap content in chunk 2 but got: {secondChunk}");
    }

    [Fact]
    public void Single_Very_Long_Sentence_Returns_Single_Chunk()
    {
        // No sentence boundary → entire text is one sentence → one chunk regardless of length
        var chunker = BuildChunker(targetTokens: 20, overlapTokens: 0);
        // Build a 30-word sentence with NO terminal punctuation split point
        const string longSentence =
            "one two three four five six seven eight nine ten " +
            "eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty " +
            "twenty-one twenty-two twenty-three twenty-four twenty-five twenty-six twenty-seven twenty-eight twenty-nine thirty";

        var result = chunker.Chunk(longSentence).ToList();
        Assert.Single(result);
    }

    [Fact]
    public void Word_Count_Approximation_Respects_Target()
    {
        // targetTokens=13 → targetWords≈9 (int(13/1.33)=9)
        // A 20-word text with no sentence boundary → one chunk
        var chunker = BuildChunker(targetTokens: 13, overlapTokens: 0);
        const string text =
            "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty";

        var result = chunker.Chunk(text).ToList();
        // No sentence boundary means the entire text stays as one chunk
        Assert.Single(result);
    }

    [Fact]
    public void Multiple_Chunks_All_NonEmpty()
    {
        // Force many chunks with small target
        var chunker = BuildChunker(targetTokens: 7, overlapTokens: 0);
        // 6 sentences of ~6 words each → should produce several chunks
        const string text =
            "First sentence has six words here. " +
            "Second sentence also has six words. " +
            "Third sentence likewise has six words. " +
            "Fourth sentence continues with six words. " +
            "Fifth sentence wraps up the list. " +
            "Sixth sentence is the final one.";

        var result = chunker.Chunk(text).ToList();
        Assert.True(result.Count >= 2, $"Expected multiple chunks but got {result.Count}");
        Assert.All(result, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk)));
    }
}
