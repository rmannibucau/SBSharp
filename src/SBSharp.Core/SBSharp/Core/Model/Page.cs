using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NAsciidoc.Model;

namespace SBSharp.Core;

internal record Cache(ConcurrentDictionary<string, string> Gravatar)
{
    internal static readonly Cache INSTANCE = new Cache(new ConcurrentDictionary<string, string>());
}

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

    public DateTime PublishedOn
    {
        get
        {
            var date = Document.Header.Attributes.TryGetValue("published-on", out var p) ? p : "";
            return date.Length == 0
                ? DateTime.MinValue
                : DateTime.ParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture);
        }
    }

    public string Gravatar
    {
        get
        {
            var attributes = Document.Header.Attributes;
            var mail = (
                attributes.TryGetValue("mail", out var m)
                    ? m
                    : (
                        attributes.TryGetValue("author", out var a)
                            ? $"{a.Split(',')[0]}@gmail.com"
                            : ""
                    )
            )
                .Trim()
                .ToLowerInvariant();
            return Cache.INSTANCE.Gravatar.GetOrAdd(
                mail,
                m =>
                {
                    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(m));
                    var gravatarHash = BitConverter
                        .ToString(hash)
                        .Replace("-", "", StringComparison.OrdinalIgnoreCase)
                        .ToLowerInvariant();
                    return $"https://gravatar.com/avatar/{gravatarHash}?d=robohash";
                }
            );
        }
    }

    public class GlobalContext // lazy init since it requires to have loaded pages
    {
        public List<Page> Pages { get; set; } = [];
    }
}
