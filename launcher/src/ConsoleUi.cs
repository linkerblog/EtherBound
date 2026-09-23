using System.Globalization;
using System.Text;
using EtherBound.Launcher.Win32;

namespace EtherBound.Launcher;

internal sealed class ConsoleUi
{
    private const string LauncherSource = "launcher";
    private const int HeaderInnerWidth = 66;
    private const string KeyLine =
        "  [O] Game   [R] Server   [W] Web   [L] Logs   [H] Help   [Q] Quit";

    private readonly Lock gate = new();
    private LogFile? log;

    public ConsoleUi()
    {
        Interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
        Colors = !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        if (!Console.IsOutputRedirected)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
        }
    }

    // Keys need a console to read from. Without one (an agent, a redirected run) the launcher
    // prints plain lines and is stopped by ending its process.
    public bool Interactive { get; }

    public bool Colors { get; }

    public void AttachLog(LogFile launcherLog)
    {
        lock (gate)
        {
            log = launcherLog;
        }
    }

    public void Header(string version)
    {
        lock (gate)
        {
            var border = new string('\u2500', HeaderInnerWidth);
            var release = $"RELEASE  {version}";
            var brand = "  E T H E R B O U N D  //  DEV LAUNCHER";
            Paint(ConsoleColor.DarkCyan, $"\u256d{border}\u256e");
            Paint(ConsoleColor.Cyan, BoxRow(brand.PadRight(HeaderInnerWidth - release.Length) + release));
            Paint(ConsoleColor.DarkCyan, $"\u251c{border}\u2524");
            Paint(
                ConsoleColor.Gray,
                BoxRow($"  SERVER  http://127.0.0.1:{Services.ServerPort}   |   WEB  http://127.0.0.1:{Services.WebPort}"));
            if (Interactive)
            {
                Paint(ConsoleColor.DarkYellow, BoxRow(KeyLine));
            }

            Paint(ConsoleColor.DarkCyan, $"\u2570{border}\u256f");
            Console.WriteLine();
        }
    }

    public void Keys()
    {
        lock (gate)
        {
            Paint(ConsoleColor.DarkYellow, KeyLine);
        }
    }

    public void Service(string source, string text) => Line(source, text, ConsoleColor.Gray);

    public void Step(string text) => Line(LauncherSource, "[ .. ] " + text, ConsoleColor.Gray);

    public void Ok(string text) => Line(LauncherSource, "[ OK ] " + text, ConsoleColor.Green);

    public void Warn(string text) => Line(LauncherSource, "[WARN] " + text, ConsoleColor.Yellow);

    public void Fail(string text) => Line(LauncherSource, "[FAIL] " + text, ConsoleColor.Red);

    public void Quote(string text) => Line(LauncherSource, "     | " + text, ConsoleColor.DarkGray);

    public void SetTitle(string title)
    {
        try
        {
            Console.Title = title;
        }
        catch (IOException)
        {
            // No console to put a title on.
        }
    }

    public bool AskKillOrQuit()
    {
        lock (gate)
        {
            Paint(ConsoleColor.Yellow, " [K] kill it and continue   [Q] quit");
        }

        while (true)
        {
            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.K:
                    return true;
                case ConsoleKey.Q or ConsoleKey.Escape:
                    return false;
            }
        }
    }

    // A double-clicked launcher is alone on its console, and the window would vanish with the error.
    public unsafe void PauseIfOwnWindow()
    {
        if (!Interactive)
        {
            return;
        }

        var ids = stackalloc uint[4];
        if (Native.GetConsoleProcessList(ids, 4) != 1)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(" Press any key to close.");
        Console.ReadKey(intercept: true);
    }

    private void Line(string source, string text, ConsoleColor color)
    {
        var now = DateTime.Now;
        var time = now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        lock (gate)
        {
            if (source == LauncherSource)
            {
                log?.Write(now, text);
            }

            if (!Colors)
            {
                Console.Out.WriteLine($"{time}  {source,-8}  {text}");
                return;
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(time + "  ");
            Console.ForegroundColor = SourceColor(source);
            Console.Write($"{source,-8}  ");
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    private void Paint(ConsoleColor color, string text)
    {
        if (Colors)
        {
            Console.ForegroundColor = color;
        }

        Console.WriteLine(text);
        if (Colors)
        {
            Console.ResetColor();
        }
    }

    private static string BoxRow(string text) => $"\u2502{text.PadRight(HeaderInnerWidth)}\u2502";

    private static ConsoleColor SourceColor(string source) => source switch
    {
        "server" => ConsoleColor.Cyan,
        "web" => ConsoleColor.Magenta,
        _ => ConsoleColor.White,
    };
}
