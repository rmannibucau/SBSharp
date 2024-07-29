using SBSharp.Core.IoC;

namespace SBSharp.Launcher;

public sealed class Launcher
{
    static async Task<int> Main(string[] args)
    {
        using var container = new Container(args);
        return await container.RunAsync();
    }
}
