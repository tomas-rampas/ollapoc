using RagServer.Ingestion;
using Xunit;

namespace RagServer.Tests.Ingestion;

public class ConfluenceContentNormaliserTests
{
    private static readonly ConfluenceContentNormaliser Sut = new();

    [Fact]
    public void Script_Tags_Are_Stripped()
    {
        var result = Sut.Normalise("<script>alert('x')</script>hello");
        Assert.DoesNotContain("alert", result);
        Assert.Contains("hello", result);
    }

    [Fact]
    public void Style_Tags_Are_Stripped()
    {
        var result = Sut.Normalise("<style>body{}</style>world");
        Assert.DoesNotContain("body{}", result);
        Assert.Contains("world", result);
    }

    [Fact]
    public void Block_Elements_Get_Newline()
    {
        var result = Sut.Normalise("<p>foo</p><p>bar</p>");
        // The normaliser prepends \n inside block elements, so the output contains a newline between content
        Assert.Contains("\n", result);
        Assert.Contains("foo", result);
        Assert.Contains("bar", result);
    }

    [Fact]
    public void Whitespace_Collapsed()
    {
        var result = Sut.Normalise("<p>hello   world</p>");
        // Multiple horizontal spaces collapsed to single space
        Assert.DoesNotContain("   ", result);
        Assert.Contains("hello", result);
        Assert.Contains("world", result);
    }

    [Fact]
    public void Empty_Input_Returns_Empty()
    {
        var result = Sut.Normalise("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Plain_Text_Passthrough()
    {
        const string input = "  plain text no html  ";
        var result = Sut.Normalise(input);
        Assert.Equal("plain text no html", result);
    }
}
