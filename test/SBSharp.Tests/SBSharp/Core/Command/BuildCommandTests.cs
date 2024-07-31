using System.Text.RegularExpressions;
using SBSharp.Core.IoC;
using SBSharp.Tests.Temp;

namespace SBSharp.Core.Command;

public class BuildCommandTests
{
    [Fact]
    public async Task Build()
    {
        using var tmp = new TempFolder("BuildCommandTests_Build");
        var input = Path.Combine(tmp.Value, "src");
        var output = Path.Combine(tmp.Value, "_site");
        CreateBlog(input);

        using var container = new Container(
            [
                "build",
                $"--sbsharp:Input:Location={input}",
                // global post index - we use author by convention but this is not needed
                "--sbsharp:Input:VirtualPages:0:Slug=blog/{Page}",
                "--sbsharp:Input:VirtualPages:0:Title=Posts page {Page}",
                "--sbsharp:Input:VirtualPages:0:View=post-list",
                "--sbsharp:Input:VirtualPages:0:Paginated=true",
                "--sbsharp:Input:VirtualPages:0:PerValue=false",
                "--sbsharp:Input:VirtualPages:0:CriteriaAttribute=author",
                "--sbsharp:Input:VirtualPages:0:OrderByAttribute=author",
                "--sbsharp:Input:VirtualPages:0:ReverseOrderBy=false",
                // per author index
                "--sbsharp:Input:VirtualPages:1:Slug=blog/author/{Value}/{Page}",
                "--sbsharp:Input:VirtualPages:1:Title=Posts of {Value}, page {Page}",
                "--sbsharp:Input:VirtualPages:1:View=post-list",
                "--sbsharp:Input:VirtualPages:1:Paginated=true",
                "--sbsharp:Input:VirtualPages:1:PerValue=true",
                "--sbsharp:Input:VirtualPages:1:CriteriaAttribute=author",
                "--sbsharp:Input:VirtualPages:1:OrderByAttribute=author",
                "--sbsharp:Input:VirtualPages:1:ReverseOrderBy=false",
                // output
                $"--sbsharp:Output:Location={output}",
                "--sbsharp:Output:Attributes:Key1=Value1",
                "--sbsharp:Output:Attributes:Key2=Value2" // as a reminder of the syntax
            ]
        );
        await container.RunAsync();

        Assert.Equal(".test {}", File.ReadAllText(Path.Combine(output, "css/test.css")));
        Assert.Equal(
            """
<?xml version="1.0" encoding="UTF-8" ?>
<rss version="2.0">
  <channel>
    <title>Blog</title>
    <description>Blog</description>
    <link>http://localhost:4200</link>
    <copyright>Built with SBSharp</copyright>
    <ttl>1800</ttl>
    <lastBuildDate>$date</lastBuildDate>
    <pubDate>$date</pubDate>
    <item>
      <title>Index</title>
      <description>Index
Bla bla</description>
      <link>http://localhost:4200/index.adoc</link>
      <guid isPermaLink="false">index.adoc</guid>
      <pubDate>$date</pubDate>
    </item>
    <item>
      <title>Post #1</title>
      <description>Post #1
A post with default template.</description>
      <link>http://localhost:4200/blog/post-1/simple-test.adoc</link>
      <guid isPermaLink="false">blog/post-1/simple-test.adoc</guid>
      <pubDate>$date</pubDate>
    </item>
</channel>
</rss>

""",
            new Regex("ate>[^<]+</(.+)ate>").Replace(
                File.ReadAllText(Path.Combine(output, "rss.xml")),
                "ate>$date</$1ate>"
            )
        );
        Assert.Equal(
            "{\"items\":[{\"slug\":\"index\",\"title\":\"Index\",\"description\":\"Index\\nBla bla\","
                + "\"attributes\":{\"title\":\"Index\",\"description\":\"Index\\nBla bla\","
                + "\"body\":\" <div class=\\\"paragraph\\\">\\n <p>\\nBla bla\\n </p>\\n </div>\\n\",\"publishedon\":\"0001-01-01T00:00:00.000\"}},"
                + "{\"slug\":\"simple-test\",\"title\":\"Post #1\",\"description\":\"Post #1\\nA post with default template.\","
                + "\"attributes\":{\"title\":\"Post #1\",\"description\":\"Post #1\\nA post with default template.\","
                + "\"body\":\" <div class=\\\"paragraph\\\">\\n <p>\\nA post with default template.\\n </p>\\n </div>\\n\",\"publishedon\":\"0001-01-01T00:00:00.000\"}}]}",
            File.ReadAllText(Path.Combine(output, "index.json"))
        );

        var files = Directory
            .EnumerateFiles(
                output,
                "*.html",
                new EnumerationOptions { RecurseSubdirectories = true }
            )
            .Aggregate(
                new Dictionary<string, string>(),
                (agg, it) =>
                {
                    agg[Path.GetRelativePath(output, it)] = File.ReadAllText(it);
                    return agg;
                }
            );
        Assert.Equivalent(
            new Dictionary<string, string>
            {
                {
                    "index.html",
                    """
<html>
 <head>
   <title>Index</title>
   <meta name="slug" content="index">
   <meta name="attr" content="Value1">
   
   
 </head>
 <body>
 <div class="landing>
   <div class="paragraph">
 <p>
Bla bla
 </p>
 </div>

</div>
 
 </body>
</html>
"""
                },
                {
                    "blog/post-1/simple-test.html",
                    """
<html>
 <head>
   <title>Post #1</title>
   <meta name="slug" content="simple-test">
   <meta name="attr" content="local">
   
   
 </head>
 <body>
 <!-- top -->
<div class="default>
   <div class="paragraph">
 <p>
A post with default template.
 </p>
 </div>

</div>
 
 </body>
</html>
"""
                },
                {
                    "blog/1.html",
                    """
<html>
 <head>
   <title>Posts page 1</title>
   <meta name="slug" content="blog/1">
   <meta name="attr" content="Value1">
   
   
 </head>
 <body>
 <div class="default>
      <ul>
            <li>
                <a href="simple-test">Post #1</a>
            </li>
    </ul>
</div>
 
 </body>
</html>
"""
                },
                {
                    "blog/author/rmannibucau/1.html",
                    """
<html>
 <head>
   <title>Posts of rmannibucau, page 1</title>
   <meta name="slug" content="blog/author/rmannibucau/1">
   <meta name="attr" content="Value1">
   
   
 </head>
 <body>
 <div class="default>
      <ul>
            <li>
                <a href="simple-test">Post #1</a>
            </li>
    </ul>
</div>
 
 </body>
</html>
"""
                }
            },
            files
        );
    }

