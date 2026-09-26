using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;

namespace EtherBound.Sim.Tests;

/// <summary>The debug lab generator (ported from <c>test_worldgen_lab.py</c>).</summary>
public class LabGenerator
{
    private const long Seed = 7;
    private static readonly MaterialRegistry Registry = MaterialRegistry.Load();
    private static readonly HashSet<(int, int)> FeatureChunks = new() { (1, 1), (1, 2), (2, 1), (2, 2) };

    private static WorldGrid Grid(long seed = Seed, LabOptions? options = null)
    {
        var world = Lab.Generate(seed, options ?? new LabOptions(), Registry);
        return new WorldGrid(world.Chunks.Values, world.Levels.Values.OrderBy(l => (l.Cx, l.Cy, l.Z)), Registry);
    }

    private static Spot? Goal(WorldGrid grid, int x, int y)
    {
        var surfaces = grid.StandingSurfaces(x, y);
        return surfaces.Count == 0 ? null : new Spot(x, y, surfaces.Min(s => s.H));
    }

    [Fact]
    public void Deterministic_and_four_by_four()
    {
        var first = Lab.Generate(Seed, new LabOptions(), Registry);
        Assert.Equal(16, first.Chunks.Count);
        Assert.Equal(Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 4).Select(y => (x, y))).ToHashSet(), first.Chunks.Keys.ToHashSet());
        Assert.Equal(first.BlobBytes(), Lab.Generate(Seed, new LabOptions(), Registry).BlobBytes());
        // The fixed fixture ignores the seed; a seed-driven feature does not.
        var relief = new LabOptions("relief");
        Assert.Equal(Lab.Generate(Seed, relief, Registry).BlobBytes(), Lab.Generate(Seed, relief, Registry).BlobBytes());
        Assert.NotEqual(Lab.Generate(Seed, relief, Registry).BlobBytes(), Lab.Generate(Seed + 1, relief, Registry).BlobBytes());
    }

    [Fact]
    public void Every_spawn_bay_is_standable()
    {
        var world = Lab.Generate(Seed, new LabOptions(), Registry);
        Assert.Equal(Lab.BayKeys.ToHashSet(), Lab.Bays.Select(b => b.Key).ToHashSet());
        foreach (var key in Lab.BayKeys)
        {
            var (sx, sy, sh) = Lab.Spawn(Seed, new LabOptions(SpawnBay: key));
            var grid = new WorldGrid(world.Chunks.Values, world.Levels.Values.OrderBy(l => (l.Cx, l.Cy, l.Z)), Registry);
            Assert.True(grid.StandingSurfaces((int)sx, (int)sy).Any(s => s.H == sh), key);
        }
    }

    [Fact]
    public void Structure_walls_enclose_the_floor_without_corner_stubs()
    {
        var world = Lab.Generate(Seed, new LabOptions(), Registry);
        const int x0 = 17, y0 = 45, x1 = x0 + 9, y1 = y0 + 7;
        for (var z = 0; z < 3; z++)
        {
            var floors = world.Levels.Values.Where(l => l.Z == z)
                .SelectMany(l => Enumerable.Range(0, ChunkConst.CellCount)
                    .Where(i => l.FloorH[i] != ChunkConst.NoFloor)
                    .Select(i => (X: l.Cx * ChunkConst.Size + i % ChunkConst.Size, Y: l.Cy * ChunkConst.Size + i / ChunkConst.Size)))
                .Where(p => p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1).ToHashSet();
            var expected = Enumerable.Range(x0 + 1, x1 - x0 - 1).SelectMany(x => Enumerable.Range(y0 + 1, y1 - y0 - 1).Select(y => (x, y))).ToHashSet();
            if (z is 1 or 2)
            {
                var stairY = y0 + (z == 1 ? 1 : 2);
                for (var x = x0 + 1; x < x0 + 4; x++) expected.Remove((x, stairY));
            }
            Assert.Equal(expected, floors);

            HashSet<(int, int)> Edges(Func<ChunkLevel, ushort[]> select) => world.Levels.Values.Where(l => l.Z == z)
                .SelectMany(l => Enumerable.Range(0, ChunkConst.CellCount).Where(i => select(l)[i] != 0)
                    .Select(i => (X: l.Cx * ChunkConst.Size + i % ChunkConst.Size, Y: l.Cy * ChunkConst.Size + i / ChunkConst.Size)))
                .Where(p => p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1).ToHashSet();
            var north = Enumerable.Range(x0 + 1, x1 - x0 - 1).SelectMany(x => new[] { (x, y0 + 1), (x, y1) }).ToHashSet();
            var west = Enumerable.Range(y0 + 1, y1 - y0 - 1).SelectMany(y => new[] { (x0 + 1, y), (x1, y) }).ToHashSet();
            Assert.Equal(north, Edges(l => l.WallN));
            Assert.Equal(west, Edges(l => l.WallW));
        }
        var doorway = world.Levels.Values.First(l => l.Z == 0 && l.Cx == 0 && l.Cy == 1);
        var doorIndex = Chunk.Index(x0 + 1, (y0 + y1) / 2 - ChunkConst.Size);
        Assert.True((doorway.EdgeFlags[doorIndex] & ChunkConst.EdgeWDoorway) != 0);
        foreach (var z in new[] { 1, 2 })
        {
            var level = world.Levels.Values.First(l => l.Z == z && l.Cx == 0 && l.Cy == 1);
            var windows = Enumerable.Range(0, ChunkConst.CellCount)
                .Where(i => (level.EdgeFlags[i] & ChunkConst.EdgeNWindow) != 0)
                .Select(i => (X: i % 32, Y: level.Cy * 32 + i / 32)).ToHashSet();
            Assert.Equal(Enumerable.Range(x0 + 1, x1 - x0 - 1).Where(x => x % 3 == 0).Select(x => (x, y0 + 1)).ToHashSet(), windows);
        }
    }

    [Fact]
    public void Navigation_reaches_every_bay_from_the_default_spawn()
    {
        var grid = Grid();
        var start = new Spot(61, 41, 2);
        foreach (var key in Lab.BayKeys)
        {
            var (x0, y0, _, _) = Lab.BayRects[key];
            var goal = Goal(grid, x0 + 1, y0 + 1)!.Value;
            var path = Nav.FindPath(grid, start, goal);
            Assert.NotNull(path);
            Assert.Equal(start, path![0]);
            Assert.Equal(goal, path[^1]);
        }
    }

    [Fact]
    public void Materials_bay_has_a_patch_per_registered_material()
    {
        var grid = Grid();
        var (x0, y0, _, _) = Lab.BayRects["materials"];
        for (var index = 0; index < Registry.Materials.Count; index++)
        {
            var material = Registry.Materials[index];
            int col = index % 6, row = index / 6;
            int px = x0 + col * 6, py = y0 + row * 6;
            var patch = grid.GroundAt(px, py)!.Value;
            var block = grid.GroundAt(px + 4, py)!.Value;
            Assert.True(patch.GroundH == 2 && patch.SurfaceMat == material.Id, material.Key);
            Assert.True(block.GroundH == 4 && block.SurfaceMat == material.Id, material.Key);
        }
    }

    [Fact]
    public void Objects_bay_has_every_catalog_kind()
    {
        var catalog = ObjectCatalog.Load(Registry);
        var world = Lab.Generate(Seed, new LabOptions(), Registry);
        var (x0, y0, x1, y1) = Lab.BayRects["objects"];
        var kinds = world.Objects.Where(o => o.X >= x0 && o.X <= x1 && o.Y >= y0 && o.Y <= y1).Select(o => o.Kind).ToHashSet();
        Assert.Equal(catalog.Keys.ToHashSet(), kinds);
    }

    [Fact]
    public void Relief_amplitude_changes_only_the_feature_bay_chunks()
    {
        var small = Lab.Generate(Seed, new LabOptions("relief", ReliefValue: new ReliefOptions(Amplitude: 8)), Registry);
        var large = Lab.Generate(Seed, new LabOptions("relief", ReliefValue: new ReliefOptions(Amplitude: 20)), Registry);
        var changed = small.Chunks.Keys.Where(k => !small.Chunks[k].GroundBlob.SequenceEqual(large.Chunks[k].GroundBlob)).ToHashSet();
        Assert.NotEmpty(changed);
        Assert.True(changed.IsSubsetOf(FeatureChunks));
    }

    [Fact]
    public void Relief_blends_to_flat_at_the_bay_border()
    {
        var world = Lab.Generate(Seed, new LabOptions("relief", ReliefValue: new ReliefOptions(Amplitude: 20)), Registry);
        var (x0, y0, x1, y1) = Lab.BayRects["feature"];
        var grid = new WorldGrid(world.Chunks.Values, world.Levels.Values.OrderBy(l => (l.Cx, l.Cy, l.Z)), Registry);
        for (var x = x0; x <= x1; x++)
        foreach (var y in new[] { y0, y1 })
            Assert.Equal(2, grid.GroundAt(x, y)!.Value.GroundH);
        for (var y = y0; y <= y1; y++)
        foreach (var x in new[] { x0, x1 })
            Assert.Equal(2, grid.GroundAt(x, y)!.Value.GroundH);
    }
}

