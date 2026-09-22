using System.Diagnostics;
using System.Globalization;

namespace EtherBound.Launcher.Win32;

internal static class CtrlC
{
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(3);

    // Runs in a throwaway copy of the launcher (`--ctrl-c <pid>`). The launcher cannot do this
    // itself: attaching to another console means freeing its own, which destroys its window.
    public static int SendTo(int processId)
    {
        Native.FreeConsole();
        if (!Native.AttachConsole((uint)processId))
        {
            return 2;
        }

        // We sit on the service's console now; the event must not stop this helper first.
        Native.SetConsoleCtrlHandler(0, true);
        return Native.GenerateConsoleCtrlEvent(Native.CtrlCEvent, 0) ? 0 : 3;
    }

    // Sends Ctrl+C to every process on the console of processId, through the helper.
    public static bool Request(int processId)
    {
        if (Environment.ProcessPath is not { } self)
        {
            return false;
        }

        var info = new ProcessStartInfo(self) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--ctrl-c");
        info.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
        try
        {
            using var helper = Process.Start(info);
            return helper is not null && helper.WaitForExit(HelperTimeout) && helper.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
