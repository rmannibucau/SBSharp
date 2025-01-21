using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SBSharp.Core.Configuration;
using SBSharp.Tests.Temp;

namespace SBSharp.Core.View;

public class ViewRendererTests
{
    [Fact]
    public async Task Render()
    {
        using var baseDir = new TempFolder("ViewRendererTests-Render");
        var views = Directory.CreateDirectory(Path.Combine(baseDir.Value, "views"));
        await File.WriteAllTextAsync(Path.Combine(views.FullName, "tpl.cshtml"), "will be @(Model.Slug).html");

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
            NewPage()
        );
        Assert.Equal("will be some-sample-file.html", html);
    }

    [Fact]
    public async Task EnableViewCache()
    {
        using var baseDir = new TempFolder("ViewRendererTests-EnableViewCache");
        var views = Directory.CreateDirectory(Path.Combine(baseDir.Value, "views"));
        await File.WriteAllTextAsync(Path.Combine(views.FullName, "tpl.cshtml"), "will be @(Model.Slug).html");

        var cache = Path.Combine(baseDir.Value, ".cache");

        using var factory = new NullLoggerFactory();
        for (var i = 0; i < 2; i++) // ensure reusing it works
        {
            var html = await new ViewRenderer(
                new SBSharpConfiguration
                {
                    Build = new SBSharpConfiguration.BuildConfiguration
                    {
                        RazorLocalCache = cache
                    },
                    Input = new SBSharpConfiguration.InputConfiguration
                    {
                        Location = baseDir.Value,
                        View = "views"
                    }
                },
                new Logger<ViewRenderer>(factory)
            ).RenderAsync(
                "tpl",
                NewPage()
            );
            Assert.Equal("will be some-sample-file.html", html);

            var compiled = Path.Combine(cache, "db4c8ac4aea85fd63a61216cfc41d1dc.dll");
            Assert.True(File.Exists(compiled), compiled);
        }
    }

    private static Page NewPage()
    {
        return new Page( // fake a loaded model, we just use the slug here
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
        );
    }
}
