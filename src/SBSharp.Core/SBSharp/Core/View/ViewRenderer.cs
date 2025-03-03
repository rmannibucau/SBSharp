using System.Collections.Concurrent;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using RazorLight;
using RazorLight.Caching;
using RazorLight.Compilation;
using SBSharp.Core.Configuration;

namespace SBSharp.Core.View;

public class ViewRenderer
{
    private readonly RazorLightEngine engine;
    private readonly ConcurrentDictionary<string, Task<Func<ITemplatePage>>> cache = new();
    private readonly SBSharpConfiguration configuration;
    private readonly ILogger<ViewRenderer> logger;
    private int initDone = 0;

    public ViewRenderer(SBSharpConfiguration configuration, ILogger<ViewRenderer> logger)
    {
        this.configuration = configuration;
        this.logger = logger;
        var root = Root();
        logger.LogInformation("Using view directory '{Directory}'", root);

        var builder = new RazorLightEngineBuilder()
            .SetOperatingAssembly(typeof(ViewRenderer).Assembly)
            .UseOptions(new RazorLightOptions { EnableDebugMode = false, PreRenderCallbacks = [] });
        if (Directory.Exists(root))
        {
            builder.UseFileSystemProject(root);
        }
        if (!string.IsNullOrEmpty(configuration.Build.RazorLocalCache))
        {
            builder.UseCachingProvider(
                new FileSystemCachingProvider(
                    root,
                    configuration.Build.RazorLocalCache,
                    // hash strategy assumes file exists so we can't use it the first time
                    new SimpleFileCachingStrategy()
                )
            );
        }
        else if (!configuration.Serve.WatchEnabled)
        {
            builder.UseMemoryCachingProvider();
        }
        engine = builder.Build();
    }

    private string Root()
    {
        return Path.Combine(
            Directory.GetCurrentDirectory(),
            configuration.Input.Location,
            configuration.Input.View
        );
    }

    public async Task<string> RenderAsync(string view, Page page)
    {
        // RazorLight has a caching bug (leading to computing N times > 1 the same view
        // and compilation with roselyn is slow)
        // so we add our caching layer on top of it
        try
        {
            var promise = cache.GetOrAdd(
                view,
                async k =>
                {
                    // try in the cache first (filesystem case)
                    if (
                        engine.Handler.IsCachingEnabled
                        && engine.Handler.Cache.RetrieveTemplate($"{view}.cshtml")
                            is { Success: true } result
                    )
                    {
                        return result.Template.TemplatePageFactory;
                    }
                    var templateDescriptor = await engine.Handler.Compiler.CompileAsync(k);
                    return engine.Handler.FactoryProvider.CreateFactory(templateDescriptor);
                }
            );
            var template = await promise.ConfigureAwait(false);
            return await engine.RenderTemplateAsync(template(), page).ConfigureAwait(false);
        }
        catch (TemplateCompilationException e)
        {
            throw new InvalidOperationException($"Error compiling view '{view}': {e.Message}", e);
        }
    }

    public async Task MaybeInit()
    {
        if (Interlocked.Exchange(ref initDone, 1) == 1) // skip in serve mode
        {
            return;
        }
        if (string.IsNullOrEmpty(configuration.Input.RemoteView.Url))
        {
            return;
        }

        var root = Root();
        logger.LogInformation(
            "Downloading '{Url}' to '{Location}'",
            configuration.Input.RemoteView.Url,
            root
        );

        var httpRequestMessage = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(configuration.Input.RemoteView.Url),
        };
        if (!string.IsNullOrEmpty(configuration.Input.RemoteView.Authorization))
        {
            httpRequestMessage.Headers.Add(
                "Authorization",
                configuration.Input.RemoteView.Authorization
            );
        }

        using var httpClient = new HttpClient(
            new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }
        );
        using var response = await httpClient
            .SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Invalid view download: HTTP {Status}", response.StatusCode);
            response.EnsureSuccessStatusCode(); // make it fail
        }
        using var zip = new ZipArchive(
            await response.Content.ReadAsStreamAsync().ConfigureAwait(false)
        );
        zip.ExtractToDirectory(
            string.IsNullOrEmpty(configuration.Input.RemoteView.OverrideView)
                ? configuration.Input.View
                : configuration.Input.RemoteView.OverrideView
        );
    }
}
