using NAsciidoc.Model;

namespace SBSharp.Core;

public record Page(
    IDictionary<string, string> GlobalAttributes,
    Document Document,
    Func<string> Body,
    string Slug,
    Page.GlobalContext Context
)
{
    public string? Attribute(string key)
    {
        return Document.Header.Attributes.TryGetValue(key, out var l)
            ? l
            : GlobalAttributes.TryGetValue(key, out var g)
                ? g
                : null;
    }

    public class GlobalContext // lazy init since it requires to have loaded pages
    {
        public List<Page> Pages { get; set; } = [];
    }
}
