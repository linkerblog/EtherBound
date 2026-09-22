using EtherBound.Launcher.Win32;

namespace EtherBound.Launcher.Tests;

public class LeftoverRuleTests
{
    private const string Root = @"C:\Work\EtherBound";
    private const int Self = 1;
    private static readonly DateTime Boot = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_server_of_this_checkout_listening_on_8000_is_a_leftover()
    {
        var python = Running(10, 9, @"C:\Python314\python.exe",
            @"""C:\Python314\python.exe"" ""C:\Work\EtherBound\server\.venv\Scripts\uvicorn.exe"" etherbound.app:app");

        var findings = Evaluate([python], Listen(8000, 10));

        Assert.Equal(10, Assert.Single(findings.Leftovers).Process.Id);
        Assert.Empty(findings.Foreign);
    }

    [Fact]
    public void A_listener_whose_living_parent_is_this_checkouts_venv_is_a_leftover()
    {
        var venv = Running(20, 0, @"C:\Work\EtherBound\server\.venv\Scripts\python.exe", "python.exe", minute: 1);
        var worker = Running(21, 20, @"C:\Python314\python.exe",
            @"C:\Python314\python.exe -c ""from multiprocessing.spawn import spawn_main""", minute: 2);

        var findings = Evaluate([venv, worker], Listen(8000, 21));

        Assert.Equal(21, Assert.Single(findings.Leftovers).Process.Id);
    }

    [Fact]
    public void A_parent_that_started_after_its_child_is_a_reused_id()
    {
        var impostor = Running(30, 0, @"C:\Work\EtherBound\server\.venv\Scripts\python.exe", "python.exe", minute: 9);
        var other = Running(31, 30, @"C:\Python314\python.exe", "python.exe -m http.server 8000", minute: 2);

        var findings = Evaluate([impostor, other], Listen(8000, 31));

        Assert.Empty(findings.Leftovers);
        Assert.Equal(31, Assert.Single(findings.Foreign).Owner?.Id);
    }

    [Fact]
    public void Vite_started_through_the_npm_shim_is_a_leftover()
    {
        var node = Running(40, 39, @"C:\Program Files\nodejs\node.exe",
            @"""node""   ""C:\Work\EtherBound\web\node_modules\.bin\\..\vite\bin\vite.js"" --host 127.0.0.1 --port 5173");

        var findings = Evaluate([node], Listen(5173, 40));

        Assert.Equal(40, Assert.Single(findings.Leftovers).Process.Id);
    }

    [Fact]
    public void A_server_of_another_worktree_is_foreign()
    {
        var python = Running(50, 0, @"C:\Python314\python.exe",
            @"python.exe ""C:\Work\EtherBound\.kilo\worktrees\x\server\.venv\Scripts\uvicorn.exe"" etherbound.app:app");

        var findings = Evaluate([python], Listen(8000, 50));

        Assert.Empty(findings.Leftovers);
        Assert.Single(findings.Foreign);
    }

    [Fact]
    public void A_sibling_checkout_with_a_longer_name_is_foreign()
    {
        var python = Running(60, 0, @"C:\Work\EtherBound2\server\.venv\Scripts\python.exe", "python.exe");

        var findings = Evaluate([python], Listen(8000, 60));

        Assert.Empty(findings.Leftovers);
        Assert.Single(findings.Foreign);
    }

    [Fact]
    public void Matching_ignores_case_and_slash_direction()
    {
        var python = Running(70, 0, null, "python c:/work/etherbound/SERVER/.venv/Scripts/uvicorn.exe etherbound.app:app");

        var findings = Evaluate([python], Listen(8000, 70));

        Assert.Single(findings.Leftovers);
    }

    [Fact]
    public void A_venv_process_that_does_not_listen_is_left_alone()
    {
        var pytest = Running(80, 0, @"C:\Work\EtherBound\server\.venv\Scripts\pytest.exe", "pytest");

        var findings = Evaluate([pytest]);

        Assert.Empty(findings.Leftovers);
        Assert.Empty(findings.Foreign);
    }

    [Fact]
    public void The_old_launcher_parts_are_leftovers_without_listening()
    {
        var serverWrapper = Running(90, 0, @"C:\Windows\System32\cmd.exe",
            @"""C:\Windows\system32\cmd.exe"" /d /c call ""C:\Work\EtherBound\scripts\run-server.bat"" > ""C:\Work\EtherBound\logs\server.log"" 2>&1");
        var supervisor = Running(91, 0, @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            @"powershell -NoProfile -ExecutionPolicy Bypass -File ""C:\Work\EtherBound\scripts\launcher.ps1""");
        var startBat = Running(92, 0, @"C:\Windows\System32\cmd.exe", @"cmd.exe /c """"C:\Work\EtherBound\start.bat"" """);

        var findings = Evaluate([serverWrapper, supervisor, startBat]);

        Assert.Equal([90, 91], findings.Leftovers.Select(leftover => leftover.Process.Id).Order());
    }

    [Fact]
    public void A_listener_whose_owner_is_gone_is_reported_without_an_owner()
    {
        var findings = Evaluate([], Listen(8000, 99));

        Assert.Null(Assert.Single(findings.Foreign).Owner);
    }

    [Fact]
    public void Other_ports_are_not_watched()
    {
        var python = Running(100, 0, @"C:\Work\EtherBound\server\.venv\Scripts\python.exe", "python.exe");

        var findings = Evaluate([python], Listen(3000, 100));

        Assert.Empty(findings.Leftovers);
        Assert.Empty(findings.Foreign);
    }

    [Fact]
    public void The_launcher_itself_is_never_a_leftover()
    {
        var launcher = Running(Self, 0, @"C:\Work\EtherBound\EtherBound.exe",
            @"""C:\Work\EtherBound\EtherBound.exe"" C:\Work\EtherBound\scripts\run-server.bat");

        var findings = Evaluate([launcher], Listen(8000, Self));

        Assert.Empty(findings.Leftovers);
        Assert.Empty(findings.Foreign);
    }

    [Fact]
    public void An_IPv4_and_IPv6_listener_of_one_process_counts_once()
    {
        var node = Running(110, 0, @"C:\Program Files\nodejs\node.exe", @"node C:\Other\app\server.js");

        var findings = Evaluate([node], Listen(5173, 110), new Listener(5173, "::1", 110));

        Assert.Single(findings.Foreign);
    }

    private static ProcessInfo Running(int id, int parent, string? image, string? commandLine, int minute = 5) =>
        new(id, parent, Path.GetFileName(image ?? "unknown.exe"), image, commandLine, Boot.AddMinutes(minute));

    private static Listener Listen(int port, int processId) => new(port, "127.0.0.1", processId);

    private static Findings Evaluate(ProcessInfo[] processes, params Listener[] listeners) =>
        LeftoverRule.Evaluate(Root, processes.ToDictionary(process => process.Id), listeners, Self);
}
