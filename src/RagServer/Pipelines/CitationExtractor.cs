using System.Text.RegularExpressions;
using RagServer.Infrastructure.Docs;

namespace RagServer.Pipelines;

/// <summary>
/// Extracts citation references ([n]) from an answer and maps them to retrieved chunks.
/// </summary>
public static partial class CitationExtractor
{
    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex CitationPattern();

    public static IReadOnlyList<Citation> Extract(string answerText, IReadOnlyList<RetrievedChunk> chunks)
    {
        var seen = new HashSet<int>();
        var citations = new List<Citation>();

        foreach (Match m in CitationPattern().Matches(answerText))
        {
            if (!int.TryParse(m.Groups[1].Value, out var idx))
                continue;

            // 1-based index
            if (idx < 1 || idx > chunks.Count)
                continue;

            if (!seen.Add(idx))
                continue;

            var chunk = chunks[idx - 1];
            citations.Add(new Citation(idx, chunk.Url, chunk.Title));
        }

        citations.Sort((a, b) => a.Index.CompareTo(b.Index));
        return citations;
    }
}
