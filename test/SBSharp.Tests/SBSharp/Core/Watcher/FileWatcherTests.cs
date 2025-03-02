using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SBSharp.Tests.Temp;

namespace SBSharp.Core.Watcher;

public class FileWatcherTests
{
    // really trivial test to check we get some event when something happens
    // note: it does not test debouncing/latency - should be added but leads to timing test issues
    [Fact]
    public void Watch()
    {
        var latch = new CountdownEvent(1);
        using var tmp = new TempFolder("FileWatcherTests-Watch");
        using var factory = new NullLoggerFactory();
        using var watcher = new FileWatcher(
            new Configuration.SBSharpConfiguration
            {
                Input = new Configuration.SBSharpConfiguration.InputConfiguration
                {
                    Location = tmp.Value,
                },
            },
            new Logger<FileWatcher>(factory)
        ).Watch(() =>
        {
            latch.Signal();
            return Task.FromResult(0);
        });
        File.WriteAllText(Path.Combine(tmp.Value, "test.adoc"), "");
        Assert.True(latch.Wait(30_000));
    }
}
