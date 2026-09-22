using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EtherBound.Launcher.Win32;

internal static unsafe partial class Native
{
    public const uint JobObjectLimitKillOnJobClose = 0x2000;
    public const int JobObjectBasicAccountingInformationClass = 1;
    public const int JobObjectExtendedLimitInformationClass = 9;

    public const uint CtrlCEvent = 0;

    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const int ProcessCommandLineInformation = 60;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    public const uint SnapProcess = 0x2;

    public const int AddressFamilyInet = 2;
    public const int AddressFamilyInet6 = 23;
    public const int TcpTableOwnerPidListener = 3;
    public const uint ErrorInsufficientBuffer = 122;

    public const uint CreateSuspended = 0x00000004;
    public const uint CreateUnicodeEnvironment = 0x00000400;
    public const uint ExtendedStartupInfoPresent = 0x00080000;
    public const uint CreateNoWindow = 0x08000000;
    public const uint StartfUseStdHandles = 0x00000100;
    public const nuint ProcThreadAttributeHandleList = 0x00020002;
    public const uint HandleFlagInherit = 0x00000001;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreatePipe(
        out SafeFileHandle readPipe, out SafeFileHandle writePipe, SecurityAttributes* attributes, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InitializeProcThreadAttributeList(void* list, int count, uint flags, nuint* size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateProcThreadAttribute(
        void* list, uint flags, nuint attribute, void* value, nuint size, void* previousValue, nuint* returnSize);

    [LibraryImport("kernel32.dll")]
    public static partial void DeleteProcThreadAttributeList(void* list);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcessW(
        char* applicationName, char* commandLine, void* processAttributes, void* threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, char* environment,
        char* currentDirectory, StartupInfoEx* startupInfo, ProcessInformation* processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial SafeJobHandle CreateJobObjectW(nint jobAttributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(SafeJobHandle job, int infoClass, void* info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryInformationJobObject(
        SafeJobHandle job, int infoClass, void* info, uint length, uint* returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateJobObject(SafeJobHandle job, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeConsole();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetConsoleCtrlHandler(nint handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetConsoleProcessList(uint* processList, uint count);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial SafeProcessHandle OpenProcess(
        uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, char* buffer, uint* size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(
        SafeProcessHandle process, long* creation, long* exit, long* kernel, long* user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32FirstW(SafeFileHandle snapshot, ProcessEntry32* entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32NextW(SafeFileHandle snapshot, ProcessEntry32* entry);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(
        SafeProcessHandle process, int infoClass, void* info, uint length, uint* returnLength);

    [LibraryImport("iphlpapi.dll")]
    public static partial uint GetExtendedTcpTable(
        void* table, uint* size, [MarshalAs(UnmanagedType.Bool)] bool order, int addressFamily, int tableClass,
        uint reserved);
}

internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeJobHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => Native.CloseHandle(handle);
}

// Filled in by Windows; most fields are never read, a few are never written from managed code.
#pragma warning disable CS0649

[StructLayout(LayoutKind.Sequential)]
internal struct JobObjectBasicLimitInformation
{
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public uint LimitFlags;
    public nuint MinimumWorkingSetSize;
    public nuint MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public nuint Affinity;
    public uint PriorityClass;
    public uint SchedulingClass;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IoCounters
{
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JobObjectExtendedLimitInformation
{
    public JobObjectBasicLimitInformation BasicLimitInformation;
    public IoCounters IoInfo;
    public nuint ProcessMemoryLimit;
    public nuint JobMemoryLimit;
    public nuint PeakProcessMemoryUsed;
    public nuint PeakJobMemoryUsed;
}

[StructLayout(LayoutKind.Sequential)]
internal struct JobObjectBasicAccountingInformation
{
    public long TotalUserTime;
    public long TotalKernelTime;
    public long ThisPeriodTotalUserTime;
    public long ThisPeriodTotalKernelTime;
    public uint TotalPageFaultCount;
    public uint TotalProcesses;
    public uint ActiveProcesses;
    public uint TotalTerminatedProcesses;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ProcessEntry32
{
    public uint Size;
    public uint Usage;
    public uint ProcessId;
    public nuint DefaultHeapId;
    public uint ModuleId;
    public uint Threads;
    public uint ParentProcessId;
    public int PriorityClassBase;
    public uint Flags;
    public fixed char ExeFile[260];
}

[StructLayout(LayoutKind.Sequential)]
internal struct UnicodeString
{
    public ushort Length;
    public ushort MaximumLength;
    public nint Buffer;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SecurityAttributes
{
    public uint Length;
    public nint SecurityDescriptor;
    public int InheritHandle;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfo
{
    public uint Size;
    public nint Reserved;
    public nint Desktop;
    public nint Title;
    public uint X;
    public uint Y;
    public uint XSize;
    public uint YSize;
    public uint XCountChars;
    public uint YCountChars;
    public uint FillAttribute;
    public uint Flags;
    public ushort ShowWindow;
    public ushort Reserved2Size;
    public nint Reserved2;
    public nint StdInput;
    public nint StdOutput;
    public nint StdError;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct StartupInfoEx
{
    public StartupInfo StartupInfo;
    public void* AttributeList;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation
{
    public nint Process;
    public nint Thread;
    public uint ProcessId;
    public uint ThreadId;
}

#pragma warning restore CS0649
