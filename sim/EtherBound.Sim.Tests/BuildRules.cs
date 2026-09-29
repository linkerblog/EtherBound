using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Engine.Ops;
using EtherBound.Sim.World;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

/// <summary>
/// `build`: a wall on a tile edge and a floor on a tile (Dev-036 phase 1). Niko builds out of the
/// surface the build stands on, so a tile whose material carries no `build_cost` is refused.
/// </summary>
public sealed class BuildRules : IDisposable
{
    // Seed 0: the road (x 120-123) and the grass east of it (x 124) are both at h = 2. The build
    // spot is the road tile, the only flat tile of the spawn area with nothing on either side.
    private const int TileX = 123;
    private const int TileY = 128;
    private const int GroundH = 2;
    /// <summary>Walkable and a building material, unlike brick.</summary>
    private const string Build = "concrete";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"etherbound-build-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private WorldEngine NewEngine()
    {
        var engine = new WorldEngine(_path);
        engine.EnsureWorld(0);
        return engine;
    }

    /// <summary>Reshape one tile in memory only, as a synthetic world for a test.</summary>
    private static void SetGround(WorldEngine engine, int x, int y, int h, string material)
    {
        var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(x, y);
        var chunk = engine.Grid.Chunk(cx, cy)!;
        var index = Chunk.Index(lx, ly);
        var heights = (short[])chunk.GroundH.Clone();
        var surfaces = (ushort[])chunk.SurfaceMat.Clone();
        heights[index] = (short)h;
        surfaces[index] = (ushort)engine.Registry[material].Id;
        engine.Grid.AddChunk(chunk.With(heights, surfaces));
    }

    /// <summary>Niko at a tile centre and the seeded Extras out of the way.</summary>
    private static void Place(WorldEngine engine, int x, int y, string material, int? h = null)
    {
        SetGround(engine, x, y, h ?? GroundH, material);
        var session = engine.OpenSession();
        foreach (var extra in session.Actors().Where(a => a.Id != Ids.Player)) (extra.X, extra.Y, extra.H, extra.Z) = (250.5, 250.5, 0, 0);
        var actor = session.GetActor(Ids.Player)!;
        (actor.X, actor.Y) = (x + 0.5, y + 0.5);
        actor.H = h ?? GroundH;
        actor.Z = PyMath.FloorDiv(actor.H, 6);
        actor.Activity = null;
        session.Commit();
    }

    private static void PlaceExtra(WorldEngine engine, string actorId, int x, int y, int h)
    {
        var session = engine.OpenSession();
        var extra = session.GetActor(actorId)!;
        (extra.X, extra.Y) = (x + 0.5, y + 0.5);
        (extra.H, extra.Z) = (h, PyMath.FloorDiv(h, 6));
        session.Commit();
    }

    private static List<MenuEntry> Builds(WorldEngine engine, int x, int y, int z = 0) =>
        Menu.Build(engine.OpenSession(), engine.Grid, engine.Registry, engine.LoadKg(Ids.Player), Ids.Player, x + 0.5, y + 0.5, z)
            .Entries.Where(e => e.Op == "build").ToList();

    private static MenuEntry BuildFor(WorldEngine engine, int x, int y, Target target, int z = 0) =>
        Builds(engine, x, y, z).Single(e => Json.Same(e.Action.Target!.ToJson(), target.ToJson()));

    private static (bool Accepted, string? Reason) Reject(WorldEngine engine, Target target)
    {
        var result = engine.Submit(Ids.Player, GameAction.On("build", target));
        return (result.Accepted, result.Reason);
    }

    private static void Ticks(WorldEngine engine, int count)
    {
        for (var i = 0; i < count; i++) engine.AdvanceTime();
    }

    private static (int? North, int? West) Wall(WorldEngine engine, int x, int y, int z = 0)
    {
        var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(x, y);
        var level = engine.Grid.Level(cx, cy, z) ?? ChunkLevel.Empty(cx, cy, z);
        var index = Chunk.Index(lx, ly);
        return (level.WallN[index] == 0 ? null : level.WallN[index], level.WallW[index] == 0 ? null : level.WallW[index]);
    }

    private static int? Floor(WorldEngine engine, int x, int y, int z = 0)
    {
        var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(x, y);
        var level = engine.Grid.Level(cx, cy, z);
        if (level is null) return null;
        var h = level.FloorH[Chunk.Index(lx, ly)];
        return h == ChunkConst.NoFloor ? null : h;
    }