/// <summary>Object kinds and the volumes they claim (ported from <c>test_objects.py</c>).</summary>
public class ObjectKinds
{
    private static readonly MaterialRegistry Registry = MaterialRegistry.Load();

    private static ObjectCatalog Load(string body) => ObjectCatalog.FromText(body, Registry);

    private const string Valid = "\n[[kinds]]\nkey = \"widget\"\nname = \"Widget\"\nmaterial = \"wood\"\nmass = 1.0\nbulk = 2.0\nheight = 0\n";

    [Fact]
    public void The_v1_kinds_load()
    {
        var catalog = ObjectCatalog.Load(Registry);
        Assert.Equal(new[] { "chest", "barrel", "table", "chair", "shelf", "backpack", "shovel", "sledgehammer", "bottle", "apple", "rubble" }, catalog.Keys);
        var chest = catalog["chest"];
        Assert.Equal((15.0, 120.0, 2), (chest.Mass, chest.Bulk, chest.Height));
        Assert.True(chest.Solid && chest.Surface && chest.Openable);
        Assert.Equal(100.0, chest.Container!.Capacity);
        Assert.False(catalog["shelf"].Openable);
        Assert.Equal(new Wearable("back"), catalog["backpack"].Wearable);
        Assert.Equal(new Tool(1.0, 15.0), catalog["shovel"].Tool);
        Assert.Equal(new Tool(null, 15.0), catalog["sledgehammer"].Tool);
        Assert.True(catalog["apple"].Stackable && !catalog["apple"].Solid);
        Assert.True(catalog["rubble"].Material == "rock" && catalog["rubble"].Stackable);
    }

