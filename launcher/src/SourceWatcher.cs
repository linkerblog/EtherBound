namespace EtherBound.Launcher;

// Hot reload for the server: watches its sources and reports each burst of saves once. onChange
// runs one call at a time and blocks until its restart is done; whatever is saved meanwhile
// makes exactly one more call afterwards.
internal sealed class SourceWatcher : IDisposable
{
    // Editors save in bursts (a write, a rename, a second write); one restart per burst.
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);
    private static readonly string[] Folders = ["src", "alembic"];

    private readonly Lock gate = new();
    private readonly SortedSet<string> changed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly string serverDirectory;
    private readonly Action<IReadOnlyList<string>> onChange;
    private readonly Timer debounce;
    private int flushing;

    public SourceWatcher(string serverDirectory, Action<IReadOnlyList<string>> onChange)
    {
        this.serverDirectory = serverDirectory;
        this.onChange = onChange;
        debounce = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        foreach (var folder in Folders)
        {
            var path = Path.Combine(serverDirectory, folder);
            if (!Directory.Exists(path))
            {
                continue;
            }

            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += OnEvent;
            watcher.Created += OnEvent;
            watcher.Deleted += OnEvent;
            watcher.Renamed += OnEvent;
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
    }

    // Python modules and the TOML data they load at startup (the materials registry).
    internal static bool Matters(string path) =>
        (path.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)) &&
        !path.Contains(@"\__pycache__\", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        foreach (var watcher in watchers)
        {
            watcher.Dispose();
        }

        debounce.Dispose();
    }

    private void OnEvent(object sender, FileSystemEventArgs change)
    {
        if (!Matters(change.FullPath))
        {
            return;
        }

        lock (gate)
        {
            changed.Add(Path.GetRelativePath(serverDirectory, change.FullPath));
            debounce.Change(Quiet, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        // Timer callbacks can overlap; the flush already running picks up the new files.
        if (Interlocked.Exchange(ref flushing, 1) == 1)
        {
            return;
        }

        try
        {
            while (Take() is { Length: > 0 } files)
            {
                onChange(files);
            }
        }
        finally
        {
            Volatile.Write(ref flushing, 0);
            lock (gate)
            {
                // A save that landed between the last Take and the reset must not wait for the next one.
                if (changed.Count > 0)
                {
                    debounce.Change(Quiet, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private string[] Take()
    {
        lock (gate)
        {
            string[] files = [.. changed];
            changed.Clear();
            return files;
        }
    }
}