    /// <summary>The bare-handed duration of a 1 m build: <c>ceil(30 x build_cost / 0.25)</c>.</summary>
    private static int Minutes() => (int)Math.Ceiling(BuildOp.MinutesPerCost * MaterialRegistry.Load()[Build].BuildCost!.Value
        / BuildOp.BareHandTool);

    [Fact]
    public void Build_is_offered_on_a_tile_and_on_a_bare_edge()
    {
        using var engine = NewEngine();
        Place(engine, TileX, TileY, Build);

        var entries = Builds(engine, TileX, TileY);

        Assert.Equal(new[] { "tile", "edge", "edge" }, entries.Select(e => e.Action.Target!.Kind));
        Assert.Equal(new[] { "Concrete floor", "Concrete wall", "Concrete wall" }, entries.Select(e => e.Subject));
        Assert.All(entries, e => Assert.True(e.Available, e.Reason));
    }

    [Fact]
    public void A_material_without_a_build_cost_is_not_a_building_material()
    {
        using var engine = NewEngine();
        Place(engine, TileX, TileY, "grass");

        // Grass carries no `build_cost`, so the tile offers `build` with its reason and nothing else.
        foreach (var target in new Target[] { new TileTarget(TileX, TileY, GroundH), new EdgeTarget(TileX, TileY, 0, "north") })
        {
            var entry = BuildFor(engine, TileX, TileY, target);
            Assert.Equal((false, "no building material"), (entry.Available, entry.Reason));
            Assert.Equal((false, "no building material"), Reject(engine, target));
        }
    }

    [Fact]
    public void Build_respects_reach()
    {
        using var engine = NewEngine();
        Place(engine, TileX, TileY, Build);
        var session = engine.OpenSession();
        var actor = session.GetActor(Ids.Player)!;
        actor.X = TileX - 3.5;
        session.Commit();

        // Two tiles north of Niko and beyond his reach.
        Assert.Equal((false, "out of reach"), Reject(engine, new EdgeTarget(TileX, TileY, 0, "north")));
        Assert.Equal((false, "out of reach"), Reject(engine, new TileTarget(TileX, TileY, GroundH + 6)));
    }

    [Fact]
    public void Build_refuses_to_cover_a_body_or_an_object()
    {
        using var engine = NewEngine();
        Place(engine, TileX, TileY, Build);
        PlaceExtra(engine, "extra-001", TileX, TileY - 1, GroundH);

        // The Extra's 2 m body sits in the wall's six half-metres.
        Assert.Equal((false, "someone is in the way"), Reject(engine, new EdgeTarget(TileX, TileY, 0, "north")));

        var session = engine.OpenSession();
        var chest = session.AddObject(new ObjectRow { Kind = "chest", Loc = "tile" });
        ObjectHelpers.SetTile(chest, TileX, TileY, GroundH - 1);
        session.Commit();
        engine.Reindex();

        // A chest resting one half-metre below fills the volume the floor would take.
        Assert.Equal((false, "no headroom"), Reject(engine, new TileTarget(TileX, TileY, GroundH)));
    }

    [Fact]
    public void South_and_east_normalize_to_the_canonical_edge()
    {
        using var engine = NewEngine();
        Place(engine, TileX, TileY, Build);
        // South and east land on the neighbouring tile, so that tile's surface is the material too.
        SetGround(engine, TileX, TileY + 1, GroundH, Build);
        SetGround(engine, TileX + 1, TileY, GroundH, Build);

        Assert.True(engine.Submit(Ids.Player, GameAction.On("build", new EdgeTarget(TileX, TileY, 0, "south"))).Accepted);
        Ticks(engine, Minutes());
        Assert.True(engine.Submit(Ids.Player, GameAction.On("build", new EdgeTarget(TileX, TileY, 0, "east"))).Accepted);
        Ticks(engine, Minutes());

        // South is the north edge of y+1 and east the west edge of x+1, so nothing is stored twice.
        Assert.Equal(engine.Registry[Build].Id, Wall(engine, TileX, TileY + 1).North);
        Assert.Equal(engine.Registry[Build].Id, Wall(engine, TileX + 1, TileY).West);
        Assert.Null(Wall(engine, TileX, TileY).North);
        Assert.Null(Wall(engine, TileX, TileY).West);
    }

