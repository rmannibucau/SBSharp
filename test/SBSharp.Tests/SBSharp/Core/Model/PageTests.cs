using System.Collections.Immutable;
using NAsciidoc.Model;

namespace SBSharp.Core.Model;

public class PageTests
{
    [Fact]
    public void Gravatar()
    {
        var url = new Page(
            ImmutableDictionary<string, string>.Empty,
            new Document(
                new Header(
                    "",
                    null,
                    null,
                    new Dictionary<string, string> { { "author", "rmannibucau" } }
                ),
                new Body([])
            ),
            () => "",
            "",
            new Page.GlobalContext()
        ).Gravatar;
        Assert.Equal(
            "https://gravatar.com/avatar/5f4f172015d79e25f0d0afc4de251d94f4b2cd5bb05e2baea28de98361d811b7?d=robohash",
            url
        );
    }

    [Fact]
    public void PublishedOn()
    {
        var date = new Page(
            ImmutableDictionary<string, string>.Empty,
            new Document(
                new Header(
                    "",
                    null,
                    null,
                    new Dictionary<string, string> { { "published-on", "20240701" } }
                ),
                new Body([])
            ),
            () => "",
            "",
            new Page.GlobalContext()
        ).PublishedOn;
        Assert.Equal(new DateOnly(2024, 7, 1), date);
    }
}
