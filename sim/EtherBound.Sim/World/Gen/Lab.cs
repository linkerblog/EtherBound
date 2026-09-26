namespace EtherBound.Sim.World.Gen;

/// <summary>The debug lab (<c>gen/lab.py</c>): a 128 m flat fixture with paths and nine bays.</summary>
public static class Lab
{
    public const int GenVersion = 2;
    public const int Chunks = 4;
    public const int BaseH = 2;
    private static readonly HashSet<int> PathTiles = new() { 0, 1, 2, 3, 40, 41, 42, 43, 80, 81, 82, 83, 120, 121, 122, 123 };
    private const int Border = 124;
    private static readonly (int, int)[] BayColumns = { (4, 39), (44, 79), (84, 119) };
    private static readonly (int, int)[] BayRows = { (4, 39), (44, 79), (84, 119) };
    public static readonly string[] BayKeys = { "steps", "materials", "water", "structure", "feature", "objects", "dig", "walls", "open" };

    private static readonly Dictionary<string, (int Col, int Row)> BayPosition = BayKeys.ToDictionary(
        key => key, key => (Array.IndexOf(BayKeys, key) % 3, Array.IndexOf(BayKeys, key) / 3));

    public static readonly IReadOnlyDictionary<string, (int X0, int Y0, int X1, int Y1)> BayRects = BayKeys.ToDictionary(
        key => key,
        key => (BayColumns[BayPosition[key].Col].Item1, BayRows[BayPosition[key].Row].Item1,
            BayColumns[BayPosition[key].Col].Item2, BayRows[BayPosition[key].Row].Item2));

    public static readonly IReadOnlyList<GeneratorBay> Bays =
        BayKeys.Select(key => new GeneratorBay(key, BayRects[key].X0, BayRects[key].Y0, 36, 36)).ToList();

    public static (double X, double Y, int H) Spawn(long seed, LabOptions options)
    {
        var (col, row) = BayPosition[options.SpawnBay];
        var (x0, x1) = BayColumns[col];
        var northPathY = new[] { 1.5, 41.5, 81.5 }[row];
        return ((x0 + x1) / 2.0 + 0.0, northPathY, BaseH);
    }

    private static void Paths(GenCanvas canvas, int asphalt)
    {
        for (var x = 0; x < canvas.Width; x++)
        for (var y = 0; y < canvas.Height; y++)
        {
            if (x >= Border || y >= Border) continue;
            if (PathTiles.Contains(x) || PathTiles.Contains(y))
            {
                canvas.SetGround(x, y, BaseH);
                canvas.SetSurface(x, y, asphalt);
            }
        }
    }

    private static void Steps(GenCanvas canvas, int concrete)
    {
        var (x0, y0, _, y1) = BayRects["steps"];
        int[] heights = { 0, 1, 3, 6, 10, 16, 24 };
        for (var index = 0; index < heights.Length; index++)
            for (var y = y0; y <= y1; y++)
            for (var x = x0 + index * 4; x < x0 + index * 4 + 4; x++)
                canvas.SetGround(x, y, BaseH + heights[index]);
        // A 1:1 ramp and a six-step stair in the flat eastern part of the bay.
        for (var y = y0; y < y0 + 4; y++)
        for (var x = x0 + 28; x < x0 + 35; x++)
            canvas.SetGround(x, y, BaseH + Math.Min(x - (x0 + 28), 6));
        for (var y = y0 + 8; y < y0 + 12; y++)
        for (var step = 0; step < 6; step++)
        {
            var x = x0 + 28 + step;
            canvas.SetGround(x, y, BaseH + step + 1);
            canvas.SetSurface(x, y, concrete);
        }
    }