    [Fact]
    public void A_second_build_on_the_same_wall_is_refused()
    {
        using var engine = NewEngine();
        Place(engine, TileX, TileY, Build);
        var edge = new EdgeTarget(TileX, TileY, 0, "north");
        Assert.True(engine.Submit(Ids.Player, GameAction.On("build", edge)).Accepted);
        Ticks(engine, Minutes());

        Assert.Equal((false, "already built"), Reject(engine, edge));
    }

    [Fact]
    public void Duration_comes_from_build_cost_the_slot_length_and_the_best_held_tool()
    {
        var catalog = ObjectCatalog.FromText(DataFiles.ReadText("objects.toml") +
            "\n[[kinds]]\nkey = \"trowel\"\nname = \"Trowel\"\nmaterial = \"metal\"\nmass = 1.0\nbulk = 4.0\nheight = 0\n\n" +
            "[kinds.tool]\nbuild = 1.0\n", MaterialRegistry.Load());
        using var engine = new WorldEngine(_path, catalog: catalog);
        engine.EnsureWorld(0);
        Place(engine, TileX, TileY, Build);
        var bare = engine.Submit(Ids.Player, GameAction.On("build", new EdgeTarget(TileX, TileY, 0, "north")));

        // ceil(30 x concrete build_cost 2.5 x 1 m / 0.25 bare hands).
        Assert.Equal(300, bare.Activity!.EndsMinute - bare.Activity.StartedMinute);
        Ticks(engine, 300);

        var session = engine.OpenSession();
        var trowel = session.AddObject(new ObjectRow { Kind = "trowel", Loc = "held" });
        ObjectHelpers.SetHeld(trowel, Ids.Player, "right");
        session.Commit();
        engine.Reindex();

        // The same 1 m wall with the best held tool: ceil(30 x 2.5 x 1 / 1.0).
        var tooled = engine.Submit(Ids.Player, GameAction.On("build", new EdgeTarget(TileX, TileY, 0, "west")));

        Assert.Equal(75, tooled.Activity!.EndsMinute - tooled.Activity.StartedMinute);
    }

    [Fact]
    public void A_completed_build_writes_the_wall_and_logs_it_in_one_commit()
    {
        using var engine = NewEngine();
        var changed = new List<Events.SimEvent>();
        engine.Bus.Subscribe("chunk.changed", changed.Add, "test.chunk_changed");
        Place(engine, TileX, TileY, Build);
        var revision = engine.Grid.Chunk(3, 4)!.Revision;
        var edge = new EdgeTarget(TileX, TileY, 0, "north");

        var started = engine.Submit(Ids.Player, GameAction.On("build", edge));

        Assert.True(started.Accepted, started.Reason);
        Assert.NotNull(started.Activity);
        Assert.Equal(new[] { "build" }, engine.ReadEvents(0, 500, "activity.started").Select(e => e.Data["op"]!.GetValue<string>()));
        Ticks(engine, Minutes() - 1);
        Assert.Null(Wall(engine, TileX, TileY).North);
        Ticks(engine, 1);

        var built = engine.ReadEvents(0, 500, "wall.built");
        Assert.Equal(new[] { ("wall", Build, 0) }, built.Select(e => (e.Data["kind"]!.GetValue<string>(),
            e.Data["material"]!.GetValue<string>(), Json.ToInt(e.Data["z"]!))));
        Assert.Equal(new[] { "completed" }, engine.ReadEvents(0, 500, "activity.finished").Select(e => e.Data["outcome"]!.GetValue<string>()));
        Assert.Equal(engine.Registry[Build].Id, Wall(engine, TileX, TileY).North);
        Assert.Equal((3, 4, revision + 1), (Json.ToInt(changed[0].Data["cx"]!), Json.ToInt(changed[0].Data["cy"]!),
            Json.ToInt(changed[0].Data["revision"]!)));

        SqliteConnection.ClearAllPools();
        using var reopened = new WorldEngine(_path);
        reopened.EnsureWorld(0);
        Assert.Equal(engine.Registry[Build].Id, Wall(reopened, TileX, TileY).North);
    }

