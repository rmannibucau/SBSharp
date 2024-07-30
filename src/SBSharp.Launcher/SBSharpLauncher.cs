using System.Diagnostics;
using SBSharp.Core.IoC;

namespace SBSharp.Launcher;

public sealed class Launcher
{
    static int Main(string[] args)
    {
        WaitDebuggerIfNeeded();

        using var container = new Container(args);
        return container.RunAsync().GetAwaiter().GetResult();
    }

    private static void WaitDebuggerIfNeeded()
    {
        if (Environment.GetEnvironmentVariable("SBSHARP_DEBUG") != "true")
        {
            return;
        }
        while (!Debugger.IsAttached)
        {
            Thread.Sleep(100);
        }
        Debugger.Break();
    }
}
