namespace EtherBound.Sim.World.Gen;

/// <summary>The v5 test world (<c>gen/testworld.py</c>): hills, road, terrace, building, pond, pit.</summary>
public static class TestWorld
{
    public const int GenVersion = 5;
    public const int WorldChunks = 8;
    public static readonly (double X, double Y) SpawnPoint = (121.5, 128.5);

    /// <summary>The fixed v5 object layout; order is stable, so generated ids are stable.</summary>
    public static IReadOnlyList<GeneratedObject> Objects() => new[]
    {
        // 1. Near spawn (grass at h = 2, within four tiles of 121.5, 128.5).
        new GeneratedObject("shovel", 124, 126, 2),
        new GeneratedObject("backpack", 125, 126, 2),
        new GeneratedObject("chest", 124, 127, 2),
        new GeneratedObject("apple", 124, 127, 2, Quantity: 3, Parent: 2),
        new GeneratedObject("bottle", 124, 127, 2, Quantity: 2, Parent: 2),
        // 2. Building ground floor (interior at h = 12).
        new GeneratedObject("table", 137, 133, 12),
        new GeneratedObject("bottle", 137, 133, 14),
        new GeneratedObject("chair", 138, 133, 12),
        new GeneratedObject("chair", 137, 134, 12),
        new GeneratedObject("shelf", 139, 133, 12),
        new GeneratedObject("apple", 139, 133, 12, Quantity: 2, Parent: 9),
        new GeneratedObject("barrel", 140, 133, 12),
        // 3. Climbing test: two chests stacked on open ground at h = 1.
        new GeneratedObject("chest", 126, 127, 1),
        new GeneratedObject("chest", 126, 127, 3),
        new GeneratedObject("sledgehammer", 126, 126, 2),
    };

    private sealed class LevelBuffer
    {
        public readonly short[] FloorH = Enumerable.Repeat(ChunkConst.NoFloor, ChunkConst.CellCount).ToArray();
        public readonly ushort[] FloorMat = new ushort[ChunkConst.CellCount];
        public readonly ushort[] WallN = new ushort[ChunkConst.CellCount];
        public readonly ushort[] WallW = new ushort[ChunkConst.CellCount];
        public readonly byte[] EdgeFlags = new byte[ChunkConst.CellCount];
        public readonly byte[] Flags = new byte[ChunkConst.CellCount];
    }

    private static (int Cx, int Lx) Split(int v) => (PyMath.FloorDiv(v, ChunkConst.Size), PyMath.Mod(v, ChunkConst.Size));

