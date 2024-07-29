using Microsoft.Extensions.Logging;
using SBSharp.Core.Configuration;
using SBSharp.Core.Watcher;

namespace SBSharp.Core.Command;

public class WatchCommand(
    SBSharpConfiguration configuration,
    FileWatcher watcher,
    BuildCommand delegateCommand,
    ILogger<WatchCommand> logger
)
{
    public async Task<int> InvokeAsync()
    {
        async Task onChange()
        {
            logger.LogInformation("Rebuilding '{Directory}'", configuration.Input.Location);
            try
            {
                await delegateCommand.InvokeAsync();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Rendering failed");
            }
        }

        // initial build
        await onChange();

        using var w = watcher.Watch(onChange);

        using var canceller = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, args) => canceller.Cancel();
        await Task.Delay(Timeout.Infinite, canceller.Token);

        return 0;
    }
}
