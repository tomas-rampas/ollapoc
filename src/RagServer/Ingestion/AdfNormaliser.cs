using System.Text;
using System.Text.Json;

namespace RagServer.Ingestion;

public sealed class AdfNormaliser
{
    public string Normalise(JsonElement adf)
    {
        var sb = new StringBuilder();
        WalkNode(adf, sb);
        return sb.ToString().Trim();
    }

    private static void WalkNode(JsonElement node, StringBuilder sb)
    {
        if (node.ValueKind != JsonValueKind.Object) return;

        string? type = null;
        if (node.TryGetProperty("type", out var typeEl))
            type = typeEl.GetString();

        if (type == "text")
        {
            if (node.TryGetProperty("text", out var textEl))
                sb.Append(textEl.GetString());
            return;
        }

        if (type == "hardBreak")
        {
            sb.Append('\n');
            return;
        }

        if (node.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.Array)
            foreach (var child in contentEl.EnumerateArray())
                WalkNode(child, sb);

        if (type == "paragraph" || type == "codeBlock" || type == "heading")
            sb.Append('\n');
    }
}
