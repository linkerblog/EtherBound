using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EtherBound.Launcher.Win32;

// A service process started the way .NET's Process cannot: suspended until it sits in its job, so
// nothing it spawns can escape the job, and inheriting only its own three pipe ends. It gets a
// windowless console of its own, so a Ctrl+C meant for it reaches nothing else.
internal sealed unsafe class ChildProcess : IDisposable
{
    private readonly SafeFileHandle input;
    private readonly SafeFileHandle output;
    private readonly SafeFileHandle error;
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int openStreams = 2;

    private ChildProcess(Process process, SafeFileHandle input, SafeFileHandle output, SafeFileHandle error)
    {
        Process = process;
        this.input = input;
        this.output = output;
        this.error = error;
    }

    public Process Process { get; }

    // Completes once stdout and stderr have both reached end of file.
    public Task Drained => drained.Task;

    public static ChildProcess Start(ServiceSpec spec, Job? job, out int jobError)
    {
        jobError = 0;
        SafeFileHandle? inputRead = null, inputWrite = null;
        SafeFileHandle? outputRead = null, outputWrite = null;
        SafeFileHandle? errorRead = null, errorWrite = null;
        try
        {
            (inputRead, inputWrite) = CreatePipe(childReads: true);
            (outputRead, outputWrite) = CreatePipe(childReads: false);
            (errorRead, errorWrite) = CreatePipe(childReads: false);

            var created = Launch(spec, inputRead, outputWrite, errorWrite);
            using var processHandle = new SafeProcessHandle(created.Process, ownsHandle: true);
            Process process;
            try
            {
                if (job is not null)
                {
                    job.TryAssign(processHandle, out jobError);
                }

                process = Process.GetProcessById((int)created.ProcessId);
                // Opened while the process cannot have exited yet: its exit code is only readable
                // through a handle opened before the exit.
                _ = process.SafeHandle;
                if (Native.ResumeThread(created.Thread) == uint.MaxValue)
                {
                    throw Failure("ResumeThread");
                }
            }
            catch
            {
                Native.TerminateProcess(created.Process, 1);
                throw;
            }
            finally
            {
                Native.CloseHandle(created.Thread);
            }

            var child = new ChildProcess(process, inputWrite, outputRead, errorRead);
            inputWrite = outputRead = errorRead = null;
            return child;
        }
        finally
        {
            // The child's ends: it has its own copies now, and ours would keep the pipes from ever
            // reaching end of file.
            inputRead?.Dispose();
            outputWrite?.Dispose();
            errorWrite?.Dispose();
            inputWrite?.Dispose();
            outputRead?.Dispose();
            errorRead?.Dispose();
        }
    }

    // Reads stdout and stderr line by line, each on a thread of its own, until end of file.
    public void Pump(Action<string> onLine)
    {
        Read(output, onLine);
        Read(error, onLine);
    }

    public void Dispose()
    {
        // stdin closes only now: Vite exits as soon as its stdin reaches end of file.
        input.Dispose();
        Process.Dispose();
    }

