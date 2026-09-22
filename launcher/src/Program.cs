using System.Diagnostics;
using EtherBound.Launcher.Win32;

namespace EtherBound.Launcher;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (UsageException error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Options.Usage);
            return 1;
        }

        switch (options.Mode)
        {
            case Mode.Help:
                Console.WriteLine(Options.Usage);
                return 0;
            case Mode.CtrlC:
                return CtrlC.SendTo(options.ProcessId);
        }

        var ui = new ConsoleUi();
        var root = RepoRoot.Find(AppContext.BaseDirectory);
        if (root is null)
        {
            ui.Fail("no EtherBound checkout (server\\pyproject.toml and web\\package.json) at or above " +
                    AppContext.BaseDirectory);
            ui.PauseIfOwnWindow();
            return 1;
        }

        return options.Mode == Mode.Cleanup
            ? Cleanup(root, ui)
            : await Supervisor.RunAsync(root, AppInfo.ReadVersion(root), options, ui);
    }

    private static int Cleanup(string root, ConsoleUi ui)
    {
        if (SingleInstance.IsRunning(root))
        {
            ui.Fail("a launcher is running for this checkout; quit it with Q first");
            return 1;
        }

        var blockers = Leftovers.Sweep(root, ui, out var killed);
        foreach (var blocker in blockers)
        {
            ui.Warn(Leftovers.Describe(blocker) + "; not this checkout's, left alone");
        }

        if (killed > 0)
        {
            ui.Ok($"removed {killed} leftover process tree(s)");
        }
        else if (blockers.Count == 0)
        {
            ui.Ok("nothing to clean up");
        }

        return 0;
    }
}

internal static class AppInfo
{
    private const string VersionPrefix = "Overall project version: `";

    public static string ReadVersion(string root)
    {
        var versionLine = File.ReadLines(Path.Combine(root, "docs", "utils", "VERSION.md"))
            .FirstOrDefault(line => line.StartsWith(VersionPrefix, StringComparison.Ordinal) && line.EndsWith('`'));
        var version = versionLine is null ? null : versionLine[VersionPrefix.Length..^1];
        if (version is null || !version.StartsWith('v') || !Version.TryParse(version[1..], out _))
        {
            throw new InvalidDataException("docs/utils/VERSION.md has no valid overall project version");
        }

        return version;
    }
}

internal static class Wait
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);

    public static bool Until(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed >= timeout)
            {
                return false;
            }

            Thread.Sleep(Step);
        }

        return true;
    }

    public static async Task<bool> UntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed >= timeout)
            {
                return false;
            }

            await Task.Delay(Step);
        }

        return true;
    }
}
