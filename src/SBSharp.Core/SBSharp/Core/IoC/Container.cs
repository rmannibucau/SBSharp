using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SBSharp.Core.Command;
using SBSharp.Core.Configuration;
using SBSharp.Core.Scanner;
using SBSharp.Core.View;
using SBSharp.Core.Watcher;

namespace SBSharp.Core.IoC;

public class Container : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly string[] args;

    public Container(string[] args)
    {
        this.args = args;

        var configuration = new SBSharpConfiguration();
        new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("sbsharp.json", true)
            .AddEnvironmentVariables()
            .AddCommandLine(args.Length == 0 ? args : args[1..])
            .Build()
            .GetSection("sbsharp")
            .Bind(configuration);

        var beans = new ServiceCollection();

        // stack
        beans.AddLogging(config => config.AddConsole());
        beans.AddSingleton(configuration);

        // services
        beans.AddTransient<SourceScanner>();
        beans.AddTransient<ViewRenderer>();
        beans.AddTransient<FileWatcher>();
        beans.AddTransient<CommandExecutor>();

        // commands
        beans.AddTransient<BuildCommand>();
        beans.AddTransient(p => new Lazy<BuildCommand>(() => p.GetService<BuildCommand>()!));
        beans.AddTransient<ServeCommand>();
        beans.AddTransient(p => new Lazy<ServeCommand>(() => p.GetService<ServeCommand>()!));
        beans.AddTransient<WatchCommand>();
        beans.AddTransient(p => new Lazy<WatchCommand>(() => p.GetService<WatchCommand>()!));

        provider = beans.BuildServiceProvider();
    }

    public async Task<int> RunAsync()
    {
        return await provider
            .GetService<CommandExecutor>()!
            .ExecuteAsync(args.Length == 0 ? "" : args[0]);
    }

    public void Stop()
    {
        provider.Dispose();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Stop();
    }
}