    private static void Materials(GenCanvas canvas, MaterialRegistry registry)
    {
        var (x0, y0, _, _) = BayRects["materials"];
        for (var index = 0; index < registry.Materials.Count; index++)
        {
            var material = registry.Materials[index];
            int col = index % 6, row = index / 6;
            int px = x0 + col * 6, py = y0 + row * 6;
            for (var y = py; y < py + 4; y++)
            for (var x = px; x < px + 4; x++)
            {
                canvas.SetGround(x, y, BaseH);
                canvas.SetSurface(x, y, material.Id);
            }
            for (var y = py; y < py + 2; y++)
            for (var x = px + 4; x < px + 6; x++)
            {
                canvas.SetGround(x, y, BaseH + 2);
                canvas.SetSurface(x, y, material.Id);
            }
        }
    }

    private static void Water(GenCanvas canvas, int shallow, int deep)
    {
        const int centreX = 101, centreY = 21;
        var rect = BayRects["water"];
        for (var y = rect.Y0; y <= rect.Y1; y++)
        for (var x = rect.X0; x <= rect.X1; x++)
        {
            var distance = (x - centreX) * (x - centreX) + (y - centreY) * (y - centreY);
            if (distance <= 12 * 12)
            {
                canvas.SetGround(x, y, BaseH - (distance > 7 * 7 ? 2 : 4));
                canvas.SetSurface(x, y, distance > 7 * 7 ? shallow : deep);
            }
        }
    }

    private static void Structure(GenCanvas canvas, MaterialRegistry registry)
    {
        const int x0 = 17, y0 = 45;
        const int x1 = x0 + 9, y1 = y0 + 7;
        var brick = registry["brick"].Id;
        var concrete = registry["concrete"].Id;
        var wood = registry["wood_floor"].Id;
        int[] floorMaterials = { concrete, concrete, wood };
        for (var z = 0; z < floorMaterials.Length; z++)
        {
            var level = canvas.Level(z);
            var floorH = BaseH + z * 6;
            for (var y = y0 + 1; y < y1; y++)
            for (var x = x0 + 1; x < x1; x++)
                level.SetFloor(x, y, floorH, floorMaterials[z]);
            for (var y = y0 + 1; y < y1; y++)
            {
                level.SetWall(x0 + 1, y, EdgeDirection.West, brick);
                level.SetWall(x1, y, EdgeDirection.West, brick);
            }
            for (var x = x0 + 1; x < x1; x++)
            {
                level.SetWall(x, y0 + 1, EdgeDirection.North, brick);
                level.SetWall(x, y1, EdgeDirection.North, brick);
            }
            if (z == 0) level.SetEdge(x0 + 1, (y0 + y1) / 2, ChunkConst.EdgeWDoorway);
            else
                for (var x = x0 + 1; x < x1; x++)
                    if (x % 3 == 0) level.SetEdge(x, y0 + 1, ChunkConst.EdgeNWindow);
        }
        var roof = canvas.Level(2);
        for (var y = y0 + 1; y < y1; y++)
        for (var x = x0 + 1; x < x1; x++)
            roof.SetFlag(x, y, ChunkConst.LevelRoof);
        // Ground-to-first and first-to-roof stairs, with open stairwells in the slabs above.
        for (var index = 0; index < 6; index++)
        {
            var x = x0 + 1 + index;
            var stairH = BaseH + 1 + index;
            canvas.Level(PyMath.FloorDiv(stairH, 6)).SetFloor(x, y0 + 1, stairH, concrete);
            if (index < 3) canvas.Level(1).ClearFloor(x, y0 + 1);
        }
        for (var index = 0; index < 6; index++)
        {
            var x = x0 + 1 + index;
            var stairH = BaseH + 7 + index;
            canvas.Level(PyMath.FloorDiv(stairH, 6)).SetFloor(x, y0 + 2, stairH, concrete);
            if (index < 3) canvas.Level(2).ClearFloor(x, y0 + 2);
        }
    }

