using Microsoft.Extensions.Logging;

namespace SBSharp.Core.Command;

// SBSharp relies on sbsharp.json (or env vars) so there is no real configuration/options
// (all is handled by configuration binder)
// only the first argument is used to detect the command
public class CommandExecutor(
    ILogger<CommandExecutor> logger,
    Lazy<BuildCommand> build,
    Lazy<ServeCommand> serve,
    Lazy<WatchCommand> watch
)
{
    public async Task<int> ExecuteAsync(string command)
    {
        return await (
            command switch
            {
                "build" => build.Value.InvokeAsync(),
                "serve" => serve.Value.InvokeAsync(),
                "watch" => watch.Value.InvokeAsync(),
                // todo: serve+watch
                _ => UnknownCommand(command),
            }
        ).ConfigureAwait(false);
    }

    private Task<int> UnknownCommand(string cmd)
    {
        logger.LogInformation("Unknown command '{Command}'", cmd);
        return Task.FromResult(1);
    }
}
