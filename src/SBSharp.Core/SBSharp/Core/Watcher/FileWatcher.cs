using Microsoft.Extensions.Logging;
using SBSharp.Core.Configuration;

namespace SBSharp.Core.Watcher;

public class FileWatcher(SBSharpConfiguration configuration, ILogger<FileWatcher> logger)
{
    public IDisposable Watch(Func<Task> onChange)
    {
        var assets = Path.Combine(configuration.Input.Location, configuration.Input.AssetsLocation);
        var views = Path.Combine(configuration.Input.Location, configuration.Input.View);
        var fusionSourcesAndAssets =
            assets != configuration.Input.AssetsLocation || !Directory.Exists(assets);
        var fusionSourcesAndViews = views != configuration.Input.View || !Directory.Exists(views);

        var releaseActions = new List<DisposableAction>(2);

        var viewConf = new SBSharpConfiguration.GlobbingConfiguration { Includes = ["*.cshtml"] };
        if (fusionSourcesAndAssets && fusionSourcesAndViews)
        {
            releaseActions.Add(
                DoWatch(
                    onChange,
                    configuration.Input.Location,
                    configuration.Input.Sources,
                    configuration.Input.Assets,
                    viewConf
                )
            );
        }
        else if (fusionSourcesAndViews)
        {
            releaseActions.Add(
                DoWatch(
                    onChange,
                    configuration.Input.Location,
                    configuration.Input.Sources,
                    viewConf
                )
            );
            releaseActions.Add(DoWatch(onChange, assets, configuration.Input.Assets));
        }
        else if (fusionSourcesAndAssets)
        {
            releaseActions.Add(
                DoWatch(
                    onChange,
                    configuration.Input.Location,
                    configuration.Input.Sources,
                    configuration.Input.Assets
                )
            );
            releaseActions.Add(DoWatch(onChange, views, viewConf));
        }
        else
        {
            releaseActions.Add(
                DoWatch(onChange, configuration.Input.Location, configuration.Input.Sources)
            );
            releaseActions.Add(DoWatch(onChange, assets, configuration.Input.Assets));
            releaseActions.Add(DoWatch(onChange, views, viewConf));
        }

        return new DisposableAction(() =>
        {
            foreach (var it in releaseActions)
            {
                it.Dispose();
            }
        });
    }

    private DisposableAction DoWatch(
        Func<Task> onChange,
        string location,
        params SBSharpConfiguration.GlobbingConfiguration[] globbing
    )
    {
        FileSystemWatcher watcher =
            new(location)
            {
                IncludeSubdirectories = true,
                NotifyFilter =
                    NotifyFilters.Attributes
                    | NotifyFilters.Size
                    | NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
            };

        // we are not 100% equivalent between globbing and watched filters
        // but it enables to detect changes well enough
        if (
            globbing.SelectMany(it => it.Includes).Any(it => it.EndsWith("**") || it.EndsWith("/*"))
        )
        {
            watcher.Filters.Add("*.*");
        }
        else
        {
            foreach (var it in globbing)
            {
                RegisterFiles(watcher, it);
            }
        }

        // avoid to call onChange() in chain so await "last" event before re-rendering
        CancellationTokenSource?[] cancellation = [null];
        async void debounce()
        {
            var token = new CancellationTokenSource();
            lock (cancellation)
            {
                ResetToken(cancellation);
                cancellation[0] = token;
            }

            try
            {
                await Task.Delay(configuration.Watch.Debouncing, token.Token);
            }
            catch (TaskCanceledException)
            {
                // no-op, not important, we use it exactly for that purpose
            }
            if (!token.IsCancellationRequested)
            {
                await onChange();
            }
        }

        watcher.Changed += (_, _) => debounce();
        watcher.Created += (_, _) => debounce();
        watcher.Deleted += (_, _) => debounce();
        watcher.Renamed += (_, _) => debounce();

        logger.LogInformation(
            "Watching change in '{Directory}' for patterns {Patterns}",
            location,
            watcher.Filters
        );
        watcher.EnableRaisingEvents = true;

        return new DisposableAction(() =>
        {
            watcher.Dispose();
            lock (cancellation)
            {
                ResetToken(cancellation);
            }
        });
    }

    // glob uses file path but watcher uses file names so we just skip the rest
    private static void RegisterFiles(
        FileSystemWatcher watcher,
        SBSharpConfiguration.GlobbingConfiguration assets
    )
    {
        foreach (var it in assets.Includes)
        {
            var end = it.LastIndexOf('/');
            var pattern = it[(end + 1)..it.Length];
            if (!watcher.Filters.Contains(pattern))
            {
                watcher.Filters.Add(pattern);
            }
        }
    }

    private static void ResetToken(CancellationTokenSource?[] cancellation)
    {
        var token = cancellation[0];
        if (token is not null && !token.IsCancellationRequested)
        {
            token.Cancel();
            token.Dispose();
        }
    }

    internal class DisposableAction(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
