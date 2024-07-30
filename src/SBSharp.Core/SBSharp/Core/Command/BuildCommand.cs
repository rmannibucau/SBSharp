using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using NAsciidoc.Model;
using NAsciidoc.Parser;
using NAsciidoc.Renderer;
using SBSharp.Core.Asciidoc;
using SBSharp.Core.Configuration;
using SBSharp.Core.Scanner;
using SBSharp.Core.View;

namespace SBSharp.Core.Command;

public class BuildCommand
{
    private readonly SBSharpConfiguration configuration;
    private readonly SourceScanner scanner;
    private readonly ViewRenderer views;
    private readonly ILogger<BuildCommand> logger;
    private readonly Parser parser;
    private readonly ParserContext ctx;
    private readonly AsciidoctorLikeHtmlRenderer.Configuration renderingConfiguration;

    public BuildCommand(
        SBSharpConfiguration configuration,
        SourceScanner scanner,
        ViewRenderer views,
        ILogger<BuildCommand> logger
    )
    {
        this.configuration = configuration;
        this.scanner = scanner;
        this.views = views;
        this.logger = logger;

        var attributes = new Dictionary<string, string>() { { "noheader", "true" } };
        foreach (var it in configuration.Output.Attributes)
        {
            attributes.Add(it.Key, it.Value);
        }

        parser = new Parser(attributes);
        ctx = new ParserContext(new SimpleContentResolver(configuration.Input));
        renderingConfiguration = new AsciidoctorLikeHtmlRenderer.Configuration
        {
            Attributes = attributes,
            DataUriForAscii2Svg = true,
            SkipGlobalContentWrapper = true,
            SupportDataAttributes = true,
            AssetsBase = configuration.Input.Location
        };
    }

    public async Task<int> InvokeAsync()
    {
        var outputBase = configuration.Output.Location;

        var rendered = RenderAsync().ConfigureAwait(false);
        await CopyAssetsAsync().ConfigureAwait(false);

        var count = await rendered;

        logger.LogInformation(
            "Found {FileCount} files in {Input} and rendered them in {Output}",
            count,
            configuration.Input.Location,
            outputBase
        );

        return 0;
    }

    private string RenderAdoc(Document document)
    {
        // todo: make it bootstrap friendly?
        var renderer = configuration.Output.UseBootstrap
            ? new BootstrapRender(renderingConfiguration)
            : new AsciidoctorLikeHtmlRenderer(renderingConfiguration);
        renderer.Visit(document);
        return renderer.Result();
    }

    private async Task CopyAssetsAsync()
    {
        foreach (var it in scanner.ScanAssets())
        {
            var source = Path.Combine(
                configuration.Input.Location,
                configuration.Input.AssetsLocation,
                it
            );
            var target = Path.Combine(configuration.Output.Location, it);
            Directory.GetParent(target)!.Create();

            using var from = new FileStream(source, FileMode.Open);
            using var to = new FileStream(
                target,
                new FileStreamOptions
                {
                    // disable any buffer, just go to disk
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Options = FileOptions.WriteThrough,
                    BufferSize = 0,
                    PreallocationSize = source.Length
                }
            );
            await from.CopyToAsync(to).ConfigureAwait(false);
        }
    }

    private async Task<int> RenderAsync()
    {
        var blockOptions = new ExecutionDataflowBlockOptions
        // todo: make it configurable?
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
            SingleProducerConstrained = true,
            EnsureOrdered = false,
            TaskScheduler = TaskScheduler.Default,
            BoundedCapacity = 1_024,
            CancellationToken = new CancellationToken()
        };

        var pages = new List<(string, Page)>(16);
        await LoadPages(pages, blockOptions);

        blockOptions.CancellationToken.ThrowIfCancellationRequested();

        var renderBlock = await RenderPages(pages, blockOptions).ConfigureAwait(false);
        var renderVirtualBlock = await RenderVirtualPages(pages, blockOptions)
            .ConfigureAwait(false);

        await Task.WhenAll(renderBlock, renderVirtualBlock).ConfigureAwait(false);
        blockOptions.CancellationToken.ThrowIfCancellationRequested();

