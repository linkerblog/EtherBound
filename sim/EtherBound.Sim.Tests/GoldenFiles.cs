using System.Text.Json;

namespace EtherBound.Sim.Tests;

/// <summary>
/// The golden set is complete before any system is ported: every generator and seed, every RNG
/// method, every scripted scenario and the Dev-016 menus. The equality tests arrive in stage 2.
/// </summary>
public class GoldenFiles
{
    private static readonly long[] Seeds = { 0, 7, 1895070486 };

    [Fact]
    public void Worlds_cover_each_generator_and_seed()
    {
        using var doc = Goldens.Load("worlds.json");
        var keys = doc.RootElement.EnumerateArray()
            .Select(w => (w.GetProperty("generator").GetString(), w.GetProperty("seed").GetInt64()))
            .ToHashSet();
        foreach (var generator in new[] { "lab", "test" })
        foreach (var seed in Seeds)
            Assert.Contains((generator, seed), keys);
    }

    [Fact]
    public void Rng_streams_hold_each_method()
    {
        using var doc = Goldens.Load("rng.json");
        foreach (var stream in doc.RootElement.EnumerateArray())
        {
            Assert.Equal(1000, stream.GetProperty("random").GetArrayLength());
            foreach (var method in new[] { "randint", "uniform", "shuffle", "sample" })
                Assert.True(stream.GetProperty(method).GetArrayLength() > 0, method);
        }
    }

    [Fact]
    public void Scenarios_cover_the_stage_one_list()
    {
        using var doc = Goldens.Load("scenarios.json");
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        foreach (var name in new[] { "walk", "walk-stairs", "dig", "handling", "push", "throw", "hit-break", "climb", "wait", "extras", "extras-x10" })
            Assert.Contains(name, names);
        foreach (var scenario in doc.RootElement.EnumerateObject())
        {
            Assert.Equal(JsonValueKind.Array, scenario.Value.GetProperty("events").ValueKind);
            Assert.Equal(JsonValueKind.Object, scenario.Value.GetProperty("state").ValueKind);
        }
    }

    [Fact]
    public void Menus_include_the_dev016_tiles_and_every_lab_bay()
    {
        using var doc = Goldens.Load("menus.json");
        var labels = doc.RootElement.EnumerateArray().Select(m => m.GetProperty("label").GetString()).ToHashSet();
        foreach (var label in new[] { "table", "stacked-chests", "closed-chest", "feature", "walls", "objects" })
            Assert.Contains(label, labels);
    }
}
