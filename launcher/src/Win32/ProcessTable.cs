using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace EtherBound.Launcher.Win32;

internal sealed record ProcessInfo(
    int Id, int ParentId, string Name, string? ImagePath, string? CommandLine, DateTime? StartTime);

internal enum KillResult
{
    Killed,
    Gone,
    Failed,
}

internal static unsafe class ProcessTable
{
    // Every process with its parent, image, command line and start time. Fields Windows will not
    // disclose (elevated or system processes) stay null, so no rule ever matches them.
    public static Dictionary<int, ProcessInfo> Snapshot()
    {
        var processes = new Dictionary<int, ProcessInfo>();
        using var snapshot = Native.CreateToolhelp32Snapshot(Native.SnapProcess, 0);
        if (snapshot.IsInvalid)
        {
            return processes;
        }

        var entry = new ProcessEntry32 { Size = (uint)sizeof(ProcessEntry32) };
        for (var more = Native.Process32FirstW(snapshot, &entry); more; more = Native.Process32NextW(snapshot, &entry))
        {
            var id = (int)entry.ProcessId;
            if (id != 0)
            {
                processes[id] = Describe(id, (int)entry.ParentProcessId, new string(entry.ExeFile));
            }
        }

        return processes;
    }

    public static KillResult KillTree(ProcessInfo target, out string? error)
    {
        error = null;
        Process process;
        try
        {
            process = Process.GetProcessById(target.Id);
        }
        catch (ArgumentException)
        {
            return KillResult.Gone;
        }

        using (process)
        {
            try
            {
                // The id may belong to a new process since the snapshot was taken.
                if (target.StartTime is { } expected && StartTime(process.SafeHandle) != expected)
                {
                    return KillResult.Gone;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(5));
                return KillResult.Killed;
            }
            catch (InvalidOperationException)
            {
                return KillResult.Gone;
            }
            catch (Exception failure) when (failure is Win32Exception or NotSupportedException or AggregateException)
            {
                error = failure.Message;
                return KillResult.Failed;
            }
        }
    }

    public static DateTime? StartTime(SafeProcessHandle process)
    {
        long creation, exit, kernel, user;
        return Native.GetProcessTimes(process, &creation, &exit, &kernel, &user)
            ? DateTime.FromFileTimeUtc(creation)
            : null;
    }

    private static ProcessInfo Describe(int id, int parentId, string name)
    {
        using var process = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, (uint)id);
        return process.IsInvalid
            ? new ProcessInfo(id, parentId, name, null, null, null)
            : new ProcessInfo(id, parentId, name, ImagePath(process), CommandLine(process), StartTime(process));
    }

    private static string? ImagePath(SafeProcessHandle process)
    {
        const int capacity = 1024;
        var buffer = stackalloc char[capacity];
        uint size = capacity;
        return Native.QueryFullProcessImageNameW(process, 0, buffer, &size) ? new string(buffer, 0, (int)size) : null;
    }

    private static string? CommandLine(SafeProcessHandle process)
    {
        uint length = 2048;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = new byte[length];
            fixed (byte* data = buffer)
            {
                uint needed = 0;
                var status = Native.NtQueryInformationProcess(
                    process, Native.ProcessCommandLineInformation, data, length, &needed);
                if (status == Native.StatusInfoLengthMismatch && needed > length)
                {
                    length = needed;
                    continue;
                }

                if (status < 0)
                {
                    return null;
                }

                // A UNICODE_STRING whose buffer points into our own buffer, right after it.
                var text = (UnicodeString*)data;
                return text->Buffer == 0 ? "" : new string((char*)text->Buffer, 0, text->Length / sizeof(char));
            }
        }

        return null;
    }
}
