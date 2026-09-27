using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Minds;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

/// <summary>The Extras routine brain (ported from <c>test_extras_brain.py</c>).</summary>
public sealed class ExtrasBrainRules : IDisposable
{
    private const long Seed = 0;
    private readonly List<string> _paths = new();

    private WorldEngine NewEngine(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"etherbound-brain-{name}-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        var engine = new WorldEngine(path);
        engine.EnsureWorld(Seed);
        return engine;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in _paths) File.Delete(path);
    }

    private static void Teleport(WorldEngine engine, string actorId, double x, double y, int h)
    {
        var session = engine.OpenSession();
        var actor = session.GetActor(actorId)!;
        (actor.X, actor.Y, actor.H, actor.Z) = (x, y, h, PyMath.FloorDiv(h, 6));
        actor.Activity = null;
        session.Commit();
    }

    private static int? ValidTile(WorldEngine engine, int x, int y)
    {
        var surfaces = engine.Grid.StandingSurfaces(x, y);
        return surfaces.Count > 0 ? surfaces[0].H : null;
    }

    [Fact]
    public void Set_goal_refuses_invalid_and_logs_valid()
    {
        using var engine = NewEngine("goal");
        var anchor = engine.GetState().Actors.First(a => a.Id == "extra-001").Mind!.Anchor;

        Assert.Throws<ArgumentException>(() => engine.SetGoal(Ids.Player, new GoalSpot(anchor.X, anchor.Y, anchor.H), "chosen"));
        Assert.Throws<KeyNotFoundException>(() => engine.SetGoal("nobody", null, "arrived"));
        Assert.Throws<ArgumentException>(() => engine.SetGoal("extra-001", new GoalSpot(1_000_000, 1_000_000, 0), "chosen"));

        engine.SetGoal("extra-001", new GoalSpot(anchor.X, anchor.Y, anchor.H), "chosen");
        var events = engine.ReadEvents(0, 5000, "actor.goal_set");
        Assert.Equal("extra-001", events[^1].ActorId);
        Assert.Equal("chosen", events[^1].Data["reason"]!.GetValue<string>());
        Assert.Equal(anchor.X, events[^1].Data["goal"]!.Int("x"));

        engine.SetGoal("extra-001", null, "arrived");
        var last = engine.ReadEvents(0, 5000, "actor.goal_set")[^1];
        Assert.True(last.Data["goal"] is null);
        Assert.Equal("arrived", last.Data["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Extras_walk_and_wait_near_their_anchor()
    {
        using var engine = NewEngine("walk");
        new ExtrasBrain(engine).Attach(engine.Bus);
        for (var i = 0; i < 120; i++) engine.AdvanceTime();

        var extras = engine.GetState().Actors.Where(a => a.Kind == "extra").ToList();
        var walked = engine.ReadEvents(0, 50000, "actor.moved").Select(r => r.ActorId).ToHashSet();
        var waited = engine.ReadEvents(0, 50000, "activity.started").Where(r => r.Data["op"]!.GetValue<string>() == "wait").Select(r => r.ActorId).ToHashSet();
        var chosen = engine.ReadEvents(0, 50000, "actor.goal_set").Where(r => r.Data["goal"] is not null).ToList();
        var goals = chosen.GroupBy(r => r.ActorId).ToDictionary(g => g.Key!, g => g.Last().Data["goal"]!);

        foreach (var extra in extras)
        {
            Assert.Contains(extra.Id, walked);
            Assert.Contains(extra.Id, waited);
            var anchor = extra.Mind!.Anchor;
            Assert.True(goals.TryGetValue(extra.Id, out var goal));
            Assert.True(PyMath.Hypot(goal!.Num("x") - anchor.X, goal.Num("y") - anchor.Y) <= 12);
            Assert.True(PyMath.Hypot(extra.X - (anchor.X + 0.5), extra.Y - (anchor.Y + 0.5)) <= 16);
        }
        foreach (var row in engine.ReadEvents(0, 50000, "actor.moved"))
        {
            var source = row.Data["from_tile"]!;
            var target = row.Data["to_tile"]!;
            Assert.True(Math.Abs(target.Int("x") - source.Int("x")) <= 1 && Math.Abs(target.Int("y") - source.Int("y")) <= 1);
            Assert.Contains(engine.Grid.StandingSurfaces(target.Int("x"), target.Int("y")), s => s.H == target.Int("h"));
        }
    }

    [Fact]
    public void Two_engines_match_and_a_restart_continues()
    {
        using var a = NewEngine("a");
        new ExtrasBrain(a).Attach(a.Bus);
        using var b = NewEngine("b");
        new ExtrasBrain(b).Attach(b.Bus);
        for (var i = 0; i < 60; i++)
        {
            a.AdvanceTime();
            b.AdvanceTime();
        }
        var eventsA = a.ReadEvents(0, 500000);
        var eventsB = b.ReadEvents(0, 500000);
        Assert.Equal(eventsA.Count, eventsB.Count);
        for (var i = 0; i < eventsA.Count; i++)
        {
            Assert.Equal(eventsA[i].Type, eventsB[i].Type);
            Assert.True(Json.Same(eventsA[i].Data, eventsB[i].Data));
        }

        var marker = eventsA[^1].Seq;
        var aPath = _paths.First(p => p.Contains("-a-", StringComparison.Ordinal));
        var cPath = Path.Combine(Path.GetTempPath(), $"etherbound-brain-c-{Guid.NewGuid():N}.db");
        _paths.Add(cPath);
        SqliteConnection.ClearAllPools();
        File.Copy(aPath, cPath);
        // `a` is still open, so its newest commits live in the WAL until a checkpoint (Fix19).
        if (File.Exists(aPath + "-wal")) File.Copy(aPath + "-wal", cPath + "-wal");
        using var c = new WorldEngine(cPath);
        c.EnsureWorld(Seed);
        new ExtrasBrain(c).Attach(c.Bus);
        for (var i = 0; i < 60; i++)
        {
            a.AdvanceTime();
            c.AdvanceTime();
        }
        List<(string, string)> Tail(WorldEngine engine) => engine.ReadEvents(0, 500000).Where(e => e.Seq > marker).Select(e => (e.Type, e.Data.ToJsonString())).ToList();
        var tailA = Tail(a);
        Assert.NotEmpty(tailA);
        Assert.Equal(tailA, Tail(c));
    }

    [Fact]
    public void A_shoved_extra_recovers_and_a_blocked_goal_sticks()
    {
        using var engine = NewEngine("shove");
        new ExtrasBrain(engine).Attach(engine.Bus);
        var anchor = engine.GetState().Actors.First(a => a.Id == "extra-001").Mind!.Anchor;
        var goal = new GoalSpot(anchor.X, anchor.Y, anchor.H);

        // Start ten metres out and give it one tick to walk before the shove, so the goal is
        // still set when the position changes and the cached plan must be thrown away.
        var (nx, ny) = (anchor.X + 10, anchor.Y);
        var nearH = ValidTile(engine, nx, ny)!.Value;
        Teleport(engine, "extra-001", nx + 0.5, ny + 0.5, nearH);
        engine.SetGoal("extra-001", goal, "chosen");
        engine.AdvanceTime();
        Assert.NotNull(engine.GetState().Actors.First(a => a.Id == "extra-001").Mind!.Goal);

        var (fx, fy) = (anchor.X - 8, anchor.Y);
        var farH = ValidTile(engine, fx, fy)!.Value;
        Teleport(engine, "extra-001", fx + 0.5, fy + 0.5, farH);
        for (var i = 0; i < 300; i++)
        {
            engine.AdvanceTime();
            if (engine.GetState().Actors.First(a => a.Id == "extra-001").Mind!.Goal is null) break;
        }
        var arrivals = engine.ReadEvents(0, 500000, "actor.goal_set").Where(r => r.ActorId == "extra-001" && r.Data["reason"]!.GetValue<string>() == "arrived").ToList();
        Assert.NotEmpty(arrivals);
        var arrived = engine.GetState().Actors.First(a => a.Id == "extra-001");
        Assert.Equal(((int)arrived.X, (int)arrived.Y, arrived.H), (anchor.X, anchor.Y, anchor.H));

        // Blocked goal: teleport into a region with no standing surface, so A* cannot leave it.
        const string other = "extra-002";
        engine.SetGoal(other, goal, "chosen");
        Teleport(engine, other, 1000.5, 1000.5, 0);
        for (var i = 0; i < 3; i++) engine.AdvanceTime();
        var stuck = engine.ReadEvents(0, 500000, "actor.goal_set").Where(r => r.ActorId == other && r.Data["reason"]!.GetValue<string>() == "stuck").ToList();
        Assert.NotEmpty(stuck);
    }

    [Fact]
    public void A_paused_clock_moves_no_extra()
    {
        using var engine = NewEngine("paused");
        new ExtrasBrain(engine).Attach(engine.Bus);
        engine.SetClock(paused: true);
        var before = engine.GetState().Actors.Select(a => (a.Id, a.X, a.Y)).ToHashSet();
        for (var i = 0; i < 10; i++) engine.AdvanceTime();
        var after = engine.GetState().Actors.Select(a => (a.Id, a.X, a.Y)).ToHashSet();
        Assert.Equal(before, after);
        Assert.DoesNotContain(engine.ReadEvents(0, 500000, "actor.moved"), r => r.ActorId != Ids.Player);
    }
}
