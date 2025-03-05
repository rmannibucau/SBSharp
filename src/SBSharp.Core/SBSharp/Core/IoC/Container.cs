using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SBSharp.Core.Command;
using SBSharp.Core.Configuration;
using SBSharp.Core.SBSharp.Core.Rendering;
using SBSharp.Core.Scanner;
using SBSharp.Core.View;
using SBSharp.Core.Watcher;

namespace SBSharp.Core.IoC;

public static class IoCExtensions
{
    public static void RegisterBeans(
        this ServiceCollection beans,
        string[] args,
        bool autoConfigureLogging = true
    )
    {
        var configuration = new SBSharpConfiguration();
        var globalConfiguration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("sbsharp.json", true)
            .AddEnvironmentVariables()
            .AddCommandLine(args.Length == 0 ? args : args[1..])
            .Build();
        globalConfiguration.GetSection("sbsharp").Bind(configuration);

        // stack
        if (autoConfigureLogging)
        {
            beans.AddLogging(config =>
            {
                var loggingConfiguration = globalConfiguration.GetSection("Logging");
                if (!loggingConfiguration.GetChildren().Any())
                {
                    config.AddSimpleConsole(it =>
                    {
                        it.SingleLine = true;
                        it.TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ ";
                    });
                }
                else
                {
                    config.AddConfiguration(loggingConfiguration);
                }
            });
        }

        beans.AddSingleton(configuration);

        // services - overridable
        beans.TryAddTransient<SourceScanner>();
        beans.TryAddTransient<ViewRenderer>();
        beans.TryAddTransient<FileWatcher>();
        beans.TryAddTransient<CommandExecutor>();
        beans.TryAddTransient<IRendererFactory, DefaultRendererFactory>();
        beans.TryAddTransient<IDataResolverProvider, DefaultDataResolverProvider>();

        // commands
        beans.AddTransient<BuildCommand>();
        beans.AddTransient(p => new Lazy<BuildCommand>(() => p.GetService<BuildCommand>()!));
        beans.AddTransient<ServeCommand>();
        beans.AddTransient(p => new Lazy<ServeCommand>(() => p.GetService<ServeCommand>()!));
        beans.AddTransient<WatchCommand>();
        beans.AddTransient(p => new Lazy<WatchCommand>(() => p.GetService<WatchCommand>()!));
    }
}

public class Container : IDisposable
{
    private readonly string[] args;

    public Container(
        string[] args,
        Action<ServiceCollection>? servicesCustomizer = null,
        bool autoConfigureLogging = true
    )
    {
        this.args = args;

        var beans = new ServiceCollection();
        servicesCustomizer?.Invoke(beans);
        beans.RegisterBeans(this.args, autoConfigureLogging);
        Provider = beans.BuildServiceProvider();
    }

    public ServiceProvider Provider { get; }

    public async Task<int> RunAsync()
    {
        return await Provider
            .GetService<CommandExecutor>()!
            .ExecuteAsync(args.Length == 0 ? string.Empty : args[0]);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Provider.Dispose();
    }
}
