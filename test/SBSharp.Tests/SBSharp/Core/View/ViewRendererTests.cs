using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SBSharp.Tests.Temp;

namespace SBSharp.Core.View;

public class ViewRendererTests
{
    [Fact]
    public async Task Render()
    {
        using var baseDir = new TempFolder("ViewRendererTests-Render");
        var views = Directory.CreateDirectory(Path.Combine(baseDir.Value, "views"));
        File.WriteAllText(Path.Combine(views.FullName, "tpl.cshtml"), "will be @(Model.Slug).html");

        using var factory = new NullLoggerFactory();
        var html = await new ViewRenderer(
            new Configuration.SBSharpConfiguration
            {
                Input = new Configuration.SBSharpConfiguration.InputConfiguration
                {
                    Location = baseDir.Value,
                    View = "views"
                }
            },
            new Logger<ViewRenderer>(factory)
        ).RenderAsync(
            "tpl",
            new Page( // fake a loaded model, we just use the slug here
                ImmutableDictionary<string, string>.Empty,
                new NAsciidoc.Model.Document(
                    new NAsciidoc.Model.Header(
                        "",
                        null,
                        null,
                        ImmutableDictionary<string, string>.Empty
                    ),
                    new NAsciidoc.Model.Body([])
                ),
                () => "Content",
                "some-sample-file",
                new Page.GlobalContext { Pages = [] }
            )
        );
        Assert.Equal("will be some-sample-file.html", html);
    }
}