        return pages.Count;
    }

    private async Task LoadPages(
        List<(string, Page)> pages,
        ExecutionDataflowBlockOptions blockOptions
    )
    {
        DateTime? notBefore = configuration.Output.NotBeforeToday ? DateTime.Now : null;
        var loadModelBlock = new ActionBlock<string>(
            async file =>
            {
                var page = await LoadPage(file).ConfigureAwait(false);
                if (notBefore is not null)
                {
                    var postDate = page.Document.Header.Attributes.TryGetValue(
                        "published-on",
                        out var date
                    )
                        ? date
                        : null;
                    if (
                        postDate is not null
                        && DateTime.ParseExact(postDate, "yyyyMMdd", CultureInfo.InvariantCulture)
                            > notBefore
                    )
                    {
                        logger.LogInformation(
                            "Ignoring {file} since it is not yet published",
                            file
                        );
                        return;
                    }
                }
                lock (pages)
                {
                    pages.Add((file, page!));
                }
            },
            blockOptions
        );

        foreach (var file in scanner.ScanSources())
        {
            await loadModelBlock.SendAsync(file).ConfigureAwait(false);
        }
        loadModelBlock.Complete();
        await loadModelBlock.Completion;
    }

    private async Task<Task> RenderPages(
        List<(string, Page)> pages,
        ExecutionDataflowBlockOptions blockOptions
    )
    {
        var allPages = pages.Select(it => it.Item2).ToList();
        var renderBlock = new ActionBlock<(string, Page)>(
            async it => await RenderFile(it.Item1, it.Item2).ConfigureAwait(false),
            blockOptions
        );
        foreach (var page in pages)
        {
            // init context before handling the page
            page.Item2.Context.Pages = allPages;
            await renderBlock.SendAsync(page).ConfigureAwait(false);
        }
        renderBlock.Complete();
        return renderBlock.Completion;
    }

    private async Task<Task> RenderVirtualPages(
        List<(string, Page)> pages,
        ExecutionDataflowBlockOptions blockOptions
    )
    {
        if (configuration.Input.VirtualPages.Length == 0)
        {
            return Task.CompletedTask;
        }

        var allPages = pages.Select(it => it.Item2).ToList();
        var block = new ActionBlock<SBSharpConfiguration.PageDefinition>(
            async spec =>
            {
                // pick the items the page works on
                var handled = pages
                    .Select(it => it.Item2)
                    .Where(it => it.Document.Header.Attributes.ContainsKey(spec.CriteriaAttribute))
                    .OrderBy(it =>
                        it.Document.Header.Attributes.TryGetValue(
                            spec.OrderByAttribute,
                            out var value
                        )
                            ? value ?? ""
                            : ""
                    )
                    .ToList();
                if (spec.ReverseOrderBy)
                {
                    handled.Reverse();
                }

                var attributes = new Dictionary<string, string>(spec.Attributes);

                var subFolderIndex = spec.Slug.LastIndexOf('/');
                var target = Path.Combine(
                    configuration.Output.Location,
                    $"{spec.Slug.Replace("{Page}", "1").Replace("{Value}", "")}.html"
                );
                Directory.GetParent(target)!.Create();

                if (!spec.Paginated)
                {
                    logger.LogWarning(
                        "Using virtual pages when pagination is not needed is tolerated but a physical page would be saner, think to replace it {Slug}",
                        spec.Slug
                    );

                    var html = await views.RenderAsync(
                        spec.View,
                        new Page(
                            configuration.Output.Attributes,
                            new Document(
                                new Header(
                                    spec.Title.Replace("{Page}", "1").Replace("{Value}", ""),
                                    null,
                                    null,
                                    ImmutableDictionary<string, string>.Empty
                                ),
                                new Body([])
                            ),
                            () => "",
                            $"{spec.Slug}.html",
                            new Page.GlobalContext { Pages = handled }
                        )
                    );
                    await File.WriteAllTextAsync(target, html).ConfigureAwait(false);
                }
                else
                {
                    if (!spec.PerValue)
                    {
                        logger.LogInformation(
                            "Generating paginated pages for {Criteria}",
                            spec.CriteriaAttribute
                        );
                        await GeneratePages(spec, "", handled).ConfigureAwait(false);
                    }
                    else
                    {
                        var perValue = handled
                            .SelectMany(it =>
                                it.Document.Header.Attributes[spec.CriteriaAttribute]
                                    .Split(
                                        ',',
                                        StringSplitOptions.RemoveEmptyEntries
                                            | StringSplitOptions.TrimEntries
                                    )
                                    .Select(key => (Key: key, Page: it))
                            )
                            .GroupBy(it => it.Key, it => it.Page);
                        foreach (var value in perValue)
                        {
                            logger.LogInformation(
                                "Generating paginated pages for {Criteria}='{Value}'",
                                spec.CriteriaAttribute,
                                value.Key
                            );
                            await GeneratePages(spec, value.Key, [.. value]).ConfigureAwait(false);
                        }
                    }
                }
            },
            blockOptions
        );
        foreach (var page in configuration.Input.VirtualPages)
        {
            await block.SendAsync(page).ConfigureAwait(false);
        }
        block.Complete();
        return block.Completion;
    }

    private async Task GeneratePages(
        SBSharpConfiguration.PageDefinition spec,
        string value,
        List<Page> handled
    )
    {
        var chunks = handled.Chunk(spec.PageSize).ToList();
        if (chunks.Count == 0) // enforce a page
        {
            var html = await views
                .RenderAsync(
                    spec.View,
                    new Page(
                        configuration.Output.Attributes,
                        new Document(
                            new Header(
                                spec.Title.Replace("{Page}", "1").Replace("{Value}", value),
                                null,
                                null,
                                spec.Attributes
                            ),
                            new Body([])
                        ),
                        () => "",
                        spec.Slug,
                        new Page.GlobalContext { Pages = [] }
                    )
                )
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(
                    Path.Combine(
                        configuration.Output.Location,
                        $"{spec.Slug.Replace("{Page}", "1").Replace("{Value}", value)}.html"
                    ),
                    html
                )
                .ConfigureAwait(false);
        }
        else
        {
            var pagesAttributes = new Dictionary<string, string>(spec.Attributes);
            pagesAttributes.TryAdd("paginationTotalPages", chunks.Count.ToString());
            pagesAttributes.TryAdd("paginationAttributeValue", value);

            int page = 1;
            foreach (var chunk in chunks)
            {
                var slug = spec.Slug.Replace("{Page}", page.ToString()).Replace("{Value}", value);
                var pageAttributes = new Dictionary<string, string>(pagesAttributes);
                pageAttributes.TryAdd("paginationCurrentPage", page.ToString());

                var html = await views
                    .RenderAsync(
                        spec.View,
                        new Page(
                            configuration.Output.Attributes,
                            new Document(
                                new Header(
                                    spec.Title.Replace("{Page}", page.ToString())
                                        .Replace("{Value}", value),
                                    null,
                                    null,
                                    pageAttributes
                                ),
                                new Body([])
                            ),
                            () => "",
                            slug,
                            new Page.GlobalContext { Pages = [.. chunk] }
                        )
                    )
                    .ConfigureAwait(false);

                var output = Path.Combine(configuration.Output.Location, $"{slug}.html");
                Directory.GetParent(output)!.Create();
                await File.WriteAllTextAsync(output, html).ConfigureAwait(false);
                page++;
            }
            await Task.CompletedTask; // ensure there is at least one awaiter
        }
    }

    private async Task<Page> LoadPage(string file)
    {
        try
        {
            var input = Path.Combine(configuration.Input.Location, file);
            var adoc = parser.Parse(await File.ReadAllLinesAsync(input).ConfigureAwait(false), ctx);
            var slugValue = adoc.Header.Attributes.TryGetValue("slug", out var slug)
                ? slug
                : Path.GetFileNameWithoutExtension(file);
            return new Page(
                configuration.Output.Attributes,
                adoc,
                () =>
                {
                    try
                    {
                        return RenderAdoc(adoc);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Can't render '{Document}'", file);
                        throw;
                    }
                },
                slugValue,
                new Page.GlobalContext()
            );
        }
        catch (Exception e)
        {
            logger.LogError(e, "Can't load '{Document}'", file);
            throw;
        }
    }

    private async Task RenderFile(string file, Page model)
    {
        var html = await views
            .RenderAsync(
                model.Document.Header.Attributes.TryGetValue("view", out var view)
                    ? view
                    : "default",
                model
            )
            .ConfigureAwait(false);
        var subFolderIndex = file.Replace('\\', '/').LastIndexOf('/');
        var baseOutputDir = subFolderIndex > 0 ? $"{file[0..subFolderIndex]}/" : "";
        var target = Path.Combine(
            configuration.Output.Location,
            $"{baseOutputDir}{model.Slug}.html"
        );
        Directory.GetParent(target)!.Create();
        await File.WriteAllTextAsync(target, html).ConfigureAwait(false);
    }

    internal class SimpleContentResolver(SBSharpConfiguration.InputConfiguration configuration)
        : IContentResolver
    {
        public IEnumerable<string>? Resolve(string reference, Encoding? encoding)
        {
            if (Path.IsPathRooted(reference))
            {
                return File.ReadAllLines(reference);
            }
            return File.ReadAllLines(Path.Combine(configuration.Location, reference));
        }
    }
}