    public static IEnumerable<object[]> BrokenKinds()
    {
        yield return new object[] { Valid + Valid, "unique" };
        yield return new object[] { Valid.Replace("material = \"wood\"", "material = \"unobtanium\""), "unregistered material" };
        yield return new object[] { Valid.Replace("height = 0", "height = 1"), "height must be positive" };
        yield return new object[] { Valid.Replace("height = 0", "height = 0\nsurface = true"), "surface requires solid" };
        yield return new object[]
        {
            Valid.Replace("material = \"wood\"", "material = \"food\"").Replace("height = 0", "height = 2\nsolid = true\nsurface = true"),
            "surface material must be walkable",
        };
        yield return new object[] { Valid + "stackable = true\n[kinds.container]\ncapacity = 1.0\n", "stackable excludes" };
        yield return new object[] { Valid + "[kinds.openable]\n", "openable requires container" };
        yield return new object[] { Valid + "[kinds.wearable]\nslot = \"head\"\n", "wearable slot" };
        yield return new object[] { Valid + "[kinds.tool]\ndig = 0.0\n", "tool.dig must be positive" };
        yield return new object[] { Valid + "[kinds.tool]\n", "tool requires a capability" };
        yield return new object[] { Valid + "[kinds.tool]\nstrike_speed_m_s = 0.0\n", "tool.strike_speed_m_s must be positive" };
        yield return new object[] { Valid + "colour = \"red\"\n", "unknown keys" };
        yield return new object[] { Valid + "[kinds.container]\ncapacity = 1.0\nwrong = 1.0\n", "unknown container keys" };
    }

    [Theory]
    [MemberData(nameof(BrokenKinds))]
    public void Broken_kinds_are_rejected(string body, string match)
    {
        var error = Assert.ThrowsAny<Exception>(() => Load(body));
        Assert.Contains(match, error.Message, StringComparison.Ordinal);
    }

