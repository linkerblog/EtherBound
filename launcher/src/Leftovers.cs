using System.Net.Sockets;
using EtherBound.Launcher.Win32;

namespace EtherBound.Launcher;

internal sealed record Leftover(ProcessInfo Process, string Reason);

internal sealed record Blocker(Listener Listener, ProcessInfo? Owner);

internal sealed record Findings(IReadOnlyList<Leftover> Leftovers, IReadOnlyList<Blocker> Foreign);

// What counts as a leftover of this checkout. Kept pure so it can be tested on made-up snapshots.
internal static class LeftoverRule
{
    public static Findings Evaluate(
        string root, IReadOnlyDictionary<int, ProcessInfo> processes, IEnumerable<Listener> listeners, int selfId)
    {
        var checkout = Normalize(Path.TrimEndingDirectorySeparator(root));
        string[] serviceMarkers = [checkout + @"\server\", checkout + @"\web\"];
        string[] oldLauncherMarkers =
        [
            checkout + @"\scripts\run-server.bat",
            checkout + @"\scripts\run-web.bat",
            checkout + @"\scripts\launcher.ps1",
        ];

        var leftovers = new Dictionary<int, Leftover>();
        var foreign = new List<Blocker>();
        var watched = listeners
            .Where(listener => Services.Ports.Contains(listener.Port))
            .DistinctBy(listener => (listener.Port, listener.ProcessId));
        foreach (var listener in watched)
        {
            if (!processes.TryGetValue(listener.ProcessId, out var owner))
            {
                foreign.Add(new Blocker(listener, null));
            }
            else if (owner.Id == selfId)
            {
                continue;
            }
            else if (Mentions(owner, serviceMarkers) ||
                     (LivingParent(owner, processes) is { } parent && Mentions(parent, serviceMarkers)))
            {
                leftovers.TryAdd(owner.Id, new Leftover(owner, $"it listens on port {listener.Port}"));
            }
            else
            {
                foreign.Add(new Blocker(listener, owner));
            }
        }

        // The old launcher's wrappers hold no port but keep the log files open.
        foreach (var process in processes.Values)
        {
            if (process.Id != selfId && process.CommandLine is { } commandLine && Contains(commandLine, oldLauncherMarkers))
            {
                leftovers.TryAdd(process.Id, new Leftover(process, "it belongs to the old batch launcher"));
            }
        }

        return new Findings([.. leftovers.Values], foreign);
    }

    // A "parent" that started after its child is a new process that reused a dead parent's id.
    internal static ProcessInfo? LivingParent(ProcessInfo child, IReadOnlyDictionary<int, ProcessInfo> processes) =>
        processes.TryGetValue(child.ParentId, out var parent) &&
        parent.Id != child.Id &&
        parent.StartTime is { } parentStart &&
        child.StartTime is { } childStart &&
        parentStart <= childStart
            ? parent
            : null;

    private static bool Mentions(ProcessInfo process, string[] markers) =>
        (process.ImagePath is { } image && Contains(image, markers)) ||
        (process.CommandLine is { } commandLine && Contains(commandLine, markers));

    private static bool Contains(string text, string[] markers)
    {
        var normalized = Normalize(text);
        return markers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path) => path.Replace('/', '\\');
}

internal static class Leftovers
{
    private static readonly TimeSpan Release = TimeSpan.FromSeconds(5);

    // Kills this checkout's leftovers, then returns whatever still holds a service port.
    public static IReadOnlyList<Blocker> Sweep(string root, ConsoleUi ui, out int killed)
    {
        killed = 0;
        var findings = LeftoverRule.Evaluate(
            root, ProcessTable.Snapshot(), Ports.Listeners(Services.Ports), Environment.ProcessId);
        foreach (var leftover in findings.Leftovers)
        {
            switch (ProcessTable.KillTree(leftover.Process, out var error))
            {
                case KillResult.Killed:
                    killed++;
                    ui.Warn($"killed leftover {Name(leftover.Process)}: {leftover.Reason}");
                    break;
                case KillResult.Failed:
                    ui.Fail($"could not kill leftover {Name(leftover.Process)}: {error}");
                    break;
            }
        }

        return Remaining([.. findings.Leftovers.Select(leftover => leftover.Process.Id)]);
    }

    // Before a run the ports must be free: anything that is not a leftover is the user's call.
    public static bool ClearPorts(string root, ConsoleUi ui)
    {
        var blockers = Sweep(root, ui, out _);
        if (blockers.Count > 0)
        {
            foreach (var blocker in blockers)
            {
                ui.Fail(Describe(blocker));
            }

            if (!ui.Interactive || !ui.AskKillOrQuit())
            {
                return false;
            }

            foreach (var blocker in blockers)
            {
                if (blocker.Owner is { } owner && ProcessTable.KillTree(owner, out var error) == KillResult.Failed)
                {
                    ui.Fail($"could not kill {Name(owner)}: {error}");
                }
            }

            var left = Remaining([.. blockers.Select(blocker => blocker.Listener.ProcessId)]);
            foreach (var blocker in left)
            {
                ui.Fail(Describe(blocker));
            }

            if (left.Count > 0)
            {
                return false;
            }
        }

        foreach (var port in Services.Ports)
        {
            if (Ports.TryBind(port) == SocketError.AccessDenied)
            {
                ui.Fail($"port {port} is reserved by Windows (an excluded port range); " +
                        "see  netsh interface ipv4 show excludedportrange protocol=tcp");
                return false;
            }
        }

        return true;
    }

    public static string Describe(Blocker blocker) => blocker.Owner is { } owner
        ? $"port {blocker.Listener.Port} is used by {Name(owner)}" +
          (owner.CommandLine is { } commandLine ? ": " + Shorten(commandLine) : "")
        : $"port {blocker.Listener.Port} is held by PID {blocker.Listener.ProcessId}, which no longer exists " +
          "(a socket it handed on is still open)";

    // Gives killed processes a moment to release their ports, then describes whoever still holds one.
    private static IReadOnlyList<Blocker> Remaining(IReadOnlyCollection<int> killedIds)
    {
        if (killedIds.Count > 0)
        {
            Wait.Until(
                () => !Ports.Listeners(Services.Ports).Any(listener => killedIds.Contains(listener.ProcessId)),
                Release);
        }

        var listeners = Ports.Listeners(Services.Ports);
        if (listeners.Count == 0)
        {
            return [];
        }

        var processes = ProcessTable.Snapshot();
        return
        [
            .. listeners
                .DistinctBy(listener => (listener.Port, listener.ProcessId))
                .Select(listener => new Blocker(listener, processes.GetValueOrDefault(listener.ProcessId))),
        ];
    }

    private static string Name(ProcessInfo process) => $"{process.Name} (PID {process.Id})";

    private static string Shorten(string text) => text.Length <= 160 ? text : text[..157] + "...";
}