    public static GeneratedWorld Generate(long seed, MaterialRegistry? registry = null)
    {
        registry ??= MaterialRegistry.Load();
        var noise = new ValueNoise(seed);
        var grass = registry["grass"].Id;
        var asphalt = registry["asphalt"].Id;
        var shallow = registry["water_shallow"].Id;
        var deep = registry["water_deep"].Id;
        var ground = new Dictionary<(int, int), short[]>();
        var surfaces = new Dictionary<(int, int), ushort[]>();

        for (var cy = 0; cy < WorldChunks; cy++)
        for (var cx = 0; cx < WorldChunks; cx++)
        {
            var cells = new short[ChunkConst.CellCount];
            var materials = new ushort[ChunkConst.CellCount];
            for (var ly = 0; ly < ChunkConst.Size; ly++)
            {
                var gy = cy * ChunkConst.Size + ly;
                for (var lx = 0; lx < ChunkConst.Size; lx++)
                {
                    var gx = cx * ChunkConst.Size + lx;
                    var index = ly * ChunkConst.Size + lx;
                    var relief = noise.Fbm(gx / 96.0, gy / 96.0, octaves: 3);
                    var southFlatten = Math.Max(0.0, Math.Min(1.0, (gy - 160) / 95.0));
                    var height = PyMath.Round(relief * 12.0 * (1.0 - southFlatten));
                    var material = grass;
                    if (gx is >= 120 and <= 123)
                    {
                        height = 2;
                        material = asphalt;
                    }
                    else
                    {
                        // Keep the bench local so the road spawn remains connected to its surroundings.
                        var terraceLift = 0;
                        if (gy is >= 89 and <= 94) terraceLift = 3;
                        else if (gy is >= 95 and <= 96) terraceLift = 97 - gy;
                        if (gx is >= 108 and <= 114 && gy is >= 89 and <= 90) terraceLift = gy - 88;
                        height += terraceLift;
                    }
                    // The park pit is a lowered heightfield, with a one-tile ramp on its east side.
                    if (gx is >= 48 and <= 72 && gy is >= 176 and <= 200)
                    {
                        var depth = gx < 70 ? 4 : Math.Max(0, 4 - (gx - 69));
                        height -= depth;
                    }
                    // The pond uses material, rather than a special water volume.
                    var pond = (gx - 196) * (gx - 196) + (gy - 70) * (gy - 70);
                    if (pond <= 12 * 12)
                    {
                        height -= pond > 7 * 7 ? 2 : 4;
                        material = pond > 7 * 7 ? shallow : deep;
                    }
                    cells[index] = (short)height;
                    materials[index] = (ushort)material;
                }
            }
            ground[(cx, cy)] = cells;
            surfaces[(cx, cy)] = materials;
        }

        var levels = new Dictionary<(int, int, int), LevelBuffer>();
        // Walls occupy the perimeter edges; only the enclosed interior has floor slabs.
        bool Interior(int x, int y) => x is >= 137 and < 160 && y is >= 133 and < 160;
        const int baseH = 6;
        var brick = registry["brick"].Id;
        var concrete = registry["concrete"].Id;
        var wood = registry["wood_floor"].Id;
        var roofing = registry["roofing"].Id;
        foreach (var (z, floorMaterial) in new[] { (1, concrete), (2, concrete), (3, wood), (4, roofing) })
        {
            for (var y = 132; y < 161; y++)
            for (var x = 136; x < 161; x++)
            {
                var (cx, lx) = Split(x);
                var (cy, ly) = Split(y);
                if (!levels.TryGetValue((cx, cy, z), out var level)) levels[(cx, cy, z)] = level = new LevelBuffer();
                var index = ly * ChunkConst.Size + lx;
                if (Interior(x, y))
                {
                    level.FloorH[index] = (short)(baseH + (z - 1) * 6);
                    level.FloorMat[index] = (ushort)floorMaterial;
                }
                if (x == 136)
                {
                    level.WallW[index] = (ushort)brick;
                    if (y == 146) level.EdgeFlags[index] |= ChunkConst.EdgeWDoorway;
                }
                if (y == 132)
                {
                    level.WallN[index] = (ushort)brick;
                    if (x == 146) level.EdgeFlags[index] |= ChunkConst.EdgeNDoorway;
                    else if (z < 4 && x % 4 == 0) level.EdgeFlags[index] |= ChunkConst.EdgeNWindow;
                }
                if (x == 160) level.WallW[index] = (ushort)brick;
                if (y == 160)
                {
                    level.WallN[index] = (ushort)brick;
                    if (z == 1 && x == 146) level.EdgeFlags[index] |= ChunkConst.EdgeNDoorway;
                }
                if (z == 4 && Interior(x, y)) level.Flags[index] |= ChunkConst.LevelRoof;
                if (z is 1 or 2 && Interior(x, y)) level.Flags[index] |= ChunkConst.LevelVoid;
            }
        }

        void SetGround(int x, int y, int h)
        {
            var (cx, lx) = Split(x);
            var (cy, ly) = Split(y);
            ground[(cx, cy)][ly * ChunkConst.Size + lx] = (short)h;
        }

        LevelBuffer LevelAt(int x, int y, int z, out int index)
        {
            var (cx, lx) = Split(x);
            var (cy, ly) = Split(y);
            index = ly * ChunkConst.Size + lx;
            return levels[(cx, cy, z)];
        }

        // The ground approaches the west door from the continuous road at the same standing height.
        for (var y = 133; y < 160; y++)
        for (var x = 137; x < 160; x++)
            SetGround(x, y, baseH + 6);
        // Keep the stairwell open while its west roof landing joins both sides of the roof.
        for (var x = 136; x < 161; x++)
        {
            if (x == 144) continue;
            LevelAt(x, 150, 4, out var i).FloorH[i] = ChunkConst.NoFloor;
        }
        // Extend and grade the approach from the road to the west doorway.
        for (var x = 124; x < 137; x++)
            // The wall tile is floorless but still meets the interior at the ground-floor height.
            SetGround(x, 146, x == 136 ? baseH + 6 : Math.Min(baseH + 6, 2 + x - 123));

        // A south-side stair flight drops from the ground floor into the basement.
        for (var x = 145; x < 151; x++)
        {
            var height = baseH + x - 144;
            var basement = LevelAt(x, 152, 1, out var i);
            var groundLevel = LevelAt(x, 152, 2, out _);
            basement.FloorH[i] = (short)height;
            groundLevel.FloorH[i] = ChunkConst.NoFloor;
            groundLevel.Flags[i] |= ChunkConst.LevelVoid;
        }
        // Stairs continue from the ground floor to the first floor and up to the roof.
        for (var x = 145; x < 151; x++)
        {
            LevelAt(x, 150, 3, out var i).FloorH[i] = (short)(baseH + 6 + x - 144);
            LevelAt(x, 151, 4, out var j).FloorH[j] = (short)(baseH + 12 + x - 144);
        }
        // Keep the outdoor landing south of the basement doorway level with its floor.
        for (var y = 160; y < 165; y++)
        for (var x = 142; x < 151; x++)
            SetGround(x, y, baseH);

        var strata = GenCanvas.DefaultStrata;
        var chunks = ground.Keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2)
            .ToDictionary(k => k, k => new Chunk(k.Item1, k.Item2, ground[k], surfaces[k], strata, genVersion: GenVersion));
        var chunkLevels = levels.ToDictionary(p => p.Key, p => new ChunkLevel(p.Key.Item1, p.Key.Item2, p.Key.Item3,
            p.Value.FloorH, p.Value.FloorMat, p.Value.WallN, p.Value.WallW, p.Value.EdgeFlags, p.Value.Flags));
        return new GeneratedWorld(chunks, chunkLevels, (SpawnPoint.X, SpawnPoint.Y, 0), GenVersion, Objects(), "test");
    }
}