    [Fact]
    public void An_interrupted_build_leaves_no_wall_and_no_event()
    {
        using var engine = NewEngine();
        Place(engine, TileX, TileY, Build);
        Assert.True(engine.Submit(Ids.Player, GameAction.On("build", new EdgeTarget(TileX, TileY, 0, "north"))).Accepted);
        Ticks(engine, 30);

        Assert.True(engine.Submit(Ids.Player, GameAction.Move(0, 1)).Accepted);
        Ticks(engine, Minutes());

        Assert.Null(Wall(engine, TileX, TileY).North);
        Assert.Empty(engine.ReadEvents(0, 500, "wall.built"));
        Assert.Equal(new[] { ("interrupted", "move") }, engine.ReadEvents(0, 500, "activity.finished")
            .Select(e => (e.Data["outcome"]!.GetValue<string>(), e.Data["reason"]!.GetValue<string>())));
    }

    [Fact]
    public void A_floor_is_written_and_stands_on()
    {
        using var engine = NewEngine();
        Place(engine, TileX, TileY, Build);
        var tile = new TileTarget(TileX, TileY, GroundH);

        Assert.Equal("Concrete floor", BuildFor(engine, TileX, TileY, tile).Subject);
        Assert.True(engine.Submit(Ids.Player, GameAction.On("build", tile)).Accepted);
        Ticks(engine, Minutes());

        Assert.Equal(GroundH, Floor(engine, TileX, TileY));
        Assert.Contains(engine.Grid.StandingSurfaces(TileX, TileY),
            s => s.H == GroundH && s.MaterialId == engine.Registry[Build].Id);
        Assert.Equal((false, "already built"), Reject(engine, tile));
    }

    [Fact]
    public void Break_opens_a_built_wall_and_stores_its_partial_damage()
    {
        using var engine = NewEngine();
        Place(engine, TileX, TileY, Build);
        var edge = new EdgeTarget(TileX, TileY, 0, "north");
        Assert.True(engine.Submit(Ids.Player, GameAction.On("build", edge)).Accepted);
        Ticks(engine, Minutes());
        var revision = engine.Grid.Chunk(3, 4)!.Revision;
        var session = engine.OpenSession();
        var hammer = session.Objects().Single(o => o.Kind == "sledgehammer");
        ObjectHelpers.SetHeld(hammer, Ids.Player, "right");
        session.Commit();
        engine.Reindex();

        // Concrete spans 350 J x 6 cells, so a bare hand never covers it: the partial damage lands in
        // the sparse wall_slot row and the wall only goes on a strike that covers the rest.
        Assert.True(engine.Submit(Ids.Player, GameAction.Strike("break", edge, null)).Accepted);
        Assert.Equal(engine.Registry[Build].Id, Wall(engine, TileX, TileY).North);
        Assert.Single(engine.WallRows());

        var tool = new ObjectTarget(hammer.Id);
        var strikes = 1;
        while (Wall(engine, TileX, TileY).North is not null && strikes++ < 20)
            Assert.True(engine.Submit(Ids.Player, GameAction.Strike("break", edge, tool)).Accepted);

        Assert.Null(Wall(engine, TileX, TileY).North);
        Assert.Empty(engine.WallRows());
        Assert.False(engine.Grid.WallBetween(TileX, TileY - 1, TileX, TileY, GroundH));
        Assert.True(engine.Grid.Chunk(3, 4)!.Revision > revision);
        var resolved = engine.ReadEvents(0, 500, "physics.resolved");
        Assert.All(resolved, e => Assert.Contains("absorbed_j", e.Data["damage"]!.ToJsonString()));
    }

    [Fact]
    public void A_built_wall_blocks_the_step_and_the_navigation_revision_moves()
    {
        using var engine = NewEngine();
        Place(engine, TileX, TileY, Build);
        Assert.True(engine.Grid.CanStep(TileX, TileY - 1, TileX, TileY, GroundH));
        var before = engine.Grid.NavigationRevision;

        Assert.True(engine.Submit(Ids.Player, GameAction.On("build", new EdgeTarget(TileX, TileY, 0, "north"))).Accepted);
        Ticks(engine, Minutes());

        Assert.True(engine.Grid.WallBetween(TileX, TileY - 1, TileX, TileY, GroundH));
        Assert.False(engine.Grid.CanStep(TileX, TileY - 1, TileX, TileY, GroundH));
        // One build drops the A* caches once, through the existing revision: the level write, the
        // chunk bump and its tile-object reindex are the three grid edits behind it.
        Assert.Equal(before + 3, engine.Grid.NavigationRevision);
    }
}
