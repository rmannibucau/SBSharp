using System.ComponentModel;

namespace SBSharp.Core.Configuration;

[Description("Configuration of the Maven local cache and remote repositor(ies).")]
public class SBSharpConfiguration
{
    [Description("Input configuration.")]
    public InputConfiguration Input { get; set; } = new InputConfiguration();

    [Description("Output configuration.")]
    public OutputConfiguration Output { get; set; } = new OutputConfiguration();

    [Description("Watch specific configuration - `serve`and `watch` commands only.")]
    public WatchConfiguration Watch { get; set; } = new WatchConfiguration();

    [Description("Serve specific configuration - `serve` command only.")]
    public ServeConfiguration Serve { get; set; } = new ServeConfiguration();

    [Description("Watch and serve command file system watching specific configuration.")]
    public class WatchConfiguration
    {
        [Description(
            "When enabled, how long (in milliseconds) to await before rebuilding the website - to avoid to keep rebuilding it when typing."
        )]
        public int Debouncing { get; set; } = 250;
    }

    [Description("Serve command specific configuration.")]
    public class ServeConfiguration
    {
        [Description(
            "Should watch mode be enabled for serve command, ie the site rebuilt on detected changes."
        )]
        public bool WatchEnabled { get; set; } = true;

        [Description("Urls to serve from.")]
        public string[] Urls { get; set; } = ["http://localhost:4200"];
    }

    [Description("Site output rendering configuration.")]
    public class OutputConfiguration
    {
        [Description(
            "If `True`, posts where the date set in the attribute `published-on` is before _today_ will be ignored. Expected format: `yyyyMMdd`. "
            + "If no date is found the post is considered published - but it is not expected."
        )]
        public bool NotBeforeToday { get; set; } = true;

        [Description(
            "If `True`, part of asciidoc rendering is replaced by native bootstrap (5) HTML."
                + "The most visible is to replace the standard table based adminiton by bootstrap alerts."
        )]
        public bool UseBootstrap { get; set; } = true;

        [Description("Site output base directory.")]
        public string Location { get; set; } = "_site";

        [Description("Asciidoc (global) attributes to use for the rendering.")]
        public IDictionary<string, string> Attributes { get; set; } =
            new Dictionary<string, string>
            {
                { "partialsdir", "_partials" },
                { "imagesdir", "_images" }
            };
    }

    [Description(
        "Defines a page template - does not need a `.adoc` file. Often used for navigation pages. It is primarly intended for pagination since for other pages you can use a `.adoc` with a specific `view` attribute."
    )]
    public class PageDefinition
    {
        [Description(
            "Slug to use - filename without extension, it must be set since will be the base of the output location of the generated page. "
                + "If - and only if - *paginated* you can use `{Page}` to represent the page number (starting from 1) and `{Value}` representing the attribute value for rendered pages. "
                + "If you want to use path separators, ensure to use `/`. "
                + "Ensure to *NOT* start by a slash or the location will be considered absolute."
        )]
        public string Slug { get; set; } = "";

        [Description("Page title, similarly to the slug you can use page and value variables.")]
        public string Title { get; set; } = "";

        [Description(
            "Page attributes - just forwarded to the view. Note that in the view model you can also query `paginationTotalPages` attribute and `paginationAttributeValue` (if relevant)."
        )]
        public IDictionary<string, string> Attributes { get; set; } =
            new Dictionary<string, string>();

        [Description("View used to render the page(s).")]
        public string View { get; set; } = "default";

        [Description("Is it a paginated kind of page, ie `PageSize` will be used to render it.")]
        public bool Paginated { get; set; } = false;

        [Description(
            "If `True` and paginated, the rendering generates the index per attribute value (think category) "
                + "else it is global whatever the value is - but it ensures it exists. "
                + "Take care that values are split on commas (tags, categories cases) so "
                + "ensure to use an attribute respecting this constraint."
        )]
        public bool PerValue { get; set; } = true;

        [Description("When view is `Paginated`, the page size to use.")]
        public int PageSize { get; set; } = 10;

        [Description(
            "The attribute of the _physical_ pages to use to paginate - page is ignored when not with this attribute."
        )]
        public string CriteriaAttribute { get; set; } = "category";

        [Description("Attribute to order pages by - if missing, empty is used.")]
        public string OrderByAttribute { get; set; } = "published-on";

        [Description("Should sorting be reversed.")]
        public bool ReverseOrderBy { get; set; } = true;
    }

    [Description("Site input configuration - page content, views, assets, ....")]
    public class InputConfiguration
    {
        [Description("Site source base directory.")]
        public string Location { get; set; } = ".";

        [Description("Virtual pages definitions.")]
        public PageDefinition[] VirtualPages { get; set; } = [];

        [Description("View directory, can be absolute or relative to site `Location`.")]
        public string View { get; set; } = "_views";

        [Description("Assets directory, can be absolute or relative to site `Location`.")]
        public string AssetsLocation { get; set; } = "_assets";

        [Description(
            "Assets files globbing (includes/excludes). Often used for pictures and resources."
        )]
        public GlobbingConfiguration Assets { get; set; } =
            new()
            {
                Includes = ["**/*.css", "**/*.js", "**/*.jpg", "**/*.png", "**/*.gif"],
                Excludes = ["**/_site/**"]
            };

        [Description("Site source (pages) file globbing (includes/excludes).")]
        public GlobbingConfiguration Sources { get; set; } =
            new()
            {
                Includes = ["**/*.adoc", "**/*.asciidoc"],
                Excludes = ["**/*.partial.adoc", "**/*.partial.asciidoc"]
            };
    }

    [Description("How to filter files in a directory (includes/excludes).")]
    public class GlobbingConfiguration
    {
        [Description("Included files (glob patterns relative to input location).")]
        public string[] Includes { get; set; } = [];

        [Description("Excluded files (glob patterns relative to input location).")]
        public string[] Excludes { get; set; } = [];
    }
}