    private static readonly (int X, int Y) ChestTop = (124, 127); // Test-world closed chest, resting at h = 2; its top is h = 4.
    private static readonly (int X, int Y) Stack = (126, 127); // Two chests stacked on ground h = 1; the upper top is h = 5.
    private static readonly (int X, int Y) Neighbour = (125, 127); // Grass at h = 2, next to the stack.

    private static Engine.WorldEngine Engine()
    {
        var engine = new Engine.WorldEngine();
        engine.EnsureWorld(0);
        return engine;
    }

    private static void Place(Engine.WorldEngine engine, int x, int y, int? h = null)
    {
        var session = engine.OpenSession();
        var actor = session.GetActor(Ids.Player)!;
        (actor.X, actor.Y) = (x + 0.5, y + 0.5);
        actor.H = h ?? engine.Grid.GroundAt(x, y)!.Value.GroundH;
        actor.Z = PyMath.FloorDiv(actor.H, 6);
        session.Commit();
    }

    [Fact]
    public void The_chest_top_is_standable_and_the_chest_is_solid()
    {
        using var engine = Engine();
        Assert.Equal(new[] { "chest" }, engine.Grid.ObjectsAt(ChestTop.X, ChestTop.Y).Select(o => o.Kind));
        Assert.True(engine.Grid.SolidAt(ChestTop.X, ChestTop.Y, 3) && engine.Grid.SolidAt(ChestTop.X, ChestTop.Y, 4));
        Assert.False(engine.Grid.SolidAt(ChestTop.X, ChestTop.Y, 5));
        Assert.Equal(new[] { 4 }, engine.Grid.StandingSurfaces(ChestTop.X, ChestTop.Y).Select(s => s.H));
        var resting = engine.Grid.RestingSurfaces(ChestTop.X, ChestTop.Y).Select(s => s.H).ToList();
        Assert.Contains(2, resting);
        Assert.Contains(4, resting);
    }

    [Fact]
    public void Navigation_goes_around_a_chest()
    {
        using var engine = Engine();
        var path = Nav.FindPath(engine.Grid, new Spot(123, 127, 2), new Spot(125, 127, 2));
        Assert.NotNull(path);
        Assert.DoesNotContain(new Spot(124, 127, 2), path!);
    }

    [Fact]
    public void Climbing_onto_a_chest_and_back_down()
    {
        using var engine = Engine();
        Place(engine, 124, 128, 2);
        Assert.True(engine.Submit(Ids.Player, GameAction.On("climb", new TileTarget(124, 127, 4))).Accepted);
        engine.AdvanceTime();
        Assert.Equal(4, engine.OpenSession().GetActor(Ids.Player)!.H);
        Assert.True(engine.Submit(Ids.Player, GameAction.On("climb", new TileTarget(124, 128, 2))).Accepted);
        engine.AdvanceTime();
        Assert.Equal(2, engine.OpenSession().GetActor(Ids.Player)!.H);
    }

    [Fact]
    public void Stacked_chests_block_walking_and_climb_from_a_neighbour()
    {
        using var engine = Engine();
        // The lower lid is covered by the upper chest, so only the upper top is standable.
        Assert.Equal(new[] { 5 }, engine.Grid.StandingSurfaces(Stack.X, Stack.Y).Select(s => s.H));
        Place(engine, Neighbour.X, Neighbour.Y, 2);
        Assert.True(engine.Submit(Ids.Player, GameAction.On("climb", new TileTarget(Stack.X, Stack.Y, 5))).Accepted);
        engine.AdvanceTime();
        Assert.Equal(5, engine.OpenSession().GetActor(Ids.Player)!.H);
        Assert.True(engine.Submit(Ids.Player, GameAction.On("climb", new TileTarget(Neighbour.X, Neighbour.Y, 2))).Accepted);
        engine.AdvanceTime();
        Assert.Equal(2, engine.OpenSession().GetActor(Ids.Player)!.H);
    }

    [Fact]
    public void Load_multiplier_and_move_in_world()
    {
        using var engine = Engine();
        Assert.Equal(1.0, Movement.LoadMultiplier(10.0));
        Assert.Equal(0.5, Movement.LoadMultiplier(40.0), 3);
        Assert.Equal(0.5, Movement.LoadMultiplier(70.0));
        var (fx, fy, _) = Movement.MoveInWorld(127.5, 128.5, 1, 1.0, 0.0, 2.0, engine.Grid, 0.0);
        var (lx, _, _) = Movement.MoveInWorld(127.5, 128.5, 1, 1.0, 0.0, 2.0, engine.Grid, 40.0);
        Assert.True(fx > lx);
        Assert.Equal((fx - 127.5) * 0.5, lx - 127.5, 1);
    }
}

