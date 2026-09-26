namespace EtherBound.Sim.World.Gen;

/// <summary>A flat, uniform base map that generators and features stamp into (<c>gen/canvas.py</c>).</summary>
public sealed class GenCanvas
{
    public static readonly IReadOnlyList<Stratum> DefaultStrata = new[] { new Stratum(0, "topsoil"), new Stratum(8, "dirt"), new Stratum(24, "rock") };

    private readonly Dictionary<(int, int), short[]> _ground = new();
    private readonly Dictionary<(int, int), ushort[]> _surface = new();
    private readonly Dictionary<(int, int, int), LevelArrays> _levels = new();

    public GenCanvas(int chunksX, int chunksY, int baseH, int baseMat, string generator = "lab", int genVersion = 1,
        IReadOnlyList<Stratum>? strata = null)
    {
        ChunksX = chunksX;
        ChunksY = chunksY;
        Generator = generator;
        GenVersion = genVersion;
        Strata = strata ?? DefaultStrata;
        for (var cy = 0; cy < chunksY; cy++)
        for (var cx = 0; cx < chunksX; cx++)
        {
            _ground[(cx, cy)] = Enumerable.Repeat((short)baseH, ChunkConst.CellCount).ToArray();
            _surface[(cx, cy)] = Enumerable.Repeat((ushort)baseMat, ChunkConst.CellCount).ToArray();
        }
    }

    public int ChunksX { get; }
    public int ChunksY { get; }
    public string Generator { get; }
    public int GenVersion { get; }
    public IReadOnlyList<Stratum> Strata { get; }
    public int Width => ChunksX * ChunkConst.Size;
    public int Height => ChunksY * ChunkConst.Size;

    public sealed class LevelArrays
    {
        public readonly short[] FloorH = Enumerable.Repeat(ChunkConst.NoFloor, ChunkConst.CellCount).ToArray();
        public readonly ushort[] FloorMat = new ushort[ChunkConst.CellCount];
        public readonly ushort[] WallN = new ushort[ChunkConst.CellCount];
        public readonly ushort[] WallW = new ushort[ChunkConst.CellCount];
        public readonly byte[] EdgeFlags = new byte[ChunkConst.CellCount];
        public readonly byte[] Flags = new byte[ChunkConst.CellCount];
    }

    private (int, int) ChunkKey(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) throw new IndexOutOfRangeException($"tile {x},{y} is outside the canvas");
        return (x / ChunkConst.Size, y / ChunkConst.Size);
    }

    public static int Index(int x, int y) => PyMath.Mod(y, ChunkConst.Size) * ChunkConst.Size + PyMath.Mod(x, ChunkConst.Size);

    public void SetGround(int x, int y, int h) => _ground[ChunkKey(x, y)][Index(x, y)] = (short)h;

    public void SetSurface(int x, int y, int material) => _surface[ChunkKey(x, y)][Index(x, y)] = (ushort)material;

    public int GetGround(int x, int y) => _ground[ChunkKey(x, y)][Index(x, y)];

    public CanvasLevel Level(int z) => new(this, z);

    public LevelArrays Arrays(int x, int y, int z)
    {
        var (cx, cy) = ChunkKey(x, y);
        if (!_levels.TryGetValue((cx, cy, z), out var arrays)) _levels[(cx, cy, z)] = arrays = new LevelArrays();
        return arrays;
    }

    public GeneratedWorld Build((double X, double Y, int H) spawn)
    {
        var chunks = new Dictionary<(int, int), Chunk>();
        foreach (var key in _ground.Keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2))
            chunks[key] = new Chunk(key.Item1, key.Item2, _ground[key], _surface[key], Strata, genVersion: GenVersion);
        var levels = _levels.ToDictionary(p => p.Key, p => new ChunkLevel(p.Key.Item1, p.Key.Item2, p.Key.Item3,
            p.Value.FloorH, p.Value.FloorMat, p.Value.WallN, p.Value.WallW, p.Value.EdgeFlags, p.Value.Flags));
        return new GeneratedWorld(chunks, levels, spawn, GenVersion, null, Generator);
    }
}

/// <summary>Floor, wall and flag setters for one <c>z</c> band of a <see cref="GenCanvas"/>.</summary>
public readonly struct CanvasLevel
{
    private readonly GenCanvas _canvas;
    private readonly int _z;

    public CanvasLevel(GenCanvas canvas, int z)
    {
        _canvas = canvas;
        _z = z;
    }

    public void SetFloor(int x, int y, int h, int material)
    {
        var arrays = _canvas.Arrays(x, y, _z);
        arrays.FloorH[GenCanvas.Index(x, y)] = (short)h;
        arrays.FloorMat[GenCanvas.Index(x, y)] = (ushort)material;
    }

    public void ClearFloor(int x, int y) => _canvas.Arrays(x, y, _z).FloorH[GenCanvas.Index(x, y)] = ChunkConst.NoFloor;

    public void SetWall(int x, int y, EdgeDirection direction, int material)
    {
        var arrays = _canvas.Arrays(x, y, _z);
        (direction == EdgeDirection.North ? arrays.WallN : arrays.WallW)[GenCanvas.Index(x, y)] = (ushort)material;
    }

    public void SetEdge(int x, int y, int flag) => _canvas.Arrays(x, y, _z).EdgeFlags[GenCanvas.Index(x, y)] |= (byte)flag;

    public void SetFlag(int x, int y, int flag) => _canvas.Arrays(x, y, _z).Flags[GenCanvas.Index(x, y)] |= (byte)flag;
}
