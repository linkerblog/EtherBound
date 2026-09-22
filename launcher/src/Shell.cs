using System.ComponentModel;
using System.Diagnostics;

namespace EtherBound.Launcher;

internal static class Shell
{
    // Explorer hands the request to the running shell and exits, so the browser or folder window is
    // the shell's child rather than a member of the launcher's job, and outlives the launcher.
    public static void Open(string target, ConsoleUi ui)
    {
        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            var info = new ProcessStartInfo(explorer) { UseShellExecute = false };
            info.ArgumentList.Add(target);
            using var process = Process.Start(info);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            ui.Warn($"could not open {target}: {error.Message}");
        }
    }
}
