using SBSharp.Tests.Temp;

namespace SBSharp.Core.Scanner;

public class SourceScannerTests
{
    [Fact]
    public void Scan()
    {
        using var tmp = new TempFolder("SourceScannerTests-Scan");
        File.WriteAllText(Path.Combine(tmp.Value, "file1.adoc"), "File 1");
        File.WriteAllText(Path.Combine(tmp.Value, "file11.partial.adoc"), "Ignored");

        Directory.CreateDirectory(Path.Combine(tmp.Value, "sub"));
        File.WriteAllText(Path.Combine(tmp.Value, "sub\\file2.adoc"), "File 2");
        File.WriteAllText(Path.Combine(tmp.Value, "sub\\file22.partial.adoc"), "Ignored");
        File.WriteAllText(Path.Combine(tmp.Value, "sub\\file22.other"), "Ignored");

        var files = new SourceScanner(
            new Configuration.SBSharpConfiguration
            {
                Input = new Configuration.SBSharpConfiguration.InputConfiguration
                {
                    Location = tmp.Value,
                },
            }
        )
            .ScanSources()
            .ToList();
        Assert.Equal(
            ["file1.adoc: File 1", "sub\\file2.adoc: File 2"],
            // ReSharper disable once AccessToDisposedClosure
            files.Select(it => $"{it}: {File.ReadAllText(Path.Combine(tmp.Value, it))}").Order()
        );
    }
}
