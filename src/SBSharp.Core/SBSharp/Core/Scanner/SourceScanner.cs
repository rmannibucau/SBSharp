using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using SBSharp.Core.Configuration;

namespace SBSharp.Core.Scanner;

public class SourceScanner(SBSharpConfiguration configuration)
{
    public IEnumerable<string> ScanSources()
    {
        var matcher = CreateMatcher(configuration.Input.Sources);

        var views = Path.Combine(configuration.Input.Location, configuration.Input.View);
        var relativeView = Path.GetRelativePath(configuration.Input.Location, views);
        if (!relativeView.StartsWith(".."))
        {
            matcher.AddExclude($"{relativeView}/**");
        }

        var matching = matcher.Execute(
            new DirectoryInfoWrapper(new DirectoryInfo(configuration.Input.Location))
        );
        return matching.Files.Select(it => it.Path);
    }

    public IEnumerable<string> ScanAssets()
    {
        var matcher = CreateMatcher(configuration.Input.Assets);
        var matching = matcher.Execute(
            new DirectoryInfoWrapper(
                new DirectoryInfo(
                    Path.Combine(configuration.Input.Location, configuration.Input.AssetsLocation)
                )
            )
        );
        return matching.Files.Select(it => it.Path);
    }

    private Matcher CreateMatcher(SBSharpConfiguration.GlobbingConfiguration conf)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var it in conf.Includes)
        {
            matcher.AddInclude(it);
        }
        foreach (var it in conf.Excludes)
        {
            matcher.AddExclude(it);
        }
        return matcher;
    }
}