    private void CreateBlog(string root)
    {
        var css = Directory.CreateDirectory(Path.Combine(root, "_assets/css")).FullName;
        File.WriteAllText(Path.Combine(css, "test.css"), ".test {}");

        var views = Directory.CreateDirectory(Path.Combine(root, "_views")).FullName;
        File.WriteAllText(
            Path.Combine(views, "_Layout.cshtml"),
            """
            <html>
             <head>
               <title>@Model.Document.Header.Title</title>
               <meta name="slug" content="@Model.Slug">
               <meta name="attr" content="@(Model.Attribute("Key1") ?? "none")">
               @RenderSection("Meta", required: false)
               @RenderSection("Styles", required: false)
             </head>
             <body>
             @RenderBody()
             @RenderSection("Scripts", required: false)
             </body>
            </html>
            """
        );
        File.WriteAllText(Path.Combine(views, "top.cshtml"), "<!-- top -->\n");
        File.WriteAllText(
            Path.Combine(views, "landing.cshtml"),
            """
            @{ Layout = "_Layout"; }
            <div class="landing>
              @Raw(Model.Body())
            </div>
            """
        );
        File.WriteAllText(
            Path.Combine(views, "default.cshtml"),
            """
            @{ Layout = "_Layout"; await IncludeAsync("top.cshtml", Model); }
            <div class="default>
              @Raw(Model.Body())
            </div>
            """
        );
        File.WriteAllText(
            Path.Combine(views, "post-list.cshtml"),
            """
            @{ Layout = "_Layout"; }
            <div class="default>
              @if (Model.Context.Pages.Count == 0)
              {
                <p>No post</p>
              }
              else
              {
                <ul>
                    @foreach (var post in Model.Context.Pages)
                    {
                        <li>
                            <a href="@post.Slug">@post.Document.Header.Title</a>
                        </li>
                    }
                </ul>
              }
            </div>
            """
        );

        File.WriteAllText(
            Path.Combine(root, "index.adoc"),
            """
            = Index
            :view: landing

            Bla bla
            """
        );

        File.WriteAllText(
            Path.Combine(root, "index.ignored"),
            """
            = Ignored

            Must not be rendered since it is not a .adoc/.asciidoc.
            """
        );

        var sub = Directory.CreateDirectory(Path.Combine(root, "blog/post-1")).FullName;
        File.WriteAllText(
            Path.Combine(sub, "simple-test.adoc"),
            """
            = Post #1
            :Key1: local
            :author: rmannibucau

            A post with default template.
            """
        );
    }
}
