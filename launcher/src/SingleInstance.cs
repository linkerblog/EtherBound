namespace EtherBound.Launcher;

// One launcher per checkout: a second one would take the first one's services for leftovers.
internal static class SingleInstance
{
    public static Mutex? TryAcquire(string root)
    {
        var mutex = new Mutex(initiallyOwned: true, Name(root), out var created);
        if (created)
        {
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    public static bool IsRunning(string root)
    {
        if (!Mutex.TryOpenExisting(Name(root), out var existing))
        {
            return false;
        }

        existing.Dispose();
        return true;
    }

    internal static string Name(string root)
    {
        // FNV-1a: stable across runs, unlike string.GetHashCode.
        var hash = 14695981039346656037UL;
        foreach (var character in root.ToUpperInvariant())
        {
            hash = (hash ^ character) * 1099511628211UL;
        }

        return $@"Local\EtherBound.Launcher.{hash:x16}";
    }
}
