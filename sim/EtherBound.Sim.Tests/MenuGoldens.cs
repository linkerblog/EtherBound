using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;
using Xunit.Abstractions;

namespace EtherBound.Sim.Tests;

/// <summary>
/// Menus are built by the sim: the Dev-016 tiles and every lab bay match the Python output entry for
/// entry. The ops in <see cref="AfterCutOver"/> are the exception and are dropped from both sides of the
/// comparison: the Python server never had a handler for them, so their entries are not in the frozen
/// fixture and adding them would mean regenerating a golden that no longer has a producer.
/// </summary>
public class MenuGoldens
{
    private readonly ITestOutputHelper _output;

    public MenuGoldens(ITestOutputHelper output) => _output = output;

    /// <summary>Ops added after the cut-over, which the frozen Python fixture cannot contain.</summary>
    private static readonly HashSet<string> AfterCutOver = new(StringComparer.Ordinal) { "build", "eat", "drink", "sleep" };

    private static readonly (string Label, int Sx, int Sy, int? Sh, int Tx, int Ty)[] TestTiles =
    {
        ("table", 136, 133, 12, 137, 133),
        ("stacked-chests", 125, 127, null, 126, 127),
        ("closed-chest", 123, 127, null, 124, 127),
        ("spawn", 121, 128, null, 121, 128),
    };

    private static void Place(WorldEngine engine, int x, int y, int? h)
    {
        var session = engine.OpenSession();
        var actor = session.GetActor(Ids.Player)!;
        (actor.X, actor.Y) = (x + 0.5, y + 0.5);
        actor.H = h ?? engine.Grid.GroundAt(x, y)!.Value.GroundH;
        actor.Z = PyMath.FloorDiv(actor.H, 6);
        actor.Activity = null;
        session.Commit();
    }

    private static JsonObject Comparable(MenuPayload menu)
    {
        var json = (JsonObject)menu.ToJson();
        var entries = json["entries"]!.AsArray().Where(e => !AfterCutOver.Contains(e!.Str("op")))
            .Select(e => (JsonNode?)e!.DeepClone()).ToArray();
        json["entries"] = new JsonArray(entries);
        return json;
    }

    private void Check(JsonArray goldens, string world, string label, int radius, MenuPayload menu)
    {
        var golden = goldens.First(g => g!.Str("world") == world && g.Str("label") == label && g.Int("radius") == radius)!["menu"];
        var actual = Comparable(menu);
        if (Json.Same(golden, actual)) return;
        _output.WriteLine($"expected: {golden!.ToJsonString()}\nactual:   {actual.ToJsonString()}");
        Assert.Fail($"menu {world}/{label}/r{radius} differs");
    }

    [Fact]
    public void Test_world_tiles_match_python()
    {
        var goldens = JsonNode.Parse(File.ReadAllText(Path.Combine(Goldens.Directory, "menus.json")))!.AsArray();
        using var engine = new WorldEngine();
        engine.EnsureWorld(0);
        foreach (var (label, sx, sy, sh, tx, ty) in TestTiles)
        {
            Place(engine, sx, sy, sh);
            var z = engine.OpenSession().GetActor(Ids.Player)!.Z;
            foreach (var radius in new[] { 0, 1 }) Check(goldens, "test-0", label, radius, engine.Menu(Ids.Player, tx, ty, z, radius));
        }
    }

    [Fact]
    public void Lab_bays_match_python()
    {
        var goldens = JsonNode.Parse(File.ReadAllText(Path.Combine(Goldens.Directory, "menus.json")))!.AsArray();
        using var engine = new WorldEngine();
        engine.EnsureWorld(0);
        engine.NewGame(0, "lab");
        foreach (var bay in Generators.All["lab"].Bays)
        {
            int x = bay.X + bay.Width / 2, y = bay.Y + bay.Height / 2;
            var surfaces = engine.Grid.StandingSurfaces(x, y);
            Place(engine, x, y, surfaces.Count > 0 ? surfaces[0].H : null);
            var z = engine.OpenSession().GetActor(Ids.Player)!.Z;
            foreach (var radius in new[] { 0, 1 }) Check(goldens, "lab-0", bay.Key, radius, engine.Menu(Ids.Player, x, y, z, radius));
        }
    }
}

/// <summary>The shared standing and wall rule tables (<c>world-parity.json</c>) hold on the C# grid.</summary>
public class WorldParity
{
    private static readonly MaterialRegistry Registry = MaterialRegistry.Load();

    private static List<ChunkLevel> Levels(JsonArray specs)
    {
        const int index = 33;
        var levels = new List<ChunkLevel>();
        foreach (var spec in specs)
        {
            var level = ChunkLevel.Empty(0, 0, spec!.Int("z"));
            if (spec!["floor_h"] is { } floor)
            {
                level.FloorH[index] = (short)Json.ToInt(floor);
                level.FloorMat[index] = (ushort)Registry[spec.Str("floor_material")].Id;
            }
            if (spec["wall_n"] is { } wallN) level.WallN[index] = (ushort)Registry[wallN.GetValue<string>()].Id;
            if (spec["wall_w"] is { } wallW) level.WallW[index] = (ushort)Registry[wallW.GetValue<string>()].Id;
            level.EdgeFlags[index] = (byte)(spec["edge_flags"] is { } e ? Json.ToInt(e) : 0);
            level.Flags[index] = (byte)(spec["flags"] is { } f ? Json.ToInt(f) : 0);
            levels.Add(level);
        }
        return levels;
    }

    private static JsonNode Cases() => JsonNode.Parse(File.ReadAllText(Path.Combine(Goldens.Directory, "world-parity.json")))!;

    [Fact]
    public void Standing_rule_table_holds()
    {
        foreach (var c in Cases()["standing"]!.AsArray())
        {
            var chunk = Chunk.Flat(0, 0, c!.Int("ground_h"), Registry[c.Str("ground_material")].Id);
            var grid = new WorldGrid(new[] { chunk }, Levels(c!["levels"]!.AsArray()), Registry);
            var actorH = c.Int("actor_h");
            var near = grid.StandingSurfaces(1, 1).Select(s => s.H).Where(h => Math.Abs(h - actorH) <= 1).ToList();
            int? actual = near.Count > 0 ? near.OrderBy(h => Math.Abs(h - actorH)).First() : null;
            int? expected = c["expected_h"] is { } e ? Json.ToInt(e) : null;
            Assert.True(expected == actual, c.Str("name"));
        }
    }

    [Fact]
    public void Wall_rule_table_holds()
    {
        foreach (var c in Cases()["walls"]!.AsArray())
        {
            var chunk = Chunk.Flat(0, 0, c!.Int("ground_h"), Registry["grass"].Id);
            var grid = new WorldGrid(new[] { chunk }, Levels(c!["levels"]!.AsArray()), Registry);
            var actorH = c.Int("actor_h");
            var blocked = c.Str("direction") == "north" ? grid.WallBetween(1, 1, 1, 0, actorH) : grid.WallBetween(1, 1, 0, 1, actorH);
            Assert.True(blocked == c["expected_blocked"]!.GetValue<bool>(), c.Str("name"));
        }
    }
}
