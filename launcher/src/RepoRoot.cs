namespace EtherBound.Launcher;

internal static class RepoRoot
{
    // EtherBound.exe normally sits in the checkout root, but `dotnet run` starts it from
    // launcher/src/bin/..., so walk up until the server and web projects appear.
    public static string? Find(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "server", "pyproject.toml")) &&
                File.Exists(Path.Combine(directory.FullName, "web", "package.json")))
            {
                return Path.TrimEndingDirectorySeparator(directory.FullName);
            }
        }

        return null;
    }
}
