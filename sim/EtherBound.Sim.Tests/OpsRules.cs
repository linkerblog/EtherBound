using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Engine.Ops;
using EtherBound.Sim.World;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

/// <summary>Op catalog, menus, dig, climb, activity lifecycle and inspect (ported from <c>test_ops.py</c>).</summary>
public sealed class OpsRules : IDisposable
{
    // Seed 0: the road (x 120-123) and the grass east of it (x 124) are both at h = 2.
    private static readonly (int X, int Y) Road = (123, 128);
    private static readonly (int X, int Y) Grass = (124, 128);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"etherbound-ops-{Guid.NewGuid():N}.db");

    private WorldEngine NewEngine()
    {
        var engine = new WorldEngine(_path);
        engine.EnsureWorld(0);
        return engine;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    /// <summary>Put Niko at a tile centre; move the seeded Extras out of the way first.</summary>
    private static void Place(WorldEngine engine, int x, int y, int? h = null)
    {
        var ground = engine.Grid.GroundAt(x, y)!.Value;
        var session = engine.OpenSession();
        foreach (var extra in session.Actors().Where(a => a.Id != Ids.Player)) (extra.X, extra.Y, extra.H, extra.Z) = (250.5, 250.5, 0, 0);
        var actor = session.GetActor(Ids.Player)!;
        (actor.X, actor.Y) = (x + 0.5, y + 0.5);
        actor.H = h ?? ground.GroundH;
        actor.Z = PyMath.FloorDiv(actor.H, 6);
        session.Commit();
    }

    private static void PlaceExtra(WorldEngine engine, string actorId, int x, int y, int? h = null)
    {
        var ground = engine.Grid.GroundAt(x, y)!.Value;
        var session = engine.OpenSession();
        var extra = session.GetActor(actorId)!;
        (extra.X, extra.Y) = (x + 0.5, y + 0.5);
        extra.H = h ?? ground.GroundH;
        extra.Z = PyMath.FloorDiv(extra.H, 6);
        session.Commit();
    }

    /// <summary>Reshape one tile in memory only, as a synthetic world for a test.</summary>
    private static void SetGround(WorldEngine engine, int x, int y, int h, string material = "grass")
    {
        var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(x, y);
        var chunk = engine.Grid.Chunk(cx, cy)!;
        var index = Chunk.Index(lx, ly);
        var ground = (short[])chunk.GroundH.Clone();
        var surface = (ushort[])chunk.SurfaceMat.Clone();
        ground[index] = (short)h;
        surface[index] = (ushort)engine.Registry[material].Id;
        engine.Grid.AddChunk(chunk.With(ground, surface));
    }

    private static ActorRow Actor(WorldEngine engine) => engine.OpenSession().GetActor(Ids.Player)!;

    private static List<EventRow> Stored(WorldEngine engine, string type) => engine.ReadEvents(0, 500, type);

    /// <summary>Menu entries by op; like Python's dict comprehension, the last one for an op wins.</summary>
    private static Dictionary<string, (bool Available, string? Reason)> MenuOps(WorldEngine engine, int x, int y, int z = 0)
    {
        var menu = new Dictionary<string, (bool, string?)>();
        foreach (var e in Menu.Build(engine.OpenSession(), engine.Grid, engine.Registry, engine.LoadKg(Ids.Player), Ids.Player, x + 0.5, y + 0.5, z).Entries)
            menu[e.Op] = (e.Available, e.Reason);
        return menu;
    }

    private static List<string> MenuOpOrder(WorldEngine engine, int x, int y, int z = 0) =>
        Menu.Build(engine.OpenSession(), engine.Grid, engine.Registry, engine.LoadKg(Ids.Player), Ids.Player, x + 0.5, y + 0.5, z).Entries
            .Select(e => e.Op).ToList();

    [Fact]
    public void Carried_payload_cache_refreshes_after_drop_and_take()
    {
        using var engine = NewEngine();
        var session = engine.OpenSession();
        var bottle = session.AddObject(new ObjectRow { Kind = "bottle", Loc = "tile" });
        ObjectHelpers.SetHeld(bottle, Ids.Player, "right");
        session.Commit();
        engine.Reindex();

        Assert.Single(engine.GetState().Actors.Single(a => a.Id == Ids.Player).Carried);
        var dropped = engine.Submit(Ids.Player, GameAction.On("drop", new ObjectTarget(bottle.Id)));
        Assert.True(dropped.Accepted, dropped.Reason);
        Assert.Empty(dropped.Carried);
        Assert.Empty(engine.GetState().Actors.Single(a => a.Id == Ids.Player).Carried);

        var taken = engine.Submit(Ids.Player, GameAction.On("take", new ObjectTarget(bottle.Id)));
        Assert.True(taken.Accepted, taken.Reason);
        Assert.Single(taken.Carried);
    }

    private static void Ticks(WorldEngine engine, int count)
    {
        for (var i = 0; i < count; i++) engine.AdvanceTime();
    }

    [Fact]
    public void The_catalog_loads_58_unique_ops_and_build_is_handled()
    {
        Assert.Equal(58, OpCatalog.Specs.Count);
        Assert.Equal(58, OpCatalog.Specs.Select(s => s.Key).Distinct().Count());
        Assert.Equal("build", OpCatalog.HandlerFor("build").Op);
        Assert.Equal(new[] { "tile", "edge" }, OpCatalog.Specs.Single(s => s.Key == "build").Targets);
    }

    [Fact]
    public void Registering_a_handler_missing_from_the_catalog_is_rejected()
    {
        var stray = new StrayOp();
        var error = Assert.Throws<InvalidDataException>(() => OpCatalog.Register(stray));
        Assert.Contains("missing from ops.toml", error.Message, StringComparison.Ordinal);
    }

    private sealed class StrayOp : OpHandler
    {
        public override string Op => "levitate";
        public override bool Applies(ActionContext ctx, Target target) => false;
        public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => Array.Empty<GameAction>();
        public override string? Validate(ActionContext ctx, GameAction action) => null;
    }

    [Fact]
    public void The_menu_next_to_grass_offers_actions_submit_accepts()
    {
        using var engine = NewEngine();
        Place(engine, Road.X, Road.Y);
        var entries = Menu.Build(engine.OpenSession(), engine.Grid, engine.Registry, 0, Ids.Player, 124.5, 128.5, 0).Entries;
        Assert.Equal(new[] { "inspect", "dig", "build" }, entries.Select(e => e.Op));
        // Grass carries no `build_cost`, so the neighbour tile's floor is shown with its reason.
        var build = entries.Single(e => e.Op == "build");
        Assert.Equal((false, "no building material"), (build.Available, build.Reason));
        foreach (var entry in entries.Where(e => e.Op != "build")) Assert.True(engine.Submit(Ids.Player, entry.Action).Accepted, entry.Op);
    }

    [Fact]
    public void The_menu_on_own_tile_adds_wait_and_the_two_bare_edges()
    {
        using var engine = NewEngine();
        Place(engine, Grass.X, Grass.Y);
        Assert.Equal(new[] { "wait", "inspect", "dig", "build", "build", "build" }, MenuOpOrder(engine, Grass.X, Grass.Y));
        var build = Menu.Build(engine.OpenSession(), engine.Grid, engine.Registry, 0, Ids.Player, Grass.X + 0.5, Grass.Y + 0.5, 0).Entries
            .Where(e => e.Op == "build").ToList();
        Assert.Equal(new[] { "tile", "edge", "edge" }, build.Select(e => e.Action.Target!.Kind));
    }

    [Fact]
    public void The_menu_hides_or_explains_dig()
    {
        using var engine = NewEngine();
        Place(engine, Road.X, Road.Y);
        Assert.False(MenuOps(engine, 122, 128).ContainsKey("dig"));
        SetGround(engine, Grass.X, Grass.Y, 2, "concrete");
        Assert.Equal((false, "too hard to dig by hand"), MenuOps(engine, Grass.X, Grass.Y)["dig"]);
        Assert.Equal((false, "out of reach"), MenuOps(engine, 116, 128)["dig"]);
    }

    [Fact]
    public void The_dig_lifecycle_is_logged_and_survives_a_restart()
    {
        using var engine = NewEngine();
        var changed = new List<Events.SimEvent>();
        engine.Bus.Subscribe("chunk.changed", changed.Add, "test.chunk_changed");
        Place(engine, Road.X, Road.Y);
        var chunk = engine.Grid.Chunk(3, 4)!;
        var revision = chunk.Revision;

        var result = engine.Submit(Ids.Player, GameAction.On("dig", new TileTarget(124, 128, 2)));
        Assert.True(result.Accepted);
        Assert.NotNull(result.Activity);
        Assert.Equal(96, result.Activity!.EndsMinute - result.Activity.StartedMinute);
        Assert.NotNull(Actor(engine).Activity);
        Assert.Equal(new[] { "dig" }, Stored(engine, "activity.started").Select(e => e.Data["op"]!.GetValue<string>()));

        Ticks(engine, 95);
        Assert.Empty(Stored(engine, "terrain.dug"));
        Ticks(engine, 1);
        var dug = Stored(engine, "terrain.dug");
        Assert.Equal(new[] { ("topsoil", "topsoil", 1) }, dug.Select(r => (r.Data["removed"]!.GetValue<string>(), r.Data["exposed"]!.GetValue<string>(), Json.ToInt(r.Data["dug"]!))));
        Assert.Equal(new[] { "completed" }, Stored(engine, "activity.finished").Select(r => r.Data["outcome"]!.GetValue<string>()));
        Assert.Empty(Stored(engine, "chunk.changed"));
        Assert.Equal(new[] { (3, 4, revision + 1) }, changed.Select(e => (Json.ToInt(e.Data["cx"]!), Json.ToInt(e.Data["cy"]!), Json.ToInt(e.Data["revision"]!))));
        Assert.Null(Actor(engine).Activity);
        Assert.Equal((1, engine.Registry["topsoil"].Id, 1), engine.Grid.GroundAt(Grass.X, Grass.Y)!.Value);

        SqliteConnection.ClearAllPools();
        using var restarted = new WorldEngine(_path);
        restarted.EnsureWorld(0);
        Assert.Equal((1, engine.Registry["topsoil"].Id, 1), restarted.Grid.GroundAt(Grass.X, Grass.Y)!.Value);
        Assert.Equal(revision + 1, restarted.Grid.Chunk(3, 4)!.Revision);
    }

    [Fact]
    public void Digging_your_own_tile_lowers_niko_who_can_still_walk()
    {
        using var engine = NewEngine();
        Place(engine, Grass.X, Grass.Y);
        Assert.True(engine.Submit(Ids.Player, GameAction.On("dig", new TileTarget(124, 128, 2))).Accepted);
        Ticks(engine, 96);
        var niko = Actor(engine);
        Assert.Equal((1, 0), (niko.H, niko.Z));
        var moved = Stored(engine, "actor.moved");
        Assert.Equal(new[] { ("lowered", 1) }, moved.Select(r => (r.Data["mode"]!.GetValue<string>(), r.Data["to_tile"]!.Int("h"))));
        var walked = engine.Submit(Ids.Player, GameAction.Move(0, 1), 0.05);
        Assert.True(walked.Accepted);
        Assert.True(walked.Y > niko.Y);
    }

    [Fact]
    public void Strata_stay_anchored_to_the_original_ground()
    {
        using var engine = NewEngine();
        Place(engine, Grass.X, Grass.Y);
        for (var i = 0; i < 7; i++)
        {
            engine.Submit(Ids.Player, GameAction.On("dig", new TileTarget(124, 128, Actor(engine).H)));
            Ticks(engine, 96);
        }
        var exposed = Stored(engine, "terrain.dug").Select(r => r.Data["exposed"]!.GetValue<string>()).ToList();
        // Test-world strata: topsoil from 0 m, dirt from 4 m (depth 8), measured from the original.
        Assert.Equal(Enumerable.Repeat("topsoil", 6).Append("dirt"), exposed);
        Assert.Equal((-5, engine.Registry["dirt"].Id, 7), engine.Grid.GroundAt(Grass.X, Grass.Y)!.Value);
        Assert.Equal(-5, Actor(engine).H);
    }

    [Fact]
    public void Dig_is_blocked_by_a_floor_or_hollow_ground()
    {
        using var engine = NewEngine();
        Place(engine, 140, 140, 12);
        Assert.Equal((false, "floor in the way"), MenuOps(engine, 140, 140, 2)["dig"]);

        Place(engine, Road.X, Road.Y);
        var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(Grass.X, Grass.Y);
        var index = Chunk.Index(lx, ly);
        var level = ChunkLevel.Empty(cx, cy, 0);
        var flags = new byte[ChunkConst.CellCount];
        flags[index] = ChunkConst.LevelVoid;
        engine.Grid.AddLevel(level.With(flags: flags));
        var result = engine.Submit(Ids.Player, GameAction.On("dig", new TileTarget(124, 128, 2)));
        Assert.Equal((false, "hollow below"), (result.Accepted, result.Reason));
    }

    [Fact]
    public void Moving_interrupts_but_zero_moves_and_rejections_do_not()
    {
        using var engine = NewEngine();
        Place(engine, Road.X, Road.Y);
        var dig = GameAction.On("dig", new TileTarget(124, 128, 2));
        Assert.True(engine.Submit(Ids.Player, dig).Accepted);
        Assert.True(engine.Submit(Ids.Player, GameAction.Move(0, 0)).Accepted);
        Assert.NotNull(Actor(engine).Activity);
        engine.SetClock(paused: true);
        var paused = engine.Submit(Ids.Player, GameAction.Move(1, 0));
        Assert.Equal((false, "paused"), (paused.Accepted, paused.Reason));
        Assert.NotNull(Actor(engine).Activity);
        engine.SetClock(paused: false);

        Assert.True(engine.Submit(Ids.Player, GameAction.Move(0, 1)).Accepted);
        Assert.Null(Actor(engine).Activity);
        var finished = Stored(engine, "activity.finished");
        Assert.Equal(new[] { ("interrupted", "move") }, finished.Select(r => (r.Data["outcome"]!.GetValue<string>(), r.Data["reason"]!.GetValue<string>())));
        Ticks(engine, 30);
        Assert.Empty(Stored(engine, "terrain.dug"));
        Assert.Equal((2, engine.Registry["grass"].Id, 0), engine.Grid.GroundAt(Grass.X, Grass.Y)!.Value);
    }

    [Fact]
    public void An_activity_saved_midway_completes_after_a_restart()
    {
        using var engine = NewEngine();
        Place(engine, Road.X, Road.Y);
        Assert.True(engine.Submit(Ids.Player, GameAction.Wait()).Accepted);
        Ticks(engine, 5);
        SqliteConnection.ClearAllPools();
        using var restarted = new WorldEngine(_path);
        restarted.EnsureWorld(0);
        var state = restarted.GetState().Actors.First(a => a.Id == Ids.Player);
        Assert.NotNull(state.Activity);
        Assert.Equal("wait", state.Activity!.Op);
        Ticks(restarted, 10);
        Assert.Equal(new[] { "completed" }, Stored(restarted, "activity.finished").Select(r => r.Data["outcome"]!.GetValue<string>()));
    }

    [Fact]
    public void Completion_revalidates_and_can_fail()
    {
        using var engine = NewEngine();
        Place(engine, Grass.X, Grass.Y);
        SetGround(engine, 125, 128, 4);
        Assert.True(engine.Submit(Ids.Player, GameAction.On("climb", new TileTarget(125, 128, 4))).Accepted);
        SetGround(engine, 125, 128, 14);
        Ticks(engine, 1);
        var finished = Stored(engine, "activity.finished");
        Assert.Equal(new[] { ("failed", "nothing to stand on") }, finished.Select(r => (r.Data["outcome"]!.GetValue<string>(), r.Data["reason"]!.GetValue<string>())));
        Assert.Equal((124.5, 2), (Actor(engine).X, Actor(engine).H));
    }

    [Fact]
    public void Climbing_up_a_ledge_and_down_a_drop()
    {
        using var engine = NewEngine();
        Place(engine, Grass.X, Grass.Y);
        SetGround(engine, 125, 128, 4);
        SetGround(engine, 126, 128, 1);
        SetGround(engine, 124, 129, 6);
        SetGround(engine, 123, 127, 3);
        Assert.Equal((true, (string?)null), MenuOps(engine, 125, 128)["climb"]);
        Assert.Equal((false, "too high to climb"), MenuOps(engine, 124, 129)["climb"]);
        Assert.False(MenuOps(engine, 123, 127).ContainsKey("climb"));

        Assert.True(engine.Submit(Ids.Player, GameAction.On("climb", new TileTarget(125, 128, 4))).Accepted);
        Ticks(engine, 1);
        Assert.Equal((125.5, 4), (Actor(engine).X, Actor(engine).H));
        Assert.True(engine.Submit(Ids.Player, GameAction.On("climb", new TileTarget(126, 128, 1))).Accepted);
        Ticks(engine, 1);
        Assert.Equal((126.5, 1), (Actor(engine).X, Actor(engine).H));
        Assert.Equal(new[] { "climb", "climb" }, Stored(engine, "actor.moved").Select(r => r.Data["mode"]!.GetValue<string>()));
    }

    [Fact]
    public void Inspect_returns_text_and_emits_nothing()
    {
        using var engine = NewEngine();
        Place(engine, Road.X, Road.Y);
        var before = engine.ReadEvents(0, 500).Count;
        var result = engine.Submit(Ids.Player, GameAction.On("inspect", new TileTarget(124, 128, 2)));
        Assert.Equal((true, "Grass · 1 m · diggable, flammable"), (result.Accepted, result.Text));
        var farGround = engine.Grid.GroundAt(170, 128)!.Value;
        var beyond = engine.Submit(Ids.Player, GameAction.On("inspect", new TileTarget(170, 128, farGround.GroundH)));
        Assert.Equal((false, "too far to see"), (beyond.Accepted, beyond.Reason));
        Assert.Equal(before, engine.ReadEvents(0, 500).Count);
    }

    [Fact]
    public void The_menu_and_inspect_read_an_extra()
    {
        using var engine = NewEngine();
        Place(engine, Road.X, Road.Y);
        PlaceExtra(engine, "extra-001", Grass.X, Grass.Y);
        var name = engine.OpenSession().GetActor("extra-001")!.Name!;

        // Like Python's `{entry.op: entry for entry in ...}`: later entries for the same op win.
        var menu = new Dictionary<string, MenuEntry>();
        foreach (var e in Menu.Build(engine.OpenSession(), engine.Grid, engine.Registry, 0, Ids.Player, Grass.X + 0.5, Grass.Y + 0.5, 0).Entries) menu[e.Op] = e;
        Assert.True(menu["inspect"].Available);
        Assert.Equal(name, menu["inspect"].Subject);
        // Niko is on the adjacent road tile: reach ops name the Extra instead of its kind.
        Assert.StartsWith(name, menu["push"].Subject, StringComparison.Ordinal);
        Assert.StartsWith(name, menu["hit"].Subject, StringComparison.Ordinal);

        var target = new ActorTarget("extra-001");
        var standing = engine.Submit(Ids.Player, GameAction.On("inspect", target));
        Assert.Equal($"{name}. Standing.", standing.Text);

        engine.SetGoal("extra-001", new GoalSpot(Grass.X, Grass.Y, 2), "chosen");
        var walking = engine.Submit(Ids.Player, GameAction.On("inspect", target));
        Assert.Equal($"{name}. Walking.", walking.Text);

        engine.SetGoal("extra-001", null, "arrived");
        engine.Submit("extra-001", GameAction.Wait());
        var waiting = engine.Submit(Ids.Player, GameAction.On("inspect", target));
        Assert.Equal($"{name}. Waiting.", waiting.Text);
    }
}
