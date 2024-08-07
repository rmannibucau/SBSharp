using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using SBSharp.Core.Configuration;
using SBSharp.Core.Watcher;

namespace SBSharp.Core.Command;

public class ServeCommand(
    SBSharpConfiguration configuration,
    FileWatcher watcher,
    BuildCommand delegateCommand,
    ILogger<ServeCommand> logger,
    ILoggerFactory loggerFactory
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

        IDisposable? watcherHandler = null;
        if (configuration.Serve.WatchEnabled)
        {
            watcherHandler = watcher.Watch(onChange);
        }
        else
        {
            logger.LogInformation("Not watching changes");
        }

        try
        {
            var builder = WebApplication.CreateBuilder();
            // align web logging on the standalone application
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new ProvidedLoggerFactoryLoggingProvider(loggerFactory));

            using var app = builder.Build();
            foreach (var it in configuration.Serve.Urls)
            {
                app.Urls.Add(it);
            }
            app.Environment.WebRootFileProvider = new PhysicalFileProvider(
                Path.Combine(Directory.GetCurrentDirectory(), configuration.Output.Location)
            );
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.Run();
        }
        finally
        {
            watcherHandler?.Dispose();
        }

        return await Task.FromResult(0);
    }

    internal class ProvidedLoggerFactoryLoggingProvider(ILoggerFactory factory) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return factory.CreateLogger(categoryName);
        }

        public void Dispose()
        {
            // no-op
        }
    }
}
