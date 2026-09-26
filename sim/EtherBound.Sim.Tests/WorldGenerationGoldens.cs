using System.Security.Cryptography;
using System.Text.Json;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;

namespace EtherBound.Sim.Tests;

/// <summary>Every generator and seed produces the Python server's bytes, spawn, objects and Extras.</summary>
public class WorldGenerationGoldens
{
    private static readonly MaterialRegistry Registry = MaterialRegistry.Load();

    public static IEnumerable<object[]> Worlds()
    {
        using var doc = Goldens.Load("worlds.json");
        return doc.RootElement.EnumerateArray()
            .Select(w => new object[] { w.GetProperty("generator").GetString()!, w.GetProperty("seed").GetInt64() })
            .ToList();
    }

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    [Theory]
    [MemberData(nameof(Worlds))]
    public void World_matches_python(string generator, long seed)
    {
        using var doc = Goldens.Load("worlds.json");
        var golden = doc.RootElement.EnumerateArray()
            .First(w => w.GetProperty("generator").GetString() == generator && w.GetProperty("seed").GetInt64() == seed);
        var spec = Generators.All[generator];
        var world = spec.Generate(seed, spec.Defaults, Registry);

        Assert.Equal(golden.GetProperty("gen_version").GetInt32(), world.GenVersion);
        var spawn = spec.Spawn(seed, spec.Defaults);
        var goldenSpawn = golden.GetProperty("spawn");
        Assert.Equal((goldenSpawn[0].GetDouble(), goldenSpawn[1].GetDouble(), goldenSpawn[2].GetInt32()), spawn);

        foreach (var entry in golden.GetProperty("chunks").EnumerateObject())
        {
            var parts = entry.Name.Split(',').Select(int.Parse).ToArray();
            var chunk = world.Chunks[(parts[0], parts[1])];
            Assert.Equal(entry.Value.GetProperty("ground_h").GetString(), Sha(chunk.GroundBlob));
            Assert.Equal(entry.Value.GetProperty("surface_mat").GetString(), Sha(chunk.SurfaceBlob));
            Assert.Equal(entry.Value.GetProperty("dug").GetString(), Sha(chunk.DugBlob));
        }
        Assert.Equal(golden.GetProperty("chunks").EnumerateObject().Count(), world.Chunks.Count);
        foreach (var entry in golden.GetProperty("levels").EnumerateObject())
        {
            var parts = entry.Name.Split(',').Select(int.Parse).ToArray();
            var level = world.Levels[(parts[0], parts[1], parts[2])];
            Assert.Equal(entry.Value.GetProperty("floor_h").GetString(), Sha(level.FloorBlob));
            Assert.Equal(entry.Value.GetProperty("floor_mat").GetString(), Sha(level.FloorMatBlob));
            Assert.Equal(entry.Value.GetProperty("wall_n").GetString(), Sha(level.WallNBlob));
            Assert.Equal(entry.Value.GetProperty("wall_w").GetString(), Sha(level.WallWBlob));
            Assert.Equal(entry.Value.GetProperty("edge_flags").GetString(), Sha(level.EdgeFlagsBlob));
            Assert.Equal(entry.Value.GetProperty("flags").GetString(), Sha(level.FlagsBlob));
        }
        Assert.Equal(golden.GetProperty("levels").EnumerateObject().Count(), world.Levels.Count);
        Assert.Equal(golden.GetProperty("blob_sha256").GetString(), Sha(world.BlobBytes()));

        var objects = golden.GetProperty("objects").EnumerateArray().Select(o => new GeneratedObject(
            o.GetProperty("kind").GetString()!, o.GetProperty("x").GetInt32(), o.GetProperty("y").GetInt32(),
            o.GetProperty("h").GetInt32(), o.GetProperty("quantity").GetInt32(), o.GetProperty("open").GetBoolean(),
            o.GetProperty("parent").ValueKind == JsonValueKind.Null ? null : o.GetProperty("parent").GetInt32())).ToList();
        Assert.Equal(objects, world.Objects);
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void Population_matches_python(string generator, long seed)
    {
        using var doc = Goldens.Load("populations.json");
        var golden = doc.RootElement.EnumerateArray()
            .First(p => p.GetProperty("generator").GetString() == generator && p.GetProperty("seed").GetInt64() == seed);
        var spec = Generators.All[generator];
        var world = spec.Generate(seed, spec.Defaults, Registry);
        var grid = new WorldGrid(world.Chunks.Values, world.Levels.Values.OrderBy(l => (l.Cx, l.Cy, l.Z)), Registry);
        // Objects get ids 1.. in generator order, as commit_objects inserts them into an empty table.
        var catalog = grid.Catalog;
        var tileObjects = world.Objects.Select((o, i) => (o, Id: i + 1)).Where(p => p.o.Parent is null)
            .Select(p => new TileObject(p.Id, p.o.Kind, p.o.X, p.o.Y, p.o.H, p.o.Quantity,
                catalog.Get(p.o.Kind)?.Openable == true ? p.o.Open : null))
            .ToList();
        foreach (var group in tileObjects.GroupBy(t => (PyMath.FloorDiv(t.X, 32), PyMath.FloorDiv(t.Y, 32))))
            grid.SetChunkObjects(group.Key.Item1, group.Key.Item2, group);

        var spawn = Spawning.SpawnPoint(grid, Registry, spec, spec.Defaults, seed);
        var extras = Population.Populate(grid, Registry, spawn, seed);

        var expected = golden.GetProperty("extras").EnumerateArray().ToList();
        Assert.Equal(expected.Count, extras.Count);
        for (var i = 0; i < extras.Count; i++)
        {
            Assert.Equal(expected[i].GetProperty("id").GetString(), extras[i].Id);
            Assert.Equal(expected[i].GetProperty("name").GetString(), extras[i].Name);
            Assert.Equal(expected[i].GetProperty("x").GetDouble(), extras[i].X);
            Assert.Equal(expected[i].GetProperty("y").GetDouble(), extras[i].Y);
            Assert.Equal(expected[i].GetProperty("h").GetInt32(), extras[i].H);
            var anchor = expected[i].GetProperty("mind").GetProperty("anchor");
            Assert.Equal((anchor.GetProperty("x").GetInt32(), anchor.GetProperty("y").GetInt32(), anchor.GetProperty("h").GetInt32()),
                (extras[i].AnchorX, extras[i].AnchorY, extras[i].AnchorH));
        }
    }
}
