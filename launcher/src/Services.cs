using System.Globalization;

namespace EtherBound.Launcher;

internal sealed record ServiceSpec(
    string Name,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int Port,
    Uri ReadyUrl,
    TimeSpan StartTimeout)
{
    public string Url => $"http://127.0.0.1:{Port}/";
}

internal static class Services
{
    public const int ServerPort = 8000;
    public const int WebPort = 5173;

    public static readonly IReadOnlyCollection<int> Ports = [ServerPort, WebPort];

    // Never uvicorn's --reload: its reloader restarts the worker with a console Ctrl+C that conhost
    // only dispatches on console traffic, and piped output makes none, so the reload hangs. The
    // launcher watches the sources and restarts the server instead (SourceWatcher).
    public static ServiceSpec Server(string root, string uv) =>
        new(
            "server",
            uv,
            ["run", "uvicorn", "etherbound.app:app", "--host", "127.0.0.1", "--port", Text(ServerPort)],
            Path.Combine(root, "server"),
            new Dictionary<string, string>
            {
                // Python block-buffers a piped stdout, which would hold log lines back.
                ["PYTHONUNBUFFERED"] = "1",
                // Piped stdio would otherwise use the ANSI code page.
                ["PYTHONIOENCODING"] = "utf-8",
                ["NO_COLOR"] = "1",
            },
            ServerPort,
            new Uri($"http://127.0.0.1:{ServerPort}/api/health"),
            // The first `uv run` may sync the venv; migrations and world generation run at startup.
            TimeSpan.FromSeconds(120));

    public static ServiceSpec Web(string root, string node) =>
        new(
            "web",
            node,
            // Vite's JS entry rather than `npm run dev`: npm and node_modules\.bin\vite.cmd are batch
            // shims, and cmd.exe turns a Ctrl+C into a prompt nobody can answer. The absolute path
            // also lets the leftover rule recognise the process.
            [ViteEntry(root), "--host", "127.0.0.1", "--port", Text(WebPort), "--strictPort"],
            Path.Combine(root, "web"),
            // Never FORCE_COLOR: picocolors treats any value, "0" included, as "force".
            new Dictionary<string, string> { ["NO_COLOR"] = "1" },
            WebPort,
            new Uri($"http://127.0.0.1:{WebPort}/"),
            TimeSpan.FromSeconds(30));

    public static string ViteEntry(string root) =>
        Path.Combine(root, "web", "node_modules", "vite", "bin", "vite.js");

    private static string Text(int port) => port.ToString(CultureInfo.InvariantCulture);
}

internal static class PathSearch
{
    public static string? Find(string fileName)
    {
        var entries = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(entry.Trim('"')), fileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is skipped, as the shell would.
            }
        }

        return null;
    }
}
