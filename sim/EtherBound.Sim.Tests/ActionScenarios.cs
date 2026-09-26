using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Minds;
using EtherBound.Sim.World;
using Xunit.Abstractions;

namespace EtherBound.Sim.Tests;

/// <summary>
/// Replays every scripted scenario of <c>scenarios.json</c> on the C# engine: each submit's result,
/// the logged events and the final state must equal what the Python server produced.
/// </summary>
public class ActionScenarios
{
    private readonly ITestOutputHelper _output;

    public ActionScenarios(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Names()
    {
        using var doc = Goldens.Load("scenarios.json");
        return doc.RootElement.EnumerateObject().Select(p => new object[] { p.Name }).ToList();
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Scenario_matches_python(string name)
    {
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(Goldens.Directory, "scenarios.json")))![name]!;
        using var engine = new WorldEngine();
        engine.EnsureWorld(0);
        var after = engine.ReadEvents(0, 100000).LastOrDefault()?.Seq ?? 0;
        var run = new ScenarioRunner(engine);
        var results = run.Play(golden["steps"]!.AsArray());

        var expectedResults = golden["results"]!.AsArray();
        Assert.Equal(expectedResults.Count, results.Count);
        for (var i = 0; i < results.Count; i++) AssertSame($"result {i}", expectedResults[i], results[i]);

        var events = engine.ReadEvents(after, 100000).Select(e => (JsonNode)Json.Obj(("seq", e.Seq), ("game_minute", e.GameMinute),
            ("type", e.Type), ("actor_id", e.ActorId), ("data", e.Data.DeepClone()))).ToList();
        var expectedEvents = golden["events"]!.AsArray();
        for (var i = 0; i < Math.Min(events.Count, expectedEvents.Count); i++) AssertSame($"event {i}", expectedEvents[i], events[i]);
        Assert.Equal(expectedEvents.Count, events.Count);

        var state = golden["state"]!;
        var dump = StateDump.Of(engine);
        foreach (var key in new[] { "world", "actors", "objects", "chunk_revisions", "blocks_sha256" }) AssertSame($"state.{key}", state[key], dump[key]);
        var walls = state["wall_integrity"]!.AsArray().Select(w => w!.ToJsonString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var ourWalls = dump["wall_integrity"]!.AsArray().Select(w => w!.ToJsonString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.Equal(walls.Count, ourWalls.Count);
        for (var i = 0; i < walls.Count; i++) AssertSame($"wall {i}", JsonNode.Parse(walls[i]), JsonNode.Parse(ourWalls[i]));
        Assert.Empty(engine.Bus.Failures);
    }

    private void AssertSame(string what, JsonNode? expected, JsonNode? actual)
    {
        if (Json.Same(expected, actual)) return;
        _output.WriteLine($"{what}\nexpected: {expected?.ToJsonString()}\nactual:   {actual?.ToJsonString()}");
        Assert.Fail($"{what} differs:\nexpected: {expected?.ToJsonString()}\nactual:   {actual?.ToJsonString()}");
    }
}

/// <summary>Plays the step language of <c>export_goldens.py</c> against the C# engine.</summary>
public sealed class ScenarioRunner
{
    private readonly WorldEngine _engine;
    private readonly Dictionary<string, int> _refs = new();

    public ScenarioRunner(WorldEngine engine) => _engine = engine;

    public List<JsonNode> Play(JsonArray steps)
    {
        var results = new List<JsonNode>();
        foreach (var item in steps)
        {
            var step = item!;
            switch (step.Str("do"))
            {
                case "place":
                    Place(step.Int("x"), step.Int("y"), step["h"] is { } h ? Json.ToInt(h) : null);
                    break;
                case "spawn":
                    _refs[step.Str("ref")] = Spawn(step);
                    break;
                case "wall":
                    SetWall(step.Int("x"), step.Int("y"), step.Str("edge"), step.Str("material"));
                    break;
                case "brain":
                    new ExtrasBrain(_engine, step["time_scale"]?.GetValue<double>() ?? 1.0).Attach(_engine.Bus);
                    break;
                case "clock":
                    _engine.SetClock(speed: step.Int("speed"));
                    break;
                case "tick":
                    for (var i = 0; i < step.Int("n"); i++) _engine.AdvanceTime();
                    break;
                case "submit":
                    var action = GameAction.Parse(Resolve(step["action"]!.DeepClone())!);
                    var result = _engine.Submit(step["actor"]?.GetValue<string>() ?? Ids.Player, action, step["delta"]?.GetValue<double>() ?? 0.05);
                    results.Add(result.ToJson());
                    break;
                default:
                    throw new InvalidOperationException(step.Str("do"));
            }
        }
        return results;
    }

    private JsonNode? Resolve(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var copy = new JsonObject();
            foreach (var (k, v) in obj) copy[k] = Resolve(v?.DeepClone());
            return copy;
        }
        if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() is { } text && text.StartsWith('$'))
        {
            if (text.StartsWith("$ground:", StringComparison.Ordinal))
            {
                var p = text[8..].Split(',').Select(int.Parse).ToArray();
                return new TileTarget(p[0], p[1], _engine.Grid.GroundAt(p[0], p[1])!.Value.GroundH).ToJson();
            }
            if (text.StartsWith("$top:", StringComparison.Ordinal))
            {
                var p = text[5..].Split(',');
                var row = _engine.OpenSession().GetObject(_refs[p[2]])!;
                return new TileTarget(int.Parse(p[0]), int.Parse(p[1]), row.H!.Value + _engine.Catalog[row.Kind].Height).ToJson();
            }
            if (text.StartsWith("$held:", StringComparison.Ordinal))
            {
                var held = _engine.OpenSession().Objects().First(o => o.Loc == "held" && o.Kind == text[6..]);
                return new ObjectTarget(held.Id).ToJson();
            }
            return JsonValue.Create(_refs[text[1..]]);
        }
        return node;
    }

