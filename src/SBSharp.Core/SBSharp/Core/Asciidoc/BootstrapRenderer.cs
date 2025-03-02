using NAsciidoc.Model;
using NAsciidoc.Renderer;

namespace SBSharp.Core.Asciidoc;

public class BootstrapRender(AsciidoctorLikeHtmlRenderer.Configuration configuration)
    : AsciidoctorLikeHtmlRenderer(configuration)
{
    public override void VisitAdmonition(Admonition element)
    {
        string name = Enum.GetName(element.Level)!;
        builder
            .Append(" <div class=\"alert alert-")
            .Append(
                element.Level switch
                {
                    Admonition.AdmonitionLevel.Note => "info",
                    Admonition.AdmonitionLevel.Tip => "success",
                    Admonition.AdmonitionLevel.Important => "primary",
                    Admonition.AdmonitionLevel.Warning => "warning",
                    Admonition.AdmonitionLevel.Caution => "danger",
                    _ => "secondary",
                }
            )
            .Append("\" role=\"alert\">");
        builder
            .Append(" <h4 class=\"alert-heading\"><b>")
            .Append(char.ToUpperInvariant(name[0]))
            .Append(name[1..])
            .Append("</b></h4>\n");
        VisitElement(element.Content);
        builder.Append(" </div>\n");
    }
}
