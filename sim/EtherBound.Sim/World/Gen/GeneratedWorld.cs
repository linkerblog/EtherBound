namespace EtherBound.Sim.World.Gen;

/// <summary>One object a generator places. <c>Parent</c> is the index of its container, if any.</summary>
public sealed record GeneratedObject(string Kind, int X, int Y, int H, int Quantity = 1, bool Open = false, int? Parent = null);

/// <summary>A named rectangle of a generator's map, for the debug bay list.</summary>
public sealed record GeneratorBay(string Key, int X, int Y, int Width, int Height);

/// <summary>The complete output of one generator run: everything the engine persists.</summary>
public sealed class GeneratedWorld
{
    public GeneratedWorld(IReadOnlyDictionary<(int, int), Chunk> chunks, IReadOnlyDictionary<(int, int, int), ChunkLevel> levels,
        (double X, double Y, int H) spawn, int genVersion, IReadOnlyList<GeneratedObject>? objects = null, string generator = "test")
    {
        Chunks = chunks;
        Levels = levels;
        Spawn = spawn;
        GenVersion = genVersion;
        Objects = objects ?? Array.Empty<GeneratedObject>();
        Generator = generator;
    }

    public IReadOnlyDictionary<(int, int), Chunk> Chunks { get; }
    public IReadOnlyDictionary<(int, int, int), ChunkLevel> Levels { get; }
    public (double X, double Y, int H) Spawn { get; }
    public int GenVersion { get; }
    public IReadOnlyList<GeneratedObject> Objects { get; }
    public string Generator { get; }

    /// <summary>Every chunk and level blob in key order, as <c>GeneratedWorld.blob_bytes</c>.</summary>
    public byte[] BlobBytes()
    {
        using var buffer = new MemoryStream();
        foreach (var key in Chunks.Keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2))
        {
            var chunk = Chunks[key];
            buffer.Write(chunk.GroundBlob);
            buffer.Write(chunk.SurfaceBlob);
            buffer.Write(chunk.DugBlob);
            buffer.Write(chunk.StrataCompactJson());
        }
        foreach (var key in Levels.Keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2).ThenBy(k => k.Item3))
        {
            var level = Levels[key];
            buffer.Write(level.FloorBlob);
            buffer.Write(level.FloorMatBlob);
            buffer.Write(level.WallNBlob);
            buffer.Write(level.WallWBlob);
            buffer.Write(level.EdgeFlagsBlob);
            buffer.Write(level.FlagsBlob);
        }
        return buffer.ToArray();
    }
}
