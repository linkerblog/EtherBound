using System.Globalization;

namespace EtherBound.Launcher;

internal enum Mode
{
    Run,
    Cleanup,
    Help,
    CtrlC,
}

internal sealed class UsageException(string message) : Exception(message);

internal sealed record Options(Mode Mode, bool Reload, bool Open, int ProcessId)
{
    public const string Usage = """
        Usage:
          EtherBound.exe                 start and supervise server + web
              --no-reload                server without hot reload
              --open                     open the game in the browser once both are ready
          EtherBound.exe --cleanup       remove this checkout's leftovers, report other port owners
          EtherBound.exe --help
        """;

    public static Options Parse(IReadOnlyList<string> args)
    {
        var mode = Mode.Run;
        var reload = true;
        var open = false;
        var processId = 0;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--no-reload":
                    reload = false;
                    break;
                case "--open":
                    open = true;
                    break;
                case "--cleanup":
                    mode = Switch(mode, Mode.Cleanup);
                    break;
                case "--help" or "-h" or "/?":
                    mode = Switch(mode, Mode.Help);
                    break;
                case "--ctrl-c":
                    mode = Switch(mode, Mode.CtrlC);
                    if (i + 1 >= args.Count ||
                        !int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out processId) ||
                        processId <= 0)
                    {
                        throw new UsageException("--ctrl-c needs a process id.");
                    }

                    break;
                default:
                    throw new UsageException($"Unknown option: {args[i]}");
            }
        }

        if (mode != Mode.Run && (!reload || open))
        {
            throw new UsageException("--no-reload and --open only apply when starting the services.");
        }

        return new Options(mode, reload, open, processId);
    }

    private static Mode Switch(Mode current, Mode next) =>
        current == Mode.Run ? next : throw new UsageException("Use only one of --cleanup, --help and --ctrl-c.");
}