    private void Place(int x, int y, int? h)
    {
        var session = _engine.OpenSession();
        var actor = session.GetActor(Ids.Player)!;
        (actor.X, actor.Y) = (x + 0.5, y + 0.5);
        actor.H = h ?? _engine.Grid.GroundAt(x, y)!.Value.GroundH;
        actor.Z = PyMath.FloorDiv(actor.H, 6);
        actor.Activity = null;
        session.Commit();
    }

    private int Spawn(JsonNode step)
    {
        var session = _engine.OpenSession();
        var row = new ObjectRow { Kind = step.Str("kind"), Loc = "tile", Quantity = step["quantity"] is { } q ? Json.ToInt(q) : 1 };
        if (step["held"] is { } hand) ObjectHelpers.SetHeld(row, Ids.Player, hand.GetValue<string>());
        else
        {
            int x = step.Int("x"), y = step.Int("y");
            ObjectHelpers.SetTile(row, x, y, step["h"] is { } h ? Json.ToInt(h) : _engine.Grid.GroundAt(x, y)!.Value.GroundH);
        }
        if (step["state"] is JsonObject state) row.State = (JsonObject)state.DeepClone();
        session.AddObject(row);
        session.Commit();
        _engine.Reindex();
        return row.Id;
    }

    private void SetWall(int x, int y, string edge, string material)
    {
        var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(x, y);
        var level = _engine.Grid.Level(cx, cy, 0) ?? ChunkLevel.Empty(cx, cy, 0);
        var index = Chunk.Index(lx, ly);
        var walls = (ushort[])(edge == "north" ? level.WallN : level.WallW).Clone();
        walls[index] = (ushort)_engine.Registry[material].Id;
        _engine.Grid.AddLevel(edge == "north" ? level.With(wallN: walls) : level.With(wallW: walls));
        var session = _engine.OpenSession();
        session.MarkLevel(cx, cy, 0);
        session.Commit();
    }
}

/// <summary>The same dump as <c>export_goldens.state_dump</c>, read from the committed store.</summary>
public static class StateDump
{
    public static JsonObject Of(WorldEngine engine)
    {
        var session = engine.OpenSession();
        var world = session.World;
        using var bytes = new MemoryStream();
        var revisions = new JsonObject();
        foreach (var chunk in engine.Grid.Chunks.Values.OrderBy(c => c.Cx).ThenBy(c => c.Cy))
        {
            bytes.Write(chunk.GroundBlob);
            bytes.Write(chunk.SurfaceBlob);
            bytes.Write(chunk.DugBlob);
            if (chunk.Revision != 0) revisions[$"{chunk.Cx},{chunk.Cy}"] = chunk.Revision;
        }
        foreach (var level in engine.Grid.Levels.Values.OrderBy(l => l.Cx).ThenBy(l => l.Cy).ThenBy(l => l.Z))
        {
            bytes.Write(level.FloorBlob);
            bytes.Write(level.FloorMatBlob);
            bytes.Write(level.WallNBlob);
            bytes.Write(level.WallWBlob);
            bytes.Write(level.EdgeFlagsBlob);
            bytes.Write(level.FlagsBlob);
        }
        return Json.Obj(
            ("world", Json.Obj(("seed", world.Seed), ("game_minute", world.GameMinute), ("speed", world.Speed), ("paused", world.Paused),
                ("gen_version", world.GenVersion), ("generator", world.Generator), ("gen_options", world.GenOptions.DeepClone()))),
            ("actors", new JsonArray(session.Actors().Select(a => (JsonNode)Json.Obj(("id", a.Id), ("kind", a.Kind), ("name", a.Name),
                ("x", a.X), ("y", a.Y), ("z", a.Z), ("h", a.H), ("mass_kg", a.MassKg), ("activity", a.Activity?.DeepClone()),
                ("mind", a.Mind?.DeepClone()))).ToArray())),
            ("objects", new JsonArray(session.Objects().Select(o => (JsonNode)Json.Obj(("id", o.Id), ("kind", o.Kind), ("loc", o.Loc),
                ("x", o.X), ("y", o.Y), ("h", o.H), ("cx", o.Cx), ("cy", o.Cy), ("container_id", o.ContainerId), ("actor_id", o.ActorId),
                ("slot", o.Slot), ("quantity", o.Quantity), ("state", o.State.DeepClone()), ("integrity", o.Integrity), ("owner", o.Owner))).ToArray())),
            ("wall_integrity", new JsonArray(engine.WallRows().Select(w => (JsonNode)new JsonArray(w.Key.Cx, w.Key.Cy, w.Key.Z, w.Key.CellIndex, w.Key.Edge, w.Value)).ToArray())),
            ("chunk_revisions", revisions),
            ("blocks_sha256", Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()))));
    }
}
