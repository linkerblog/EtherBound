using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

/// <summary>Grid, standing, wall and navigation rules (ported from <c>test_world.py</c> and <c>test_world_rules.py</c>).</summary>
public class GridRules
{
    private static readonly MaterialRegistry Registry = MaterialRegistry.Load();
    private static int Id(string key) => Registry[key].Id;

    private static short[] Heights(int value = 0) => Enumerable.Repeat((short)value, ChunkConst.CellCount).ToArray();
    private static ushort[] Mats(string key) => Enumerable.Repeat((ushort)Id(key), ChunkConst.CellCount).ToArray();

    private static ChunkLevel Level(int cx, int z, Action<ChunkLevel>? edit = null)
    {
        var level = ChunkLevel.Empty(cx, 0, z);
        edit?.Invoke(level);
        return level;
    }

    [Fact]
    public void Blobs_round_trip()
    {
        var values = Enumerable.Range(0, ChunkConst.CellCount).Select(i => (short)(i - 512)).ToArray();
        Assert.Equal(values, Blobs.DecodeInt16(Blobs.Encode(values)));
    }

    [Fact]
    public void Material_ids_append_to_an_existing_mapping()
    {
        const string toml = "[[materials]]\nkey = \"new\"\nname = \"New\"\ncolor = \"#fff\"\nwalkable = true\nwalk_cost = 1\nsolid = true\n" +
            "blocks_sight = false\ndiggable = true\ndig_cost = 1\nflammable = false\ndensity = 1\nresistance = 1\nliquid = false\ntags = []\n";
        Assert.Equal(8, MaterialRegistry.FromText(toml, new Dictionary<string, int> { ["old"] = 7 })["new"].Id);
        Assert.Equal(12, MaterialRegistry.FromText(toml, new Dictionary<string, int> { ["old"] = 7, ["new"] = 12 })["new"].Id);
    }

    [Fact]
    public void Half_a_metre_is_a_step_and_one_metre_is_not()
    {
        var grid = new WorldGrid(new[] { new Chunk(0, 0, Heights(), Mats("grass")) }, registry: Registry);
        var h = Heights();
        h[1] = 1;
        grid.AddChunk(new Chunk(0, 0, h, Mats("grass")));
        Assert.True(grid.CanStep(0, 0, 1, 0));
        h = Heights();
        h[1] = 2;
        grid.AddChunk(new Chunk(0, 0, h, Mats("grass")));
        Assert.False(grid.CanStep(0, 0, 1, 0));
    }

    [Fact]
    public void A_wall_blocks_and_a_doorway_opens()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 0, Id("grass")) }, registry: Registry);
        var level = Level(0, 0, l => l.WallW[1] = (ushort)Id("brick"));
        grid.AddLevel(level);
        Assert.True(grid.WallBetween(0, 0, 1, 0, 0));
        var flags = new byte[ChunkConst.CellCount];
        flags[1] = ChunkConst.EdgeWDoorway;
        grid.AddLevel(level.With(edgeFlags: flags));
        Assert.False(grid.WallBetween(0, 0, 1, 0, 0));
    }

    [Fact]
    public void A_window_blocks_bodies()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 0, Id("grass")) }, registry: Registry);
        grid.AddLevel(Level(0, 0, l =>
        {
            l.WallW[1] = (ushort)Id("brick");
            l.EdgeFlags[1] = ChunkConst.EdgeWWindow;
        }));
        Assert.True(grid.WallBetween(0, 0, 1, 0, 0));
        Assert.False(grid.CanStep(0, 0, 1, 0, 0));
    }

    [Fact]
    public void A_chunk_border_wall_blocks_from_both_sides()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 0, Id("grass")), Chunk.Flat(1, 0, 0, Id("grass")) }, registry: Registry);
        grid.AddLevel(Level(1, 0, l => l.WallW[0] = (ushort)Id("brick")));
        Assert.True(grid.WallBetween(31, 0, 32, 0, 0));
        Assert.True(grid.WallBetween(32, 0, 31, 0, 0));
    }

    [Fact]
    public void A_ground_floor_wall_does_not_block_the_floor_above()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 12, Id("grass")) }, registry: Registry);
        grid.AddLevel(Level(0, 1, l =>
        {
            Array.Fill(l.FloorH, (short)12);
            Array.Fill(l.FloorMat, (ushort)Id("grass"));
            l.WallW[1] = (ushort)Id("brick");
        }));
        Assert.True(grid.WallBetween(0, 0, 1, 0, 12));
        Assert.False(grid.WallBetween(0, 0, 1, 0, 18));
    }

    [Fact]
    public void A_low_ceiling_blocks_and_a_void_unblocks()
    {
        // Ground at h=12 buries a basement floor at h=6 unless the band is excavated.
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 12, Id("grass")) }, registry: Registry);
        var buried = Level(0, 1, l =>
        {
            Array.Fill(l.FloorH, (short)6);
            Array.Fill(l.FloorMat, (ushort)Id("concrete"));
        });
        grid.AddLevel(buried);
        Assert.False(grid.CanStep(0, 0, 1, 0, 6));
        grid.AddLevel(buried.With(flags: Enumerable.Repeat(ChunkConst.LevelVoid, ChunkConst.CellCount).ToArray()));
        Assert.True(grid.CanStep(0, 0, 1, 0, 6));
    }

    [Fact]
    public void Deep_water_is_not_walkable()
    {
        var mats = Mats("grass");
        mats[1] = (ushort)Id("water_deep");
        var grid = new WorldGrid(new[] { new Chunk(0, 0, Heights(), mats) }, registry: Registry);
        Assert.False(grid.CanStep(0, 0, 1, 0, 0));
    }

    [Fact]
    public void A_diagonal_never_cuts_a_wall_corner()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 0, Id("grass")) }, registry: Registry);
        Assert.True(grid.CanStep(0, 0, 1, 1, 0));
        // North and west walls on the corner tile close both orthogonal detours.
        grid.AddLevel(Level(0, 0, l =>
        {
            l.WallN[33] = (ushort)Id("brick");
            l.WallW[33] = (ushort)Id("brick");
        }));
        Assert.True(grid.WallBetween(1, 0, 1, 1, 0));
        Assert.True(grid.WallBetween(0, 1, 1, 1, 0));
        Assert.False(grid.CanStep(0, 0, 1, 1, 0));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(9, false)]
    public void Two_metres_of_headroom_may_touch_the_ceiling(int ceiling, bool allowsGround)
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 6, Id("grass")) }, registry: Registry);
        grid.AddLevel(Level(0, ceiling / 6, l =>
        {
            l.FloorH[0] = (short)ceiling;
            l.FloorMat[0] = (ushort)Id("grass");
        }));
        Assert.Equal(allowsGround, grid.StandingSurfaces(0, 0).Any(s => s.H == 6));
    }

    [Fact]
    public void A_standing_surface_band_comes_from_its_height()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 0, Id("grass")) }, registry: Registry);
        grid.AddLevel(Level(0, 3, l =>
        {
            l.FloorH[0] = 17;
            l.FloorMat[0] = (ushort)Id("grass");
        }));
        Assert.Equal(2, grid.StandingSurfaces(0, 0).First(s => s.H == 17).Z);
    }

    [Fact]
    public void Levels_are_indexed_by_their_chunk()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(-1, 0, 0, Id("grass")), Chunk.Flat(0, 0, 0, Id("grass")) }, registry: Registry);
        foreach (var (cx, local, height) in new[] { (-1, 31, 14), (0, 0, 17) })
            grid.AddLevel(Level(cx, 2, l =>
            {
                l.FloorH[local] = (short)height;
                l.FloorMat[local] = (ushort)Id("grass");
            }));
        Assert.Equal(new[] { 0, 14 }, grid.StandingSurfaces(-1, 0).Select(s => s.H));
        Assert.Equal(new[] { 0, 17 }, grid.StandingSurfaces(0, 0).Select(s => s.H));
    }

    [Fact]
    public void The_chunk_loader_fills_the_cache_on_first_lookup_only()
    {
        var chunk = Chunk.Flat(-1, 2, 0, Id("grass"));
        var level = ChunkLevel.Empty(-1, 2, 0);
        var calls = new List<(int, int)>();
        var grid = new WorldGrid(registry: Registry, chunkLoader: (cx, cy) =>
        {
            calls.Add((cx, cy));
            return (cx, cy) == (-1, 2) ? (chunk, new[] { level }) : null;
        });
        Assert.Same(chunk, grid.Chunk(-1, 2));
        Assert.Same(level, grid.Level(-1, 2, 0));
        Assert.Same(chunk, grid.Chunk(-1, 2));
        Assert.Null(grid.Chunk(5, 5));
        Assert.Null(grid.Chunk(5, 5));
        Assert.Equal(new[] { (-1, 2), (5, 5) }, calls);
    }

    [Fact]
    public void A_diagonal_uses_consistent_leg_heights_and_limits_the_rise()
    {
        var heights = Heights();
        heights[1] = 1;
        heights[32] = 1;
        heights[33] = 1;
        Assert.True(new WorldGrid(new[] { new Chunk(0, 0, heights, Mats("grass")) }, registry: Registry).CanStep(0, 0, 1, 1, 0));
        heights = (short[])heights.Clone();
        heights[33] = 2;
        var steep = new WorldGrid(new[] { new Chunk(0, 0, heights, Mats("grass")) }, registry: Registry);
        Assert.False(steep.CanStep(0, 0, 1, 1, 0));
        Assert.False(steep.CanStep(0, 0, 1, 1));
    }

    [Fact]
    public void A_floorless_wall_stands_on_its_support()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 14, Id("grass")) }, registry: Registry);
        grid.AddLevel(Level(0, 2, l => l.WallW[1] = (ushort)Id("brick")));
        Assert.False(grid.WallBetween(0, 0, 1, 0, 10));
        Assert.True(grid.WallBetween(0, 0, 1, 0, 11));
    }

    [Fact]
    public void A_climbable_column_is_an_explicit_navigation_edge()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 0, Id("grass")) }, registry: Registry);
        var level = Level(0, 2, l =>
        {
            l.FloorH[33] = 12;
            l.FloorMat[33] = (ushort)Id("grass");
        });
        grid.AddLevel(level);
        Assert.False(grid.CanStep(1, 1, 1, 1, 0));
        var flags = new byte[ChunkConst.CellCount];
        flags[33] = ChunkConst.LevelClimbable;
        grid.AddLevel(level.With(flags: flags));
        Assert.True(grid.CanStep(1, 1, 1, 1, 0));
        Assert.Equal(new[] { new Spot(1, 1, 0), new Spot(1, 1, 12) }, Nav.FindPath(grid, new Spot(1, 1, 0), new Spot(1, 1, 12)));
    }

    [Fact]
    public void Navigation_does_not_cross_a_wall_barrier()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 0, Id("grass")) }, registry: Registry);
        grid.AddLevel(Level(0, 0, l =>
        {
            for (var y = 0; y < 32; y++) l.WallW[y * 32 + 16] = (ushort)Id("brick");
        }));
        Assert.Null(Nav.FindPath(grid, new Spot(8, 16, 0), new Spot(24, 16, 0)));
    }

    private static WorldGrid TestGrid(long seed)
    {
        var world = TestWorld.Generate(seed, Registry);
        return new WorldGrid(world.Chunks.Values, world.Levels.Values.OrderBy(l => (l.Cx, l.Cy, l.Z)), Registry);
    }

    [Fact]
    public void Navigation_finds_the_stairs_and_refuses_the_pond()
    {
        var grid = TestGrid(123);
        var start = new Spot(121, 128, 2);
        var path = Nav.FindPath(grid, start, new Spot(140, 140, 18))!;
        Assert.Equal(start, path[0]);
        Assert.Equal(new Spot(140, 140, 18), path[^1]);
        Assert.Contains(path.Zip(path.Skip(1)), p => p.Second.H - p.First.H == 1);
        foreach (var (a, b) in path.Zip(path.Skip(1))) Assert.True(grid.CanStep(a.X, a.Y, b.X, b.Y, a.H));
        // The pond centre has no standing surface: no path can reach it.
        Assert.Null(Nav.FindPath(grid, start, new Spot(196, 70, 0)));
    }

    [Fact]
    public void The_test_world_is_deterministic_and_eight_by_eight()
    {
        var first = TestWorld.Generate(123, Registry);
        Assert.Equal(64, first.Chunks.Count);
        Assert.Equal(first.BlobBytes(), TestWorld.Generate(123, Registry).BlobBytes());
        Assert.NotEqual(first.BlobBytes(), TestWorld.Generate(124, Registry).BlobBytes());
        var viaRegistry = Generators.All["test"].Generate(123, new TestOptions(), Registry);
        Assert.Equal(first.BlobBytes(), viaRegistry.BlobBytes());
        Assert.Equal(first.Objects, viaRegistry.Objects);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(123)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    public void Building_road_terrace_and_pit_are_reachable(long seed)
    {
        var world = TestWorld.Generate(seed, Registry);
        var grid = TestGrid(seed);
        var start = new Spot(121, 128, 2);
        var groundFloor = new Spot(140, 140, 12);
        var firstFloor = new Spot(140, 140, 18);
        foreach (var goal in new[] { groundFloor, new Spot(60, 185, grid.StandingSurfaces(60, 185).Min(s => s.H)), new Spot(110, 88, grid.StandingSurfaces(110, 88).Min(s => s.H)) })
            Assert.NotNull(Nav.FindPath(grid, start, goal));
        foreach (var (from, to) in new[] { (groundFloor, firstFloor), (groundFloor, new Spot(140, 140, 6)), (firstFloor, new Spot(140, 140, 24)) })
            Assert.NotNull(Nav.FindPath(grid, from, to));
        for (var y = 0; y < 255; y++) Assert.True(grid.CanStep(121, y, 121, y + 1, 2), $"road blocked at y={y}");
        for (var z = 1; z < 5; z++)
        {
            foreach (var (x, y, north) in Enumerable.Range(132, 29).Select(y => (160, y, false)).Concat(Enumerable.Range(136, 25).Select(x => (x, 160, true))))
            {
                var level = world.Levels[(x / 32, y / 32, z)];
                var index = (y % 32) * 32 + x % 32;
                Assert.Equal(ChunkConst.NoFloor, level.FloorH[index]);
                Assert.Equal(Id("brick"), north ? level.WallN[index] : level.WallW[index]);
            }
        }
    }

    [Fact]
    public void Blob_bytes_cover_every_persisted_blob()
    {
        var world = TestWorld.Generate(123, Registry);
        var key = world.Chunks.Keys.OrderBy(k => k).First();
        var chunk = world.Chunks[key];
        GeneratedWorld WithChunk(Chunk c) => new(world.Chunks.ToDictionary(p => p.Key, p => p.Key == key ? c : p.Value), world.Levels, world.Spawn, world.GenVersion);
        var dug = (byte[])chunk.Dug.Clone();
        dug[0] = 1;
        Assert.NotEqual(world.BlobBytes(), WithChunk(chunk.With(dug: dug)).BlobBytes());
        Assert.NotEqual(world.BlobBytes(), WithChunk(new Chunk(chunk.Cx, chunk.Cy, chunk.GroundH, chunk.SurfaceMat,
            new[] { new Stratum(0, "topsoil"), new Stratum(8, "rock") }, dug: chunk.Dug)).BlobBytes());
        var levelKey = world.Levels.Keys.OrderBy(k => k).First();
        var level = world.Levels[levelKey];
        GeneratedWorld WithLevel(ChunkLevel l) => new(world.Chunks, world.Levels.ToDictionary(p => p.Key, p => p.Key == levelKey ? l : p.Value), world.Spawn, world.GenVersion);
        foreach (var edit in new Func<ChunkLevel, ChunkLevel>[]
        {
            l => l.With(floorH: Bump(l.FloorH)), l => l.With(floorMat: Bump(l.FloorMat)), l => l.With(wallN: Bump(l.WallN)),
            l => l.With(wallW: Bump(l.WallW)), l => l.With(edgeFlags: Bump(l.EdgeFlags)), l => l.With(flags: Bump(l.Flags)),
        })
            Assert.NotEqual(world.BlobBytes(), WithLevel(edit(level)).BlobBytes());
    }

    private static T[] Bump<T>(T[] values) where T : struct
    {
        var copy = (T[])values.Clone();
        var one = (T)Convert.ChangeType(1, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        var two = (T)Convert.ChangeType(2, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        copy[0] = copy[0].Equals(one) ? two : one;
        return copy;
    }

    [Fact]
    public void Material_ids_survive_a_restart_and_a_toml_reorder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etherbound-mat-{Guid.NewGuid():N}.db");
        try
        {
            Dictionary<string, int> original;
            Dictionary<(int, int), (short[], ushort[])> chunks;
            using (var engine = new WorldEngine(path))
            {
                engine.EnsureWorld(123);
                original = engine.Registry.Ids();
                chunks = engine.Grid.Chunks.ToDictionary(p => p.Key, p => (p.Value.GroundH, p.Value.SurfaceMat));
            }
            SqliteConnection.ClearAllPools();
            const string added = "[[materials]]\nkey = \"added_for_test\"\nname = \"Added\"\ncolor = \"#fff\"\nwalkable = true\nwalk_cost = 1\n" +
                "solid = true\nblocks_sight = false\ndiggable = true\ndig_cost = 1\nflammable = false\ndensity = 1\nresistance = 1\nliquid = false\ntags = []\n\n";
            using var restarted = new WorldEngine(path, materialsToml: added + DataFiles.ReadText("materials.toml"));
            restarted.EnsureWorld();
            Assert.Equal(original, original.Keys.ToDictionary(k => k, k => restarted.Registry[k].Id));
            Assert.Equal(original.Values.Max() + 1, restarted.Registry["added_for_test"].Id);
            foreach (var (k, (g, s)) in chunks)
            {
                Assert.Equal(g, restarted.Grid.Chunks[k].GroundH);
                Assert.Equal(s, restarted.Grid.Chunks[k].SurfaceMat);
            }
            Assert.Equal("asphalt", restarted.Registry[restarted.Grid.Chunk(3, 4)!.SurfaceMat[25]].Key);
            Assert.Equal(TestWorld.GenVersion, restarted.GetState().GenVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void A_stale_generator_version_regenerates_the_chunks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etherbound-regen-{Guid.NewGuid():N}.db");
        try
        {
            using (var engine = new WorldEngine(path)) engine.EnsureWorld(123);
            SqliteConnection.ClearAllPools();
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE chunk SET ground_h = zeroblob(2048) WHERE cx = 3 AND cy = 4; UPDATE world_meta SET gen_version = $v";
                command.Parameters.AddWithValue("$v", TestWorld.GenVersion - 1);
                command.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
            using var restarted = new WorldEngine(path);
            restarted.EnsureWorld();
            Assert.Equal(2, restarted.Grid.Chunk(3, 4)!.GroundH[25]);
            Assert.Equal(TestWorld.GenVersion, restarted.GetState().GenVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
