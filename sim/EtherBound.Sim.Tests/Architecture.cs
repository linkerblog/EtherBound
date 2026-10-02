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
    public void The_sim_never_references_the_llm_assembly_nor_the_network()
    {
        Assert.DoesNotContain(SimAssembly.Assembly.GetReferencedAssemblies(), a => a.Name == "EtherBound.Llm");
        Assert.Contains(Host.HostAssembly.Assembly.GetReferencedAssemblies(), a => a.Name == "EtherBound.Llm");
        var offenders = Sources(SimRoot).Where(s => Regex.IsMatch(s.Text, @"EtherBound\.Llm|System\.Net\.Http|HttpClient"))
            .Select(s => s.Relative);
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

    /// <summary>
    /// A mind proposes and the engine writes (Dev-011): in <c>Minds/</c> the only things asked of the engine
    /// are <c>Submit</c> and <c>SetGoal</c> to act, and reads. No session, store, database or engine class.
    /// </summary>
    [Fact]
    public void Minds_act_only_through_submit_and_set_goal()
    {
        var comments = new Regex(@"//.*$", RegexOptions.Multiline);
        var allowed = new HashSet<string> { "Submit", "SetGoal", "GetTickState", "GetState", "Grid", "Menu", "Percepts" };
        var minds = Sources(SimRoot).Where(s => s.Relative.StartsWith("Minds/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(minds);
        foreach (var (relative, text) in minds)
        {
            var code = comments.Replace(text, "");
            Assert.False(Regex.IsMatch(code, @"\b(WorldEngine|WorldStore|Session|ActionContext|OpHandler|Database|OpenSession|RecordDecision)\b"), relative);
            var used = Regex.Matches(code, @"_engine\.(\w+)").Select(m => m.Groups[1].Value).ToHashSet();
            Assert.True(used.IsSubsetOf(allowed), $"{relative} calls {string.Join(", ", used.Except(allowed))}");
        }
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