    private static List<GeneratedObject> Objects(MaterialRegistry registry)
    {
        var (x0, y0, _, _) = BayRects["objects"];
        var catalog = ObjectCatalog.Load(registry);
        var rowY = y0 + 2;
        var objects = new List<GeneratedObject>();
        for (var index = 0; index < catalog.Kinds.Count; index++)
        {
            var kind = catalog.Kinds[index];
            var x = x0 + 2 + index * 3;
            objects.Add(new GeneratedObject(kind.Key, x, rowY, BaseH));
            // `parent=index` is the kind's index, as in lab.py, not the list position.
            if (kind.Container is not null) objects.Add(new GeneratedObject("apple", x, rowY, BaseH, Parent: index));
        }
        int stackX = x0 + 2, stackY = y0 + 8;
        objects.Add(new GeneratedObject("chest", stackX, stackY, BaseH));
        objects.Add(new GeneratedObject("chest", stackX, stackY, BaseH + 2));
        return objects;
    }

    private static void Dig(GenCanvas canvas)
    {
        const int pitX0 = 16, pitY0 = 98;
        for (var y = pitY0; y < pitY0 + 6; y++)
        for (var x = pitX0; x < pitX0 + 6; x++)
            canvas.SetGround(x, y, BaseH - 4);
        for (var step = 0; step < 4; step++) canvas.SetGround(pitX0 + 6 + step, pitY0 + 2, BaseH - 4 + step + 1);
    }

    private static List<GeneratedObject> Walls(GenCanvas canvas, MaterialRegistry registry)
    {
        var (x0, y0, _, _) = BayRects["walls"];
        var level = canvas.Level(0);
        var objects = new List<GeneratedObject>();
        var building = registry.Materials.Where(m => m.Tags.Contains("building")).ToList();
        for (var index = 0; index < building.Count; index++)
        {
            var segmentY = y0 + 2 + index * 4;
            for (var offset = 0; offset < 6; offset++) level.SetWall(x0 + 2 + offset, segmentY, EdgeDirection.North, building[index].Id);
            level.SetEdge(x0 + 3, segmentY, ChunkConst.EdgeNDoorway);
            level.SetEdge(x0 + 5, segmentY, ChunkConst.EdgeNWindow);
            objects.Add(new GeneratedObject("chest", x0 + 4, segmentY + 1, BaseH));
        }
        return objects;
    }

    public static GeneratedWorld Generate(long seed, LabOptions options, MaterialRegistry? registry = null)
    {
        registry ??= MaterialRegistry.Load();
        var canvas = new GenCanvas(Chunks, Chunks, BaseH, registry["grass"].Id, "lab", GenVersion);
        Paths(canvas, registry["asphalt"].Id);
        Steps(canvas, registry["concrete"].Id);
        Materials(canvas, registry);
        Water(canvas, registry["water_shallow"].Id, registry["water_deep"].Id);
        Structure(canvas, registry);
        var objects = Objects(registry);
        Dig(canvas);
        objects.AddRange(Walls(canvas, registry));
        if (options.Feature != "none")
        {
            var rect = BayRects["feature"];
            objects.AddRange(Relief.Stamp(canvas, rect, seed, options.Relief));
        }
        var world = canvas.Build(Spawn(seed, options));
        return new GeneratedWorld(world.Chunks, world.Levels, world.Spawn, world.GenVersion, objects, "lab");
    }
}

/// <summary>Noise hills inside a rectangle, blended to the flat base at the border (<c>features/relief.py</c>).</summary>
public static class Relief
{
    public static List<GeneratedObject> Stamp(GenCanvas canvas, (int X0, int Y0, int X1, int Y1) rect, long seed, ReliefOptions options)
    {
        var (x0, y0, x1, y1) = rect;
        var noise = new ValueNoise(seed);
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var value = noise.Fbm(x / options.Scale, y / options.Scale, options.Octaves, gain: options.Gain);
            double blend;
            if (options.Edge > 0)
            {
                var distance = Math.Min(Math.Min(x - x0, x1 - x), Math.Min(y - y0, y1 - y));
                blend = Math.Min(1.0, Math.Max(0.0, (double)distance / options.Edge));
            }
            else
            {
                blend = 1.0;
            }
            canvas.SetGround(x, y, 2 + PyMath.Round(value * options.Amplitude * blend));
        }
        return new List<GeneratedObject>();
    }
}
