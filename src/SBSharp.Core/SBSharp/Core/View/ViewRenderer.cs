using Microsoft.Extensions.Logging;
using RazorLight;
using RazorLight.Compilation;
using SBSharp.Core.Configuration;

namespace SBSharp.Core.View;

public class ViewRenderer
{
    private readonly SBSharpConfiguration configuration;
    private readonly RazorLightEngine engine;

    public ViewRenderer(SBSharpConfiguration configuration, ILogger<ViewRenderer> logger)
    {
        this.configuration = configuration;

        var root = Path.Combine(Directory.GetCurrentDirectory(), configuration.Input.Location, configuration.Input.View);
        logger.LogInformation("Using view directory '{Directory}'", root);

        engine = new RazorLightEngineBuilder()
            .SetOperatingAssembly(typeof(ViewRenderer).Assembly)
            .UseFileSystemProject(root)
            .UseMemoryCachingProvider()
            .UseOptions(new RazorLightOptions {
                
            })
            .Build();
    }

    public async Task<string> RenderAsync(string view, Page page)
    {
        try
        {
            return await engine.CompileRenderAsync(view, page).ConfigureAwait(false);
        }
        catch (TemplateCompilationException e)
        {
            throw new InvalidOperationException($"Error compiling view '{view}': {e.Message}", e);
        }
    }
}
