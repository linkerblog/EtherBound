using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EtherBound.Launcher;

internal static partial class Ansi
{
    // CSI sequences (colours, cursor moves), OSC sequences (titles, links) and two-byte escapes.
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[@-Z\\-_])")]
    private static partial Regex Sequence();

    public static string Strip(string text) => text.Contains('\x1B') ? Sequence().Replace(text, "") : text;
}

internal sealed class LogFile : IDisposable
{
    private readonly Lock gate = new();
    private readonly StreamWriter writer;
    private bool closed;

    private LogFile(string filePath, StreamWriter writer)
    {
        FilePath = filePath;
        this.writer = writer;
    }

    public string FilePath { get; }

    public static LogFile Open(string directory, string name, DateTime now, out string? warning)
    {
        Directory.CreateDirectory(directory);
        var filePath = RotationTarget(directory, name, now, out warning);
        var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
        return new LogFile(filePath, new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
    }

    // Keeps the previous run as <name>.prev.log. A log some other process still holds open falls
    // back to a timestamped name instead of failing the launch.
    internal static string RotationTarget(string directory, string name, DateTime now, out string? warning)
    {
        warning = null;
        var current = Path.Combine(directory, name + ".log");
        try
        {
            if (File.Exists(current))
            {
                File.Move(current, Path.Combine(directory, name + ".prev.log"), overwrite: true);
            }

            return current;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var fallback = Path.Combine(directory, $"{name}.{stamp}.log");
            warning = $"logs\\{name}.log is held by another process; writing logs\\{Path.GetFileName(fallback)}";
            return fallback;
        }
    }

    public void Write(DateTime time, string text)
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            writer.Write(time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            writer.Write("  ");
            writer.WriteLine(text);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (!closed)
            {
                closed = true;
                writer.Dispose();
            }
        }
    }
}

internal sealed class TailBuffer(int capacity)
{
    private readonly Lock gate = new();
    private readonly Queue<string> lines = new(capacity);

    public void Add(string line)
    {
        lock (gate)
        {
            if (lines.Count == capacity)
            {
                lines.Dequeue();
            }

            lines.Enqueue(line);
        }
    }

    public string[] Last(int count)
    {
        lock (gate)
        {
            return lines.Skip(Math.Max(0, lines.Count - count)).ToArray();
        }
    }
}
