namespace EtherBound.Launcher.Tests;

public sealed class LogFileTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 22, 11, 28, 30);
    private readonly string directory = Directory.CreateTempSubdirectory("etherbound-logs-").FullName;

    [Fact]
    public void Rotation_keeps_the_previous_run()
    {
        File.WriteAllText(Path.Combine(directory, "server.log"), "previous run");

        var target = LogFile.RotationTarget(directory, "server", Now, out var warning);

        Assert.Equal(Path.Combine(directory, "server.log"), target);
        Assert.Null(warning);
        Assert.False(File.Exists(target));
        Assert.Equal("previous run", File.ReadAllText(Path.Combine(directory, "server.prev.log")));
    }

    [Fact]
    public void A_log_held_open_elsewhere_falls_back_to_a_timestamped_name()
    {
        using var holder = new FileStream(
            Path.Combine(directory, "server.log"), FileMode.Create, FileAccess.Write, FileShare.None);

        var target = LogFile.RotationTarget(directory, "server", Now, out var warning);

        Assert.Equal(Path.Combine(directory, "server.20260922-112830.log"), target);
        Assert.NotNull(warning);
    }

    [Fact]
    public void Every_line_gets_a_timestamp()
    {
        using (var log = LogFile.Open(directory, "web", Now, out _))
        {
            log.Write(Now, "VITE ready");
        }

        Assert.Equal(
            "11:28:30.000  VITE ready" + Environment.NewLine,
            File.ReadAllText(Path.Combine(directory, "web.log")));
    }

    [Fact]
    public void Writing_after_close_is_ignored()
    {
        var log = LogFile.Open(directory, "web", Now, out _);
        log.Dispose();

        log.Write(Now, "late line");

        Assert.Equal("", File.ReadAllText(Path.Combine(directory, "web.log")));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

public sealed class RepoRootTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("etherbound-root-").FullName;

    public RepoRootTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "server"));
        Directory.CreateDirectory(Path.Combine(root, "web"));
        File.WriteAllText(Path.Combine(root, "server", "pyproject.toml"), "");
        File.WriteAllText(Path.Combine(root, "web", "package.json"), "{}");
    }

    [Fact]
    public void Finds_the_checkout_from_its_root() => Assert.Equal(root, RepoRoot.Find(root));

    [Fact]
    public void Finds_the_checkout_from_a_build_folder()
    {
        var bin = Directory.CreateDirectory(
            Path.Combine(root, "launcher", "src", "bin", "Debug", "net10.0-windows", "win-x64")).FullName;

        Assert.Equal(root, RepoRoot.Find(bin));
    }

    [Fact]
    public void Outside_a_checkout_there_is_no_root()
    {
        var elsewhere = Directory.CreateTempSubdirectory("etherbound-none-").FullName;
        try
        {
            Assert.Null(RepoRoot.Find(elsewhere));
        }
        finally
        {
            Directory.Delete(elsewhere);
        }
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}

public class SingleInstanceTests
{
    [Fact]
    public void The_mutex_name_ignores_case_and_differs_per_checkout()
    {
        var name = SingleInstance.Name(@"C:\Work\EtherBound");

        Assert.StartsWith(@"Local\EtherBound.Launcher.", name);
        Assert.Equal(name, SingleInstance.Name(@"c:\work\etherbound"));
        Assert.NotEqual(name, SingleInstance.Name(@"C:\Work\EtherBound\.kilo\worktrees\x"));
    }
}
