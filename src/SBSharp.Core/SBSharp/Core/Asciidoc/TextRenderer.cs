using System.Text;
using NAsciidoc.Model;
using NAsciidoc.Renderer;

namespace SBSharp.Core.Asciidoc;

// todo: enhance for cases where part of words are formatted, ex: *S*BSharp, shouldn't render as "S\nBSharp" but "SBSHarp"
public class TextRenderer : Visitor<string>
{
    private readonly StringBuilder builder = new();

    public override void VisitAdmonition(Admonition element)
    {
        string name = Enum.GetName(element.Level)!;
        builder.Append(name).Append(": ");
        VisitElement(element.Content);
        builder.Append('\n');
    }

    public override void VisitHeader(Header header)
    {
        builder.Append(header.Title).Append('\n');
    }

    public override void VisitSection(Section element)
    {
        VisitElement(element.Title);
        builder.Append('\n');
        base.VisitSection(element);
        builder.Append('\n');
    }

    public override void VisitLink(Link element)
    {
        builder.Append(element.Label).Append('\n');
    }

    public override void VisitDescriptionList(DescriptionList element)
    {
        foreach (var it in element.Children)
        {
            VisitElement(it.Key);
            VisitElement(it.Value);
        }
        builder.Append('\n');
    }

    public override void VisitUnOrderedList(UnOrderedList element)
    {
        foreach (var it in element.Children)
        {
            VisitElement(it);
        }
        builder.Append('\n');
    }

    public override void VisitOrderedList(OrderedList element)
    {
        foreach (var it in element.Children)
        {
            VisitElement(it);
        }
        builder.Append('\n');
    }

    public override void VisitText(Text element)
    {
        builder.Append(element.Value).Append('\n');
    }

    public override void VisitQuote(Quote element)
    {
        foreach (var it in element.Children)
        {
            VisitElement(it);
        }
        builder.Append('\n');
    }

    public override void VisitCode(Code element)
    {
        builder.Append(element.Value).Append('\n');
    }

    public override void VisitTable(Table element)
    {
        foreach (var it in element.Elements)
        {
            foreach (var e in it)
            {
                VisitElement(e);
            }
            builder.Append('\n');
        }
        builder.Append("\n\n");
    }

    public override void VisitAnchor(Anchor element)
    {
        builder.Append(element.Label).Append('\n');
    }

    public override void VisitPassthroughBlock(PassthroughBlock element)
    {
        // skipped for now since it is generally code/internals
    }

    public override void VisitListing(Listing element)
    {
        builder.Append(element.Value).Append('\n');
    }

    public override void VisitMacro(Macro element)
    {
        if (element.Label.Length > 0)
        {
            builder.Append(element.Label).Append('\n');
        }
    }

    public override string Result()
    {
        return builder.ToString().Trim();
    }
}
