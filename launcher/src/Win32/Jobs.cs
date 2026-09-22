using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EtherBound.Launcher.Win32;

// A job object that Windows empties when its last handle closes. The launcher joins one so nothing
// it starts can outlive it, and gives each service a nested one so a stop reaches every descendant.
internal sealed unsafe class Job : IDisposable
{
    private readonly SafeJobHandle handle;

    private Job(SafeJobHandle handle) => this.handle = handle;

    public static Job CreateKillOnClose()
    {
        // The handle is not inheritable: a child holding a copy would keep the job alive after the
        // launcher dies. No breakaway flag either: the Python launchers in the server chain give
        // their own jobs silent breakaway, and a silent breakaway climbs every parent job that
        // permits one, so uvicorn's worker would escape.
        var handle = Native.CreateJobObjectW(0, 0);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateJobObject failed");
        }

        var limits = new JobObjectExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = Native.JobObjectLimitKillOnJobClose;
        if (!Native.SetInformationJobObject(
                handle, Native.JobObjectExtendedLimitInformationClass, &limits,
                (uint)sizeof(JobObjectExtendedLimitInformation)))
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, "SetInformationJobObject failed");
        }

        return new Job(handle);
    }

    public int ActiveProcesses
    {
        get
        {
            JobObjectBasicAccountingInformation info;
            return Native.QueryInformationJobObject(
                handle, Native.JobObjectBasicAccountingInformationClass, &info,
                (uint)sizeof(JobObjectBasicAccountingInformation), null)
                ? (int)info.ActiveProcesses
                : 0;
        }
    }

    public bool TryAssign(SafeProcessHandle process, out int error)
    {
        var assigned = Native.AssignProcessToJobObject(handle, process);
        error = assigned ? 0 : Marshal.GetLastPInvokeError();
        return assigned;
    }

    public void Terminate() => Native.TerminateJobObject(handle, 1);

    public void Dispose() => handle.Dispose();
}