/// <summary>Population placement (ported from <c>test_population.py</c>).</summary>
public class PopulationRules
{
    private const long Seed = 0;
    private const int MaxExpansions = 3000;
    private static readonly MaterialRegistry Registry = MaterialRegistry.Load();

    private static (WorldGrid Grid, (double X, double Y, int H) Spawn) World(string generator)
    {
        var catalog = ObjectCatalog.Load(Registry);
        var spec = Generators.All[generator];
        var generated = spec.Generate(Seed, spec.Defaults, Registry);
        var grid = new WorldGrid(generated.Chunks.Values, generated.Levels.Values.OrderBy(l => (l.Cx, l.Cy, l.Z)), Registry, catalog: catalog);
        return (grid, spec.Spawn(Seed, spec.Defaults));
    }

    [Theory]
    [InlineData("test")]
    [InlineData("lab")]
    public void Population_is_seeded_reachable_and_spaced(string generator)
    {
        var (grid, spawn) = World(generator);
        var first = Population.Populate(grid, Registry, spawn, Seed);
        var second = Population.Populate(grid, Registry, spawn, Seed);
        Assert.Equal(first, second);
        Assert.Equal(Population.ExtraCount, first.Count);
        Assert.Equal(Population.ExtraCount, first.Select(e => e.Name).ToHashSet().Count);
        Assert.Equal(Population.ExtraCount, first.Select(e => e.Id).ToHashSet().Count);

        var start = new Spot(PyMath.Floor(spawn.X), PyMath.Floor(spawn.Y), spawn.H);
        foreach (var extra in first)
        {
            Assert.NotEqual((start.X, start.Y, start.H), (extra.AnchorX, extra.AnchorY, extra.AnchorH));
            Assert.True(PyMath.Hypot(extra.X - spawn.X, extra.Y - spawn.Y) <= Population.PlacementRadiusM);
            var ground = grid.GroundAt(extra.AnchorX, extra.AnchorY)!.Value;
            Assert.Equal(extra.AnchorH, ground.GroundH);
            Assert.True(Registry.Get(ground.SurfaceMat)!.Walkable);
            Assert.Contains(grid.StandingSurfaces(extra.AnchorX, extra.AnchorY), s => s.H == extra.AnchorH);
            Assert.NotNull(Nav.FindPath(grid, start, new Spot(extra.AnchorX, extra.AnchorY, extra.AnchorH), MaxExpansions));
        }
        for (var i = 0; i < first.Count; i++)
        for (var j = i + 1; j < first.Count; j++)
            Assert.True(PyMath.Hypot(first[i].AnchorX - first[j].AnchorX, first[i].AnchorY - first[j].AnchorY) >= 2.0);
    }

    [Fact]
    public void Ensure_world_adds_extras_once_to_a_save_without_them()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etherbound-pop-{Guid.NewGuid():N}.db");
        try
        {
            using var engine = new Engine.WorldEngine(path);
            engine.EnsureWorld(3);
            var session = engine.OpenSession();
            foreach (var extra in session.Actors().Where(a => a.Kind == "extra")) session.DeleteActor(extra);
            session.Commit();
            var marker = engine.ReadEvents(0, 500).LastOrDefault()?.Seq ?? 0;

            engine.EnsureWorld(3);

            var extras = engine.GetState().Actors.Where(a => a.Kind == "extra").ToList();
            Assert.Equal(Population.ExtraCount, extras.Count);
            var fresh = engine.ReadEvents(marker, 500);
            Assert.Equal(Enumerable.Repeat("actor.spawned", Population.ExtraCount), fresh.Select(e => e.Type));
            Assert.Equal(new[] { "created" }, fresh.Select(e => e.Data["reason"]!.GetValue<string>()).Distinct());
            Assert.All(fresh, e => Assert.NotEqual(Ids.Player, e.ActorId));

            marker = engine.ReadEvents(0, 500).LastOrDefault()?.Seq ?? 0;
            engine.EnsureWorld(3);
            Assert.Empty(engine.ReadEvents(marker, 500));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
