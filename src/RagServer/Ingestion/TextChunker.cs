using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RagServer.Options;

namespace RagServer.Ingestion;

public sealed class TextChunker(IOptions<IngestionOptions> opts)
{
    private readonly int _targetWords = (int)(opts.Value.ChunkTargetTokens / 1.33);
    private readonly int _overlapWords = (int)(opts.Value.ChunkOverlapTokens / 1.33);
    private static readonly Regex SentenceBoundary = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    public IEnumerable<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var sentences = SentenceBoundary.Split(text.Trim());
        var window = new List<string>();
        int wordCount = 0;
        foreach (var sentence in sentences)
        {
            var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount + words > _targetWords && window.Count > 0)
            {
                yield return string.Join(" ", window);
                int overlapCount = 0;
                var overlap = new List<string>();
                for (int i = window.Count - 1; i >= 0 && overlapCount < _overlapWords; i--)
                {
                    overlapCount += window[i].Split(' ').Length;
                    overlap.Insert(0, window[i]);
                }
                window = overlap;
                wordCount = overlapCount;
            }
            window.Add(sentence);
            wordCount += words;
        }
        if (window.Count > 0) yield return string.Join(" ", window);
    }
}