    internal static string EnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            variables[(string)variable.Key] = (string?)variable.Value ?? "";
        }

        foreach (var (name, value) in overrides)
        {
            variables[name] = value;
        }

        var block = new StringBuilder();
        foreach (var (name, value) in variables)
        {
            block.Append(name).Append('=').Append(value).Append('\0');
        }

        return block.Append('\0').ToString();
    }

    private static ProcessInformation Launch(
        ServiceSpec spec, SafeFileHandle stdin, SafeFileHandle stdout, SafeFileHandle stderr)
    {
        // Only these three handles are inherited. Without the list the child would inherit every
        // inheritable handle of the launcher, the other service's pipe ends included.
        var inherited = stackalloc nint[3];
        inherited[0] = stdin.DangerousGetHandle();
        inherited[1] = stdout.DangerousGetHandle();
        inherited[2] = stderr.DangerousGetHandle();

        nuint size = 0;
        Native.InitializeProcThreadAttributeList(null, 1, 0, &size);
        var attributes = NativeMemory.Alloc(size);
        try
        {
            if (!Native.InitializeProcThreadAttributeList(attributes, 1, 0, &size))
            {
                throw Failure("InitializeProcThreadAttributeList");
            }

            try
            {
                if (!Native.UpdateProcThreadAttribute(
                        attributes, 0, Native.ProcThreadAttributeHandleList, inherited,
                        (nuint)(3 * sizeof(nint)), null, null))
                {
                    throw Failure("UpdateProcThreadAttribute");
                }

                var startup = new StartupInfoEx { AttributeList = attributes };
                startup.StartupInfo.Size = (uint)sizeof(StartupInfoEx);
                startup.StartupInfo.Flags = Native.StartfUseStdHandles;
                startup.StartupInfo.StdInput = inherited[0];
                startup.StartupInfo.StdOutput = inherited[1];
                startup.StartupInfo.StdError = inherited[2];

                // CreateProcessW may write into the command line, so it gets a buffer of its own.
                var commandLine = (CommandLine.Build(spec.Executable, spec.Arguments) + '\0').ToCharArray();
                var environment = EnvironmentBlock(spec.Environment);
                ProcessInformation created;
                fixed (char* application = spec.Executable)
                fixed (char* line = commandLine)
                fixed (char* block = environment)
                fixed (char* directory = spec.WorkingDirectory)
                {
                    const uint flags = Native.CreateNoWindow | Native.CreateSuspended |
                                       Native.CreateUnicodeEnvironment | Native.ExtendedStartupInfoPresent;
                    if (!Native.CreateProcessW(
                            application, line, null, null, true, flags, block, directory, &startup, &created))
                    {
                        throw Failure($"starting {spec.Executable}");
                    }
                }

                return created;
            }
            finally
            {
                Native.DeleteProcThreadAttributeList(attributes);
            }
        }
        finally
        {
            NativeMemory.Free(attributes);
        }
    }

    private static (SafeFileHandle Read, SafeFileHandle Write) CreatePipe(bool childReads)
    {
        var security = new SecurityAttributes { Length = (uint)sizeof(SecurityAttributes), InheritHandle = 1 };
        if (!Native.CreatePipe(out var read, out var write, &security, 0))
        {
            throw Failure("CreatePipe");
        }

        // Only the child's end may be inherited, by anything the launcher starts.
        if (!Native.SetHandleInformation(childReads ? write : read, Native.HandleFlagInherit, 0))
        {
            var failure = Failure("SetHandleInformation");
            read.Dispose();
            write.Dispose();
            throw failure;
        }

        return (read, write);
    }

    private void Read(SafeFileHandle handle, Action<string> onLine)
    {
        var thread = new Thread(() =>
        {
            try
            {
                using var reader = new StreamReader(
                    new FileStream(handle, FileAccess.Read, 4096, isAsync: false), Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    onLine(line);
                }
            }
            catch (IOException)
            {
                // The pipe broke: the service is gone.
            }
            finally
            {
                if (Interlocked.Decrement(ref openStreams) == 0)
                {
                    drained.TrySetResult();
                }
            }
        })
        {
            IsBackground = true,
            Name = "service output",
        };
        thread.Start();
    }

    private static Win32Exception Failure(string what) =>
        new(Marshal.GetLastPInvokeError(), $"{what} failed");
}

internal static class CommandLine
{
    // Quotes each argument the way CommandLineToArgvW and the C runtime split it back.
    public static string Build(string executable, IEnumerable<string> arguments)
    {
        var line = new StringBuilder();
        Append(line, executable);
        foreach (var argument in arguments)
        {
            line.Append(' ');
            Append(line, argument);
        }

        return line.ToString();
    }

    private static void Append(StringBuilder line, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            line.Append(argument);
            return;
        }

        line.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes only escape when a quote follows them.
            line.Append('\\', character == '"' ? (backslashes * 2) + 1 : backslashes);
            line.Append(character);
            backslashes = 0;
        }

        line.Append('\\', backslashes * 2);
        line.Append('"');
    }
}
