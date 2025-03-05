using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using NAsciidoc.Model;
using NAsciidoc.Parser;
using NAsciidoc.Renderer;
using SBSharp.Core.Asciidoc;
using SBSharp.Core.Configuration;
using SBSharp.Core.Json;
using SBSharp.Core.SBSharp.Core.Rendering;
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
    private readonly IRendererFactory rendererFactoryFactory;

    public BuildCommand(
        SBSharpConfiguration configuration,
        SourceScanner scanner,
        ViewRenderer views,
        IRendererFactory rendererFactoryFactory,
        IDataResolverProvider dataResolverProvider,
        ILogger<BuildCommand> logger
    )
    {
        this.configuration = configuration;
        this.scanner = scanner;
        this.views = views;
        this.logger = logger;
        this.rendererFactoryFactory = rendererFactoryFactory;

        var attributes = new Dictionary<string, string> { { "noheader", "true" } };
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
            AssetsBase = configuration.Input.Location,
            Resolver = dataResolverProvider.Create(),
        };
    }

    public async Task<int> InvokeAsync()
    {
        var outputBase = configuration.Output.Location;

        await views.MaybeInit().ConfigureAwait(false);

        var rendered = RenderAsync().ConfigureAwait(false);
        await CopyAssetsAsync().ConfigureAwait(false);
        var count = await rendered;

        logger.LogInformation(
            "Found {FileCount} files in {Input} and rendered them in {Output}",
            count,
            configuration.Input.Location,
            outputBase
        );

        if (configuration.PostProcessing.Length > 0)
        {
            await PostProcessAsync();
        }

        return 0;
    }

    private async Task PostProcessAsync()
    {
        int counter = 0;
        foreach (var p in configuration.PostProcessing)
        {
            if (!string.IsNullOrWhiteSpace(p.LogMessage))
            {
                logger.LogInformation("{Message}", p.LogMessage);
            }

            var dir = string.IsNullOrWhiteSpace(p.WorkDir)
                ? configuration.Input.Location
                : Path.Combine(configuration.Input.Location, p.WorkDir);

            var info = new ProcessStartInfo
            {
                FileName = p.Command[0],
                Arguments = p.Command.Length == 1 ? "" : string.Join(' ', p.Command[1..]),
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = dir,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (var env in p.Environment)
            {
                var sep = env.IndexOf('=');
                info.EnvironmentVariables[env[0..sep]] = env[(sep + 1)..];
            }

            counter++;

            var proc = new Process { StartInfo = info, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => Console.WriteLine(e.Data);
            proc.ErrorDataReceived += (_, e) => Console.Error.WriteLine(e.Data);

            if (!proc.Start())
            {
                throw new InvalidOperationException(
                    $"Can't start process. '{info.FileName}' (#{counter})"
                );
            }

            using var autoClean = proc;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc!.WaitForExitAsync();
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Invalid exit statut for post-processing '{info.FileName}' (#{counter}): {proc.ExitCode}"
                );
            }
        }
    }

    private async Task<Task> RenderRss(List<(string, Page)> pages, CancellationToken token)
    {
        if (!configuration.Output.Rss.Enabled)
        {
            logger.LogInformation("RSS feed is disabled");
            return await Task.FromResult(Task.CompletedTask);
        }

        return await Task.Run(async () =>
        {
            var xml = Path.Combine(
                configuration.Output.Location,
                configuration.Output.Rss.Location
            );
            Directory.GetParent(xml)!.Create();

            logger.LogInformation("Generating RSS feed at '{Location}'", xml);

            var dateFormat = "ddd, dd MMM yyyy HH:mm:ss zzz";
            var now = DateTime.UtcNow.ToString(dateFormat);

            using var writer = new FileInfo(xml).CreateText();
            await writer.WriteAsync(
                (
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n"
                    + "<rss version=\"2.0\">\n"
                    + "  <channel>\n"
                    + "    <title>"
                    + configuration.Output.Rss.Title
                    + "</title>\n"
                    + "    <description>"
                    + configuration.Output.Rss.Description
                    + "</description>\n"
                    + "    <link>"
                    + configuration.Output.Rss.Link
                    + "</link>\n"
                    + "    <copyright>"
                    + configuration.Output.Rss.Copyright
                    + "</copyright>\n"
                    + "    <ttl>"
                    + configuration.Output.Rss.Ttl
                    + "</ttl>\n"
                    + "    <lastBuildDate>"
                    + now
                    + "</lastBuildDate>\n"
                    + "    <pubDate>"
                    + now
                    + "</pubDate>\n"
                ).AsMemory(),
                token
            );

            string defaultDescription(Page page)
            {
                // do not use raw html there:
                // var body = page.Body();
                var body = RenderText(page.Document);
                return body[..Math.Min(body.Length, 100)];
            }

            foreach (
                var page in pages
                    .OrderByDescending(it => it.Item2.PublishedOn)
                    .ThenBy(it => it.Item2.Document.Header.Title)
            )
            {
                var header = page.Item2.Document.Header;
                if (header.Attributes.TryGetValue("rss-skip", out var v) && v != "false")
                {
                    continue;
                }

                var description = header.Attributes.TryGetValue("rss-description", out var d1)
                    ? d1
                    : (
                        header.Attributes.TryGetValue("description", out var d2)
                            ? d2
                            : (
                                header.Attributes.TryGetValue("summary", out var d3)
                                    ? d3
                                    : defaultDescription(page.Item2)
                            )
                    );
                var pageItem =
                    "    <item>\n"
                    + $"      <title>{SecurityElement.Escape(header.Title)}</title>\n"
                    + $"      <description>{SecurityElement.Escape(description)}</description>\n"
                    + $"      <link>{configuration.Output.Rss.Link}/{page.Item1}</link>\n"
                    + $"      <guid isPermaLink=\"false\">{page.Item1}</guid>\n"
                    + $"      <pubDate>{new DateTime(page.Item2.PublishedOn, TimeOnly.MinValue).ToString(dateFormat)}</pubDate>\n"
                    + "    </item>\n";
                await writer.WriteAsync(pageItem.AsMemory(), token);
            }

            await writer.WriteAsync("</channel>\n</rss>\n".AsMemory(), token);

            return Task.CompletedTask;
        });
    }

    // todo
    private async Task<Task> RenderIndexJson(List<(string, Page)> pages, CancellationToken token)
    {
        if (!configuration.Output.Index.Enabled)
        {
            logger.LogInformation("(JSON) Indexation is disabled");
            return await Task.FromResult(Task.CompletedTask);
        }

        return await Task.Run(async () =>
        {
            var json = Path.Combine(
                configuration.Output.Location,
                configuration.Output.Index.Location
            );
            Directory.GetParent(json)!.Create();

            logger.LogInformation("Generating JSON index at '{Location}'", json);

            string defaultDescription(Page page)
            {
                // do not use raw html there:
                // var body = page.Body();
                var body = RenderText(page.Document);
                return body[..Math.Min(body.Length, 100)];
            }

            var docs = new List<IndexDocument>(pages.Count);
            foreach (
                var page in pages
                    // order is not very important there but ease testing and manual review
                    .OrderByDescending(it => it.Item2.PublishedOn)
                    .ThenBy(it => it.Item2.Document.Header.Title)
            )
            {
                var header = page.Item2.Document.Header;
                if (header.Attributes.TryGetValue("index-skip", out var v) && v != "false")
                {
                    continue;
                }

                var description = header.Attributes.TryGetValue("index-description", out var d1)
                    ? d1
                    : (
                        header.Attributes.TryGetValue("description", out var d2)
                            ? d2
                            : defaultDescription(page.Item2)
                    );
                var indexAttributes =
                    configuration.Output.Index.IndexedAttributes
                    ?? ["index-title", "index-description", "index-body", "index-publishedon"];
                var attributes = indexAttributes
                    .Select(it =>
                    {
                        var value = it switch
                        {
                            // virtual attributes
                            "index-title" => header.Title,
                            "index-body" => page.Item2.Body(),
                            "index-description" => description,
                            "index-gravatar" => page.Item2.Gravatar,
                            "index-publishedon" => new DateTime(
                                page.Item2.PublishedOn,
                                TimeOnly.MinValue
                            ).ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
                            // banalized attribute (custom)
                            _ => header.Attributes.TryGetValue(it, out var v1)
                                ? v1
                                : (header.Attributes.TryGetValue(it, out var v2) ? v2 : ""),
                        };
                        return (
                            Key: it.StartsWith("index-") ? it["index-".Length..] : it,
                            Value: value
                        );
                    })
                    .ToDictionary();

                docs.Add(new IndexDocument(page.Item2.Slug, header.Title, description, attributes));
            }

            await File.WriteAllTextAsync(
                json,
                JsonSerializer.Serialize(
                    new JsonIndex(docs),
                    new SBSharpJsonContext(
                        new JsonSerializerOptions
                        {
                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        }
                    ).JsonIndex
                ),
                token
            );

            return Task.CompletedTask;
        });
    }

    private string RenderText(Document doc)
    {
        var renderer = new TextRenderer();
        renderer.Visit(doc);
        return renderer.Result();
    }

    private string RenderAdoc(Document document)
    {
        AsciidoctorLikeHtmlRenderer.Configuration options;
        if (document.Header.Attributes.Count > 0)
        {
            var attributes = new Dictionary<string, string>(renderingConfiguration.Attributes);
            foreach (var kv in document.Header.Attributes)
            {
                attributes[kv.Key] = kv.Value;
            }

            options = new AsciidoctorLikeHtmlRenderer.Configuration
            {
                Attributes = attributes,
                DataUriForAscii2Svg = renderingConfiguration.DataUriForAscii2Svg,
                SkipGlobalContentWrapper = renderingConfiguration.SkipGlobalContentWrapper,
                SupportDataAttributes = renderingConfiguration.SupportDataAttributes,
                AssetsBase = renderingConfiguration.AssetsBase,
                Resolver = renderingConfiguration.Resolver,
            };
        }
        else
        {
            options = renderingConfiguration;
        }

        var renderer = rendererFactoryFactory.Create(options);
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

            if (File.Exists(target))
            {
                File.Delete(target);
            }

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
                    PreallocationSize = source.Length,
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
            CancellationToken = new CancellationToken(),
        };

        var pages = new List<(string, Page)>(16);
        await LoadPages(pages, blockOptions);

        blockOptions.CancellationToken.ThrowIfCancellationRequested();

        var tasks = await Task.WhenAll(
                RenderRss(pages, blockOptions.CancellationToken),
                RenderIndexJson(pages, blockOptions.CancellationToken),
                RenderPages(pages, blockOptions),
                RenderVirtualPages(pages, blockOptions)
            )
            .ConfigureAwait(false);

        // double await since we use actionblock for a few tasks
        await Task.WhenAll(tasks).ConfigureAwait(false);
        blockOptions.CancellationToken.ThrowIfCancellationRequested();

        return pages.Count;
    }

    private async Task LoadPages(
        List<(string, Page)> pages,
        ExecutionDataflowBlockOptions blockOptions
    )
    {
        DateTimeOffset? notBefore = configuration.Output.NotBeforeToday
            ? DateTimeOffset.UtcNow
            : null;
        if (notBefore is not null)
        {
            logger.LogInformation("Using today={Today}", notBefore);
        }

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
                    if (postDate is not null)
                    {
                        var test = new DateTimeOffset(
                            DateOnly.ParseExact(postDate, "yyyyMMdd", CultureInfo.InvariantCulture),
                            TimeOnly.MinValue,
                            TimeSpan.Zero
                        );
                        if (test > notBefore)
                        {
                            logger.LogInformation(
                                "Ignoring {file} since it is not yet published ({Date})",
                                file,
                                test
                            );
                            return;
                        }
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
            return await Task.FromResult(Task.CompletedTask);
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

                var target = Path.Combine(
                    configuration.Output.Location,
                    $"{spec.Slug.Replace("{Page}", "1").Replace("{Value}", "v")}.html"
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
                        await Task.CompletedTask.ConfigureAwait(false); // if no perValue item
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
                : file.Replace(Path.DirectorySeparatorChar, '/');
            if (slugValue.EndsWith(".adoc"))
            {
                slugValue = slugValue[..^5];
            }
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
        var target = Path.Combine(configuration.Output.Location, $"{model.Slug}.html");
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
