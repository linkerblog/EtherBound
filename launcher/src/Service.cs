using System.ComponentModel;
using System.Diagnostics;
using EtherBound.Launcher.Win32;

namespace EtherBound.Launcher;

internal enum ServiceState
{
    Stopped,
    Starting,
    Ready,
    Down,
    Exited,
}

// One supervised service: its process, its job, its log and its health.
internal sealed class Service(ServiceSpec spec, LogFile log, ConsoleUi ui, HttpClient http) : IDisposable
{
    private const int FailuresBeforeDown = 3;
    private const int ReportLines = 15;
    private static readonly TimeSpan StartPoll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan HealthPoll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Notice = TimeSpan.FromSeconds(10);

    private readonly TailBuffer tail = new(200);
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private volatile Run? current;
    private volatile ServiceState state = ServiceState.Stopped;

    public event Action? StateChanged;

    public ServiceSpec Spec => spec;

    public ServiceState State => state;

    public string StateText => state.ToString().ToLowerInvariant();

    public void Start()
    {
        lifecycle.Wait();
        try
        {
            StartCore();
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task StopAsync(TimeSpan grace)
    {
        await lifecycle.WaitAsync();
        try
        {
            await StopCoreAsync(grace);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task RestartAsync(TimeSpan grace, string? reason = null)
    {
        await lifecycle.WaitAsync();
        try
        {
            ui.Step(reason is null ? $"restarting {spec.Name}" : $"restarting {spec.Name}: {reason}");
            await StopCoreAsync(grace);
            if (!await Wait.UntilAsync(() => Ports.Listeners([spec.Port]).Count == 0, TimeSpan.FromSeconds(5)))
            {
                ui.Warn($"port {spec.Port} is still in use; starting {spec.Name} anyway");
            }

            StartCore();
        }
        finally
        {
            lifecycle.Release();
        }
    }

    // Returns once the service has left Starting: ready, down, exited or stopped.
    public Task WaitUntilSettledAsync() =>
        Wait.UntilAsync(() => state != ServiceState.Starting, spec.StartTimeout);

    public void Dispose()
    {
        current?.Dispose();
        log.Dispose();
        lifecycle.Dispose();
    }

    private void StartCore()
    {
        var job = CreateJob();
        ChildProcess child;
        try
        {
            child = ChildProcess.Start(spec, job, out var jobError);
            if (jobError != 0)
            {
                ui.Warn($"{spec.Name} is not in a job of its own (error {jobError}); stopping it falls back to a tree kill");
                job?.Dispose();
                job = null;
            }
        }
        catch (Win32Exception error)
        {
            job?.Dispose();
            ui.Fail($"{spec.Name} could not start: {error.Message}");
            SetState(ServiceState.Exited);
            return;
        }

        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        child.Process.EnableRaisingEvents = true;
        child.Process.Exited += (_, _) => exited.TrySetResult();
        if (child.Process.HasExited)
        {
            exited.TrySetResult();
        }

        log.Write(DateTime.Now, $"[launcher] started PID {child.Process.Id}: {CommandLine.Build(spec.Executable, spec.Arguments)}");
        child.Pump(OnLine);
        var run = new Run(child, job, exited.Task);
        current = run;
        SetState(ServiceState.Starting);
        _ = MonitorAsync(run);
    }

    private Job? CreateJob()
    {
        try
        {
            return Job.CreateKillOnClose();
        }
        catch (Win32Exception error)
        {
            ui.Warn($"{spec.Name} gets no job of its own ({error.Message}); stopping it falls back to a tree kill");
            return null;
        }
    }

    private async Task MonitorAsync(Run run)
    {
        var token = run.Watch.Token;
        var clock = Stopwatch.StartNew();
        var nextNotice = Notice;
        var slow = false;
        var failures = 0;

        while (!token.IsCancellationRequested && !run.Exited.IsCompleted)
        {
            var healthy = await ProbeAsync(token);
            if (token.IsCancellationRequested || run.Exited.IsCompleted)
            {
                break;
            }

            if (state == ServiceState.Starting)
            {
                if (healthy)
                {
                    ui.Ok($"{spec.Name} ready in {clock.Elapsed.TotalSeconds:0.0} s at {spec.Url}");
                    SetState(ServiceState.Ready);
                }
                else if (!slow && clock.Elapsed >= spec.StartTimeout)
                {
                    slow = true;
                    ui.Warn($"{spec.Name} is not answering after {spec.StartTimeout.TotalSeconds:0} s; " +
                            $"still watching it (logs\\{spec.Name}.log)");
                }
                else if (!slow && clock.Elapsed >= nextNotice)
                {
                    ui.Step($"{spec.Name} still starting ({clock.Elapsed.TotalSeconds:0} s)");
                    nextNotice += Notice;
                }
            }
            else
            {
                failures = healthy ? 0 : failures + 1;
                if (state == ServiceState.Ready && failures >= FailuresBeforeDown)
                {
                    // Under --reload a broken module leaves the reloader running and nothing served.
                    ui.Fail($"{spec.Name} is down; last lines:");
                    Report();
                    SetState(ServiceState.Down);
                }
                else if (state == ServiceState.Down && healthy)
                {
                    ui.Ok($"{spec.Name} is back");
                    SetState(ServiceState.Ready);
                }
            }

            var pause = state == ServiceState.Starting && !slow ? StartPoll : HealthPoll;
            await Task.WhenAny(Task.Delay(pause, token), run.Exited);
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        // Let the last lines, usually the error, arrive before quoting them.
        await Task.WhenAny(run.Child.Drained, Task.Delay(TimeSpan.FromSeconds(1)));
        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            ui.Fail($"{spec.Name} exited with code {run.Child.Process.ExitCode}; last lines:");
            Report();
            // Whatever the root process left behind goes with it.
            run.Job?.Terminate();
            SetState(ServiceState.Exited);
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        {
            // A stop or restart disposed the run while its exit was being reported.
        }
    }

    private async Task<bool> ProbeAsync(CancellationToken token)
    {
        try
        {
            using var response = await http.GetAsync(spec.ReadyUrl, HttpCompletionOption.ResponseHeadersRead, token);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            // The client's timeout, or the watch being cancelled.
            return false;
        }
    }

    private async Task StopCoreAsync(TimeSpan grace)
    {
        var run = current;
        if (run is null)
        {
            return;
        }

        current = null;
        run.Watch.Cancel();
        var process = run.Child.Process;
        // Graceful first: on Ctrl+C uvicorn runs its lifespan shutdown and Vite closes. Not while
        // starting: there is nothing to shut down yet, and uv ignores a Ctrl+C that arrives before
        // it has launched Python, which would then never see it.
        if (!process.HasExited && state != ServiceState.Starting)
        {
            var signalled = await Task.Run(() => CtrlC.Request(process.Id));
            var ended = signalled && await Wait.UntilAsync(
                () => run.Job is { } job ? job.ActiveProcesses == 0 : process.HasExited, grace);
            if (!ended)
            {
                ui.Warn($"{spec.Name} did not stop on Ctrl+C within {grace.TotalSeconds:0} s; terminating it");
            }
        }

        if (run.Job is { } serviceJob)
        {
            serviceJob.Terminate();
        }
        else if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException or AggregateException)
            {
                ui.Warn($"{spec.Name} did not stop cleanly: {error.Message}");
            }
        }

        await Wait.UntilAsync(() => process.HasExited, TimeSpan.FromSeconds(3));
        // The last lines (the shutdown log) are still in the pipes.
        await Task.WhenAny(run.Child.Drained, Task.Delay(TimeSpan.FromSeconds(1)));
        run.Dispose();
        SetState(ServiceState.Stopped);
    }

    private void OnLine(string data)
    {
        var line = Ansi.Strip(data).TrimEnd();
        if (line.Length == 0)
        {
            return;
        }

        log.Write(DateTime.Now, line);
        tail.Add(line);
        ui.Service(spec.Name, line);
    }

    private void Report()
    {
        foreach (var line in tail.Last(ReportLines))
        {
            ui.Quote(line);
        }
    }

    private void SetState(ServiceState next)
    {
        state = next;
        StateChanged?.Invoke();
    }

    private sealed class Run(ChildProcess child, Job? job, Task exited) : IDisposable
    {
        public ChildProcess Child { get; } = child;

        public Job? Job { get; } = job;

        public Task Exited { get; } = exited;

        // Never disposed: the monitor may still read its token after the run is replaced.
        public CancellationTokenSource Watch { get; } = new();

        public void Dispose()
        {
            Job?.Dispose();
            Child.Dispose();
        }
    }
}

internal static class HealthClient
{
    public static HttpClient Create()
    {
        var handler = new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(1) };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(1) };
        // A fresh connection per probe: a pooled one dies with every reload and would count as a miss.
        client.DefaultRequestHeaders.ConnectionClose = true;
        return client;
    }
}
