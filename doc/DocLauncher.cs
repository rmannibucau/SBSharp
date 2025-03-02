using System.ComponentModel;
using System.Reflection;
using System.Text;
using SBSharp.Core.Configuration;

namespace SBSharp.doc;

public sealed class DocLauncher
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("> Generating the doc");
        return await Render(args);
    }

    private static async Task<int> Render(string[] args)
    {
        // assume running from root (or doc) dir: [DOC_CMD=serve] dotnet run --project doc
        var baseDir = Path.GetFullPath(
            Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.FullName
        );
        var partialsDir = Directory.CreateDirectory($"{baseDir}/_content/_partials").FullName;

        GenerateConfiguration(partialsDir);

        var module = Path.GetFullPath($"{AppDomain.CurrentDomain.BaseDirectory}/../../..");
        using var container = new Core.IoC.Container(
            [
                Environment.GetEnvironmentVariable("DOC_CMD") ?? "build",
                .. args,
                $"--sbsharp:Input:Location={module}/_content",
                $"--sbsharp:Input:View={module}/_views",
                $"--sbsharp:Output:Location={module}/_site",
            ]
        );
        return await container.RunAsync();
    }

    private static void GenerateConfiguration(string partialsDir)
    {
        var output = $"{partialsDir}/configuration.partial.adoc";
        var adoc = GenerateConfigurationFor(
            typeof(SBSharpConfiguration),
            [],
            "SBSHARP__",
            "--sbsharp:",
            new SBSharpConfiguration()
        );
        File.WriteAllText(output, adoc);
    }

    private static string GenerateConfigurationFor(
        Type type,
        HashSet<Type> visited,
        string envVarPrefix,
        string cliPrefix,
        object instance
    )
    {
        var desc = type.GetCustomAttribute<DescriptionAttribute>()!.Description;

        var nestedObjects = new List<(Type Type, object Instance, string Name, bool Array)>();
        var builder = new StringBuilder("[#")
            .Append(type.Name)
            .Append("]\n== ")
            .Append(type.Name)
            .Append("\n\n")
            .Append(desc.Trim())
            .Append("\n\n");

        foreach (
            var prop in type.GetProperties()
                .Where(it => it.DeclaringType == type)
                .OrderBy(it => it.Name)
        )
        {
            var description = prop.GetCustomAttribute<DescriptionAttribute>()!.Description.Trim();
            var defaultValue = prop.GetValue(instance);

            if (
                prop.PropertyType == typeof(int)
                || prop.PropertyType == typeof(bool)
                || prop.PropertyType == typeof(string)
            )
            {
                builder.Append(prop.Name).Append("::\n").Append(description).Append("\n+\n");
                if (defaultValue is not null)
                {
                    builder.Append("*Default value:* `").Append(defaultValue).Append("`.\n+\n");
                }

                builder
                    .Append("*Environment variable:* `")
                    .Append(envVarPrefix)
                    .Append(prop.Name.ToUpperInvariant())
                    .Append("`.\n+\n")
                    .Append("*Command line:* `")
                    .Append(cliPrefix)
                    .Append(prop.Name)
                    .Append("=<value>`.\n");
            }
            else if (prop.PropertyType == typeof(string[]))
            {
                builder.Append(prop.Name).Append("::\n").Append(description).Append("\n+\n");
                if (defaultValue is not null)
                {
                    builder
                        .Append("*Default value:* `")
                        .Append(string.Join(", ", (string[])defaultValue))
                        .Append("`.\n+\n");
                }

                builder
                    .Append("*Environment variable:* `")
                    .Append(envVarPrefix)
                    .Append(prop.Name.ToUpperInvariant())
                    .Append("__<index>")
                    .Append("`.\n+\n")
                    .Append("*Command line:* `")
                    .Append(cliPrefix)
                    .Append(prop.Name)
                    .Append(":<index>")
                    .Append("=<value>`.\n");
            }
            else if (prop.PropertyType == typeof(IDictionary<string, string>))
            {
                builder.Append(prop.Name).Append("::\n").Append(description).Append("\n+\n");
                if (defaultValue is not null)
                {
                    builder
                        .Append("*Default value:* `")
                        .Append(
                            string.Join(
                                ", ",
                                ((IDictionary<string, string>)defaultValue).Select(it =>
                                    $"{it.Key}={it.Value}"
                                )
                            )
                        )
                        .Append("`.\n+\n");
                }

                builder
                    .Append("*Environment variable:* `")
                    .Append(envVarPrefix)
                    .Append(prop.Name.ToUpperInvariant())
                    .Append("__<key>")
                    .Append("`.\n+\n")
                    .Append("*Command line:* `")
                    .Append(cliPrefix)
                    .Append(prop.Name)
                    .Append(":<key>")
                    .Append("=<value>`.\n");
            }
            else if (prop.PropertyType.IsArray)
            {
                builder.Append(prop.Name).Append(" (array)::\n").Append(description);
                if (!description.EndsWith('.'))
                {
                    builder.Append('.');
                }

                var itemType = prop.PropertyType.GetElementType()!;
                builder.Append(" See <<").Append(itemType.Name).Append(">> section.\n");
                if (visited.Add(itemType))
                {
                    var def =
                        defaultValue is not null && ((object[])defaultValue).Length > 0
                            ? ((object[])defaultValue)[0]
                            : Activator.CreateInstance(itemType)!;
                    nestedObjects.Add((itemType, def, prop.Name, true));
                }
            }
            else
            {
                builder.Append(prop.Name).Append("::\n").Append(description);
                if (!description.EndsWith('.'))
                {
                    builder.Append('.');
                }
                builder.Append(" See <<").Append(prop.PropertyType.Name).Append(">> section.\n");
                if (visited.Add(prop.PropertyType))
                {
                    nestedObjects.Add(
                        (
                            prop.PropertyType,
                            defaultValue ?? Activator.CreateInstance(prop.PropertyType)!,
                            prop.Name,
                            false
                        )
                    );
                }
            }
        }
        foreach (var it in nestedObjects)
        {
            builder
                .Append("\n\n")
                .Append(
                    GenerateConfigurationFor(
                        it.Type,
                        visited,
                        $"{envVarPrefix}{it.Name.ToUpperInvariant()}__{(it.Array ? "$INDEX__" : "")}",
                        $"{cliPrefix}{it.Name}:{(it.Array ? "$index:" : "")}",
                        it.Instance
                    )
                );
        }

        return builder.Append('\n').ToString();
    }
}
