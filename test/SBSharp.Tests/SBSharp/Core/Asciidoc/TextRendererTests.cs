using NAsciidoc.Parser;

namespace SBSharp.Core.Asciidoc;

public class TextRendererTests
{
    [Fact]
    public void Render()
    {
        var doc = new Parser().Parse(
            """
            = Title

            some content
            """
        );
        var renderer = new TextRenderer();
        renderer.Visit(doc);
        var txt = renderer.Result();
        Assert.Equal("Title\nsome content", txt);
    }
}
