using System.Collections;
using System.Reflection;
using EtherBound.Game.Spike;
using Xunit;

namespace EtherBound.Game.Tests;

public sealed class ChunkMesherTests
{
    [Fact]
    public void ParallelGeometryMatchesSerialBuffersExactly()
    {
        var world = MakeWorld();
        var mesher = new ChunkMesher(world, new(), new(), new(), null!, null!, null!, null!);
        var keys = new[] { (Cx: 0, Cy: 0), (Cx: 1, Cy: 0) };
        var serial = keys.ToDictionary(key => key, key => mesher.BuildGeometry(key.Cx, key.Cy));
        var parallel = new System.Collections.Concurrent.ConcurrentDictionary<(int Cx, int Cy), ChunkMesher.ChunkGeometry>();

        Parallel.ForEach(keys, key => parallel[key] = mesher.BuildGeometry(key.Cx, key.Cy));

        foreach (var key in keys)
            AssertGeometryEqual(serial[key], parallel[key]);
    }

    private static WorldDump MakeWorld()
    {
        var world = new WorldDump();
        for (var cx = 0; cx < 2; cx++)
        {
            var heights = new short[WorldDump.CellCount];
            for (var y = 0; y < WorldDump.ChunkSize; y++)
            for (var x = 0; x < WorldDump.ChunkSize; x++)
                heights[y * WorldDump.ChunkSize + x] = (short)(4 + ((cx * 32 + x) * 7 + y * 11) % 18);

            world.Chunks[(cx, 0)] = new WorldDump.Chunk
            {
                GroundH = heights,
                SurfaceMat = new ushort[WorldDump.CellCount],
                Dug = new byte[WorldDump.CellCount],
            };
        }
        return world;
    }

    private static void AssertGeometryEqual(ChunkMesher.ChunkGeometry expected, ChunkMesher.ChunkGeometry actual)
    {
        var bandsProperty = typeof(ChunkMesher.ChunkGeometry).GetProperty("Bands", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var expectedBands = (IDictionary)bandsProperty.GetValue(expected)!;
        var actualBands = (IDictionary)bandsProperty.GetValue(actual)!;
        Assert.Equal(expectedBands.Count, actualBands.Count);

        foreach (DictionaryEntry expectedEntry in expectedBands)
        {
            var actualBand = actualBands[expectedEntry.Key]!;
            var expectedBand = expectedEntry.Value!;
            foreach (var builderName in new[] { "Terrain", "Structure", "Glass", "Outline" })
            {
                var builderProperty = expectedBand.GetType().GetProperty(builderName)!;
                var expectedBuilder = builderProperty.GetValue(expectedBand)!;
                var actualBuilder = builderProperty.GetValue(actualBand)!;
                foreach (var bufferName in new[] { "Verts", "Normals", "Colors", "Uv", "Uv2", "Indices" })
                {
                    var bufferField = expectedBuilder.GetType().GetField(bufferName)!;
                    var expectedBuffer = ((IEnumerable)bufferField.GetValue(expectedBuilder)!).Cast<object>();
                    var actualBuffer = ((IEnumerable)bufferField.GetValue(actualBuilder)!).Cast<object>();
                    Assert.Equal(expectedBuffer, actualBuffer);
                }
            }
        }
    }
}
