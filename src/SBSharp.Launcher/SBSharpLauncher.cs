using SBSharp.Core.IoC;

namespace SBSharp.Launcher;

public sealed class Launcher
{
    static int Main(string[] args)
    {
        using var container = new Container(args);
        return container.RunAsync().GetAwaiter().GetResult();
    }
}
