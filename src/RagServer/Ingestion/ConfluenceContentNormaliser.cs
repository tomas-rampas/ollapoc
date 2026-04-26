using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace RagServer.Ingestion;

public sealed class ConfluenceContentNormaliser
{
    private static readonly Regex MultipleWhitespace = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly string[] BlockElements =
        ["p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "tr", "br", "blockquote", "pre"];

    public string Normalise(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var toRemove = doc.DocumentNode.SelectNodes("//script|//style");
        if (toRemove != null)
            foreach (var node in toRemove.ToList())
                node.Remove();

        var xpath = string.Join("|", BlockElements.Select(e => $"//{e}"));
        var blockNodes = doc.DocumentNode.SelectNodes(xpath);
        if (blockNodes != null)
            foreach (var node in blockNodes)
                node.InnerHtml = "\n" + node.InnerHtml;

        var text = doc.DocumentNode.InnerText;
        text = MultipleWhitespace.Replace(text, " ");
        // Collapse multiple blank lines to single newline
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}
