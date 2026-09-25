using System.Text.RegularExpressions;

namespace EtherBound.Sim.Tests;

/// <summary>
/// Layering rules of Dev-025 [Sec. 6] 5, checked on sources and assemblies: the sim never sees
/// Godot, only the engine touches the database, and events never depend on the engine.
/// </summary>
public class Architecture
{
    private static readonly string SimRoot = FindSimRoot();

    private static string FindSimRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "sim", "EtherBound.Sim");
            if (File.Exists(Path.Combine(candidate, "EtherBound.Sim.csproj"))) return candidate;
        }
        throw new DirectoryNotFoundException("sim/EtherBound.Sim not found above the test output");
    }

    private static IEnumerable<(string Relative, string Text)> Sources(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)));

    [Fact]
    public void Sim_and_host_reference_no_godot_assembly()
    {
        foreach (var assembly in new[] { SimAssembly.Assembly, Host.HostAssembly.Assembly })
        {
            var godot = assembly.GetReferencedAssemblies().Where(a => a.Name!.StartsWith("Godot", StringComparison.Ordinal));
            Assert.Empty(godot);
        }
        var offenders = Sources(SimRoot).Where(s => Regex.IsMatch(s.Text, @"\bGodot\b")).Select(s => s.Relative);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Database_access_stays_inside_engine_and_db()
    {
        var database = new Regex(@"\b(Microsoft\.Data\.Sqlite|EtherBound\.Sim\.Db)\b");
        var offenders = Sources(SimRoot)
            .Where(s => !s.Relative.StartsWith("Engine/", StringComparison.Ordinal)
                && !s.Relative.StartsWith("Db/", StringComparison.Ordinal))
            .Where(s => database.IsMatch(s.Text))
            .Select(s => s.Relative);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Events_do_not_reference_the_engine()
    {
        var offenders = Sources(SimRoot)
            .Where(s => s.Relative.StartsWith("Events/", StringComparison.Ordinal))
            .Where(s => Regex.IsMatch(s.Text, @"\bEtherBound\.Sim\.Engine\b"))
            .Select(s => s.Relative);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Banned_symbols_are_wired_into_the_sim()
    {
        var project = File.ReadAllText(Path.Combine(SimRoot, "EtherBound.Sim.csproj"));
        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers", project);
        Assert.Contains("BannedSymbols.txt", project);
        var banned = File.ReadAllText(Path.Combine(SimRoot, "BannedSymbols.txt"));
        foreach (var symbol in new[] { "T:System.Random", "P:System.DateTime.Now", "T:System.Diagnostics.Stopwatch", "T:System.Threading.Thread" })
            Assert.Contains(symbol, banned);
    }
}
