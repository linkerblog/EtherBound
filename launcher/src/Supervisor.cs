using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using EtherBound.Launcher.Win32;

namespace EtherBound.Launcher;

internal static class Supervisor
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    // Windows ends a closing console's processes about 5 s after the close event.
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(3);

    // Never disposed, and held here so the GC cannot finalize it: closing the handle kills every
    // process in the job, the launcher included, so it must close only when the process exits.
    private static Job? launcherJob;

    public static async Task<int> RunAsync(string root, Options options, ConsoleUi ui)
    {
        using var instance = SingleInstance.TryAcquire(root);
        if (instance is null)
        {
            ui.Fail("a launcher is already running for this checkout");
            ui.PauseIfOwnWindow();
            return 1;
        }

        // Joined before anything starts: whatever the launcher starts from here on dies with it,
        // however it ends (Q, Ctrl+C, closed window, crash, Task Manager).
        launcherJob = Job.CreateKillOnClose();
        using var self = Process.GetCurrentProcess();
        var contained = launcherJob.TryAssign(self.SafeHandle, out var jobError);

        var logs = Path.Combine(root, "logs");
        var now = DateTime.Now;
        using var launcherLog = LogFile.Open(logs, "launcher", now, out var launcherLogWarning);
        ui.AttachLog(launcherLog);
        ui.Header();
        if (launcherLogWarning is not null)
        {
            ui.Warn(launcherLogWarning);
        }

        if (!contained)
        {
            ui.Warn($"could not join a job object (error {jobError}); only an orderly quit stops the services");
        }

        if (!FindTools(root, ui, out var uv, out var node) || !Leftovers.ClearPorts(root, ui))
        {
            ui.PauseIfOwnWindow();
            return 1;
        }

        using var http = HealthClient.Create();
        using var server = new Service(Services.Server(root, uv), OpenLog(logs, "server", now, ui), ui, http);
        using var web = new Service(Services.Web(root, node), OpenLog(logs, "web", now, ui), ui, http);

        var quit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopped = new ManualResetEventSlim();
        var interrupts = 0;
        using var onInterrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            // A second Ctrl+C does not wait for the orderly stop; the launcher job kills the rest.
            if (Interlocked.Increment(ref interrupts) > 1)
            {
                Environment.Exit(130);
            }

            quit.TrySetResult();
        });

        // SIGHUP is the window being closed, SIGTERM a logoff or shutdown. Windows ends the process
        // soon after the handler returns, so the orderly stop only gets a bounded head start.
        void OnClose(PosixSignalContext context)
        {
            quit.TrySetResult();
            stopped.Wait(CloseGrace);
        }

        using var onClose = PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnClose);
        using var onTerminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnClose);

        var openPending = options.Open ? 1 : 0;
        void OnStateChanged()
        {
            ui.SetTitle($"EtherBound {AppInfo.Version} · server {server.StateText} · web {web.StateText}");
            if (server.State == ServiceState.Ready && web.State == ServiceState.Ready &&
                Interlocked.Exchange(ref openPending, 0) == 1)
            {
                Shell.Open(web.Spec.Url, ui);
            }
        }

        server.StateChanged += OnStateChanged;
        web.StateChanged += OnStateChanged;

        ui.Step($"server starting (hot reload {(options.Reload ? "on" : "off")})");
        server.Start();
        ui.Step("web starting");
        web.Start();

        // Created after the first start, so an early save cannot start the server twice. The
        // callback blocks a timer thread until the restarted server has settled, on purpose: that
        // is how the watcher folds the saves made meanwhile into one more restart.
        using var sources = options.Reload
            ? new SourceWatcher(server.Spec.WorkingDirectory, files =>
            {
                if (quit.Task.IsCompleted)
                {
                    return;
                }

                try
                {
                    server.RestartAsync(Grace, Changed(files)).GetAwaiter().GetResult();
                    server.WaitUntilSettledAsync().GetAwaiter().GetResult();
                }
                catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
                {
                    ui.Fail($"hot reload failed: {error.Message}");
                }
            })
            : null;

        if (ui.Interactive)
        {
            ListenForKeys(ui, quit, server, web, logs);
        }

        await quit.Task;
        ui.Step("stopping services");
        await Task.WhenAll(server.StopAsync(Grace), web.StopAsync(Grace));
        ui.Ok("services stopped");
        stopped.Set();
        return 0;
    }

    private static bool FindTools(
        string root, ConsoleUi ui, [NotNullWhen(true)] out string? uv, [NotNullWhen(true)] out string? node)
    {
        uv = PathSearch.Find("uv.exe");
        node = PathSearch.Find("node.exe");
        if (uv is null)
        {
            ui.Fail("uv.exe is not on PATH: install uv (https://docs.astral.sh/uv/) and open a new terminal");
        }

        if (node is null)
        {
            ui.Fail("node.exe is not on PATH: install Node.js and open a new terminal");
        }

        var webInstalled = File.Exists(Services.ViteEntry(root));
        if (!webInstalled)
        {
            ui.Fail("the web dependencies are missing; run:  cd web && npm install");
        }

        return uv is not null && node is not null && webInstalled;
    }

    private static LogFile OpenLog(string logs, string name, DateTime now, ConsoleUi ui)
    {
        var log = LogFile.Open(logs, name, now, out var warning);
        if (warning is not null)
        {
            ui.Warn(warning);
        }

        return log;
    }

    private static void ListenForKeys(ConsoleUi ui, TaskCompletionSource quit, Service server, Service web, string logs)
    {
        var thread = new Thread(() =>
        {
            while (!quit.Task.IsCompleted)
            {
                ConsoleKey key;
                try
                {
                    key = Console.ReadKey(intercept: true).Key;
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                if (quit.Task.IsCompleted)
                {
                    return;
                }

                switch (key)
                {
                    case ConsoleKey.Q:
                        quit.TrySetResult();
                        return;
                    case ConsoleKey.R:
                        Observe(server.RestartAsync(Grace), ui);
                        break;
                    case ConsoleKey.W:
                        Observe(web.RestartAsync(Grace), ui);
                        break;
                    case ConsoleKey.O:
                        Shell.Open(web.Spec.Url, ui);
                        break;
                    case ConsoleKey.L:
                        Shell.Open(logs, ui);
                        break;
                    case ConsoleKey.H:
                        ui.Keys();
                        break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "keys",
        };
        thread.Start();
    }

    private static string Changed(IReadOnlyList<string> files) =>
        files.Count == 1 ? $"{files[0]} changed" : $"{files[0]} and {files.Count - 1} more changed";

    private static void Observe(Task restart, ConsoleUi ui) =>
        restart.ContinueWith(
            failed => ui.Fail($"restart failed: {failed.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
