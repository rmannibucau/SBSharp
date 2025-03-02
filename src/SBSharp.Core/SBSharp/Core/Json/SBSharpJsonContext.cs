namespace SBSharp.Core.Json;

using System.Text.Json.Serialization;

public record JsonIndex(IList<IndexDocument> Items) { }

public record IndexDocument(
    string Slug,
    string Title,
    string Description,
    IDictionary<string, string> Attributes
) { }

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(JsonIndex))]
[JsonSerializable(typeof(IndexDocument))]
public partial class SBSharpJsonContext : JsonSerializerContext { }
