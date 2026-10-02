using System.Security.Cryptography;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;

namespace EtherBound.Sim.Tests;

/// <summary>The endless terrain is a pure function of seed, options and position (Dev-010).</summary>
public class EndlessTerrainRules
{
    private static readonly MaterialRegistry Registry = MaterialRegistry.Load();

    private static TerrainField Field(long seed = 7, EndlessOptions? options = null) =>
        new(seed, options ?? new EndlessOptions(), Registry);

    private static (Chunk Chunk, IReadOnlyList<ChunkLevel> Levels) Make(TerrainField field, int cx, int cy) =>
        EndlessWorld.GenerateChunk(field, cx, cy)!.Value;

    private static string Sha(Chunk chunk)
    {
        using var bytes = new MemoryStream();
        bytes.Write(chunk.GroundBlob);
        bytes.Write(chunk.SurfaceBlob);
        bytes.Write(chunk.StrataCompactJson());
        return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
    }

    [Fact]
    public void A_chunk_does_not_depend_on_what_was_generated_before_it()
    {
        var alone = Sha(Make(Field(), -3, 4).Chunk);

        var busy = Field();
        foreach (var (cx, cy) in new[] { (-2, 4), (-4, 4), (-3, 3), (-3, 5), (9, -9), (0, 0) }) Make(busy, cx, cy);
        Assert.Equal(alone, Sha(Make(busy, -3, 4).Chunk));

        // A new field instance (a restart, an eviction and reload) rebuilds the same bytes.
        Assert.Equal(alone, Sha(Make(Field(), -3, 4).Chunk));
    }

    [Fact]
    public void Golden_hashes_hold_near_far_and_negative_chunks()
    {
        using var doc = Goldens.Load("endless-terrain.json");
        var golden = doc.RootElement;
        Assert.Equal(EndlessWorld.GenVersion, golden.GetProperty("gen_version").GetInt32());
        var field = Field(golden.GetProperty("seed").GetInt64());
        var chunks = golden.GetProperty("chunks").EnumerateObject().ToList();
        Assert.Contains(chunks, c => c.Name.StartsWith("-"));
        Assert.Contains(chunks, c => Math.Abs(int.Parse(c.Name.Split(',')[0])) > 300);
        foreach (var entry in chunks)
        {
            var parts = entry.Name.Split(',').Select(int.Parse).ToArray();
            Assert.Equal(entry.Value.GetString(), Sha(Make(field, parts[0], parts[1]).Chunk));
        }
        var spawn = golden.GetProperty("spawn");
        Assert.Equal((spawn[0].GetDouble(), spawn[1].GetDouble(), spawn[2].GetInt32()), EndlessWorld.Spawn(field));
    }

    [Fact]
    public void Chunks_are_the_field_with_no_seam_at_their_borders()
    {
        var field = Field();
        var inside = 0;
        var across = 0;
        for (var cy = -2; cy <= 1; cy++)
        for (var cx = -2; cx <= 1; cx++)
        {
            var chunk = Make(field, cx, cy).Chunk;
            for (var ly = 0; ly < ChunkConst.Size; ly++)
            for (var lx = 0; lx < ChunkConst.Size; lx++)
            {
                var x = cx * ChunkConst.Size + lx;
                var y = cy * ChunkConst.Size + ly;
                var cell = field.At(x, y);
                Assert.Equal(cell.Height, chunk.GroundH[ly * ChunkConst.Size + lx]);
                Assert.Equal(cell.Material, chunk.SurfaceMat[ly * ChunkConst.Size + lx]);
                // The step to the next tile east and south, split by whether it crosses a chunk border.
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
                {
                    var step = Math.Abs(field.At(x + dx, y + dy).Height - cell.Height);
                    var border = (dx == 1 && lx == ChunkConst.Size - 1) || (dy == 1 && ly == ChunkConst.Size - 1);
                    if (border) across = Math.Max(across, step);
                    else inside = Math.Max(inside, step);
                }
            }
        }
        Assert.True(across <= inside, $"border step {across} is larger than any step inside a chunk ({inside})");
    }

