using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RazorLight;
using RazorLight.Compilation;
using SBSharp.Core.Configuration;

namespace SBSharp.Core.View;

public class ViewRenderer
{
    private readonly SBSharpConfiguration configuration;
    private readonly RazorLightEngine engine;
    private readonly ConcurrentDictionary<string, Task<Func<ITemplatePage>>> cache = new();

    public ViewRenderer(SBSharpConfiguration configuration, ILogger<ViewRenderer> logger)
    {
        this.configuration = configuration;

        var root = Path.Combine(Directory.GetCurrentDirectory(), configuration.Input.Location,
            configuration.Input.View);
        logger.LogInformation("Using view directory '{Directory}'", root);

        var builder = new RazorLightEngineBuilder()
            .SetOperatingAssembly(typeof(ViewRenderer).Assembly)
            .UseFileSystemProject(root)
            .UseOptions(new RazorLightOptions
            {
                EnableDebugMode = false
            });
        if (!this.configuration.Serve.WatchEnabled)
        {
            builder.UseMemoryCachingProvider();
        }
        engine = builder.Build();
    }

    public async Task<string> RenderAsync(string view, Page page)
    {
        // razorlight has a caching bug (leading to computing N times > 1 the same view and compilation with roselyn is slow)
        // so we add our caching layer on top of it
        try
        {
            var promise = cache.GetOrAdd(view, async k =>
            {
                var templateDescriptor = await engine.Handler.Compiler.CompileAsync(k);
                return engine.Handler.FactoryProvider.CreateFactory(templateDescriptor);
            });
            var template = await promise.ConfigureAwait(false);
            return await engine.RenderTemplateAsync(template(), page).ConfigureAwait(false);
        }
        catch (TemplateCompilationException e)
        {
            throw new InvalidOperationException($"Error compiling view '{view}': {e.Message}", e);
        }
    }
}