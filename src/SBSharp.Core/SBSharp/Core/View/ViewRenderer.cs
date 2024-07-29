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

        string root;
        if (Path.IsPathRooted(configuration.Input.View))
        {
            root = configuration.Input.View;
        }
        else
        {
            root = Path.Combine(configuration.Input.Location, configuration.Input.View);
        }
        logger.LogInformation("Using view directory '{Directory}'", root);

        engine = new RazorLightEngineBuilder()
            .SetOperatingAssembly(typeof(ViewRenderer).Assembly)
            .UseFileSystemProject(root)
            .UseMemoryCachingProvider()
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