    [Fact]
    public void Only_chunks_inside_the_coordinate_limit_exist()
    {
        var field = Field();
        Assert.NotNull(EndlessWorld.GenerateChunk(field, EndlessWorld.MaxChunkCoord, -EndlessWorld.MaxChunkCoord));
        Assert.Null(EndlessWorld.GenerateChunk(field, EndlessWorld.MaxChunkCoord + 1, 0));
        Assert.Null(EndlessWorld.GenerateChunk(field, 0, -EndlessWorld.MaxChunkCoord - 1));
    }

    [Fact]
    public void Seed_and_options_change_the_terrain_and_nothing_else_does()
    {
        var baseline = Sha(Make(Field(), 6, 6).Chunk);
        Assert.NotEqual(baseline, Sha(Make(Field(8), 6, 6).Chunk));
        Assert.NotEqual(baseline, Sha(Make(Field(7, new EndlessOptions(Relief: 0)), 6, 6).Chunk));
        Assert.NotEqual(baseline, Sha(Make(Field(7, new EndlessOptions(Mountains: 1.0)), 6, 6).Chunk));
        var wetter = Field(7, new EndlessOptions(Water: 1.0));
        var drier = Field(7, new EndlessOptions(Water: 0.0));
        int Water(TerrainField f) => Enumerable.Range(0, 1600).Count(i => Registry.Get(f.At(i % 40 * 25, i / 40 * 25).Material)!.Liquid);
        Assert.True(Water(wetter) > Water(drier));
    }

    [Fact]
    public void The_field_has_no_lattice_period()
    {
        // ValueNoise repeats every 256 lattice cells; the hash lattice must not, at any octave.
        var noise = new HashNoise(7);
        var repeats = 0;
        for (var i = 0; i < 200; i++)
            if (noise.Value(1, i, 3) == noise.Value(1, i + 256, 3)) repeats++;
        Assert.True(repeats < 3);
        var field = Field();
        var same = 0;
        for (var i = 0; i < 200; i++)
            if (field.At(i * 7, i * 3) == field.At(i * 7 + 24576, i * 3)) same++;
        Assert.True(same < 100);
    }

    [Fact]
    public void Spawn_is_inland_walkable_and_deterministic()
    {
        foreach (var seed in new long[] { 7, 1, 42, 99, 1895070486 })
        {
            var field = Field(seed);
            var spawn = EndlessWorld.Spawn(field);
            Assert.Equal(spawn, EndlessWorld.Spawn(Field(seed)));
            var cell = field.At((int)Math.Floor(spawn.X), (int)Math.Floor(spawn.Y));
            Assert.True(field.Walkable(cell));
            Assert.Equal(cell.Height, spawn.H);
            Assert.True(cell.Height >= field.SeaLevel + 4);
        }
    }

    [Fact]
    public void The_spawn_kit_lies_on_flat_walkable_ground_with_valid_parents()
    {
        var field = Field();
        var spawn = EndlessWorld.Spawn(field);
        var kit = EndlessWorld.SpawnKit(field, spawn);
        Assert.Equal(new[] { "shovel", "backpack", "chest", "apple", "bottle", "sledgehammer" }, kit.Select(o => o.Kind));
        foreach (var item in kit)
        {
            Assert.Equal(field.StandingHeight(item.X, item.Y), item.H);
            if (item.Parent is { } parent) Assert.Equal("chest", kit[parent].Kind);
        }
    }

    [Fact]
    public void The_infinite_generator_is_registered_with_forms_and_leaves_the_bounded_ones_alone()
    {
        var spec = Generators.All["infinite"];
        Assert.True(spec.Streaming);
        Assert.Equal(new[] { "relief", "water", "mountains" }, spec.Fields.Select(f => f.Path));
        Assert.False(Generators.All["test"].Streaming);
        Assert.False(Generators.All["lab"].Streaming);
        Assert.Equal("test", Generators.Default);
        Assert.Throws<ArgumentException>(() => spec.Parse(new System.Text.Json.Nodes.JsonObject { ["water"] = 1.5 }));
        Assert.Equal(new EndlessOptions(), spec.Defaults);
        var world = spec.Generate(7, spec.Defaults, Registry);
        Assert.Empty(world.Chunks);
        Assert.Equal(6, world.Objects.Count);
    }
}
