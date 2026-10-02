namespace EtherBound.Sim.World.Gen;

/// <summary>
/// What a streaming generator hands the engine: pristine chunks on demand and the bare surface sample
/// the minimap reads. Both are pure functions of the world seed and options.
/// </summary>
public interface IChunkSource
{
    (Chunk Chunk, IReadOnlyList<ChunkLevel> Levels)? Generate(int cx, int cy);

    TerrainCell Sample(int x, int y);
}

/// <summary>The endless world's source: a <see cref="TerrainField"/> cut into chunks.</summary>
public sealed class EndlessSource : IChunkSource
{
    private readonly TerrainField _field;

    public EndlessSource(long seed, EndlessOptions options, MaterialRegistry registry) =>
        _field = new TerrainField(seed, options, registry);

    public (Chunk Chunk, IReadOnlyList<ChunkLevel> Levels)? Generate(int cx, int cy) => EndlessWorld.GenerateChunk(_field, cx, cy);

    public TerrainCell Sample(int x, int y) => _field.At(x, y);
}

/// <summary>
/// The <c>infinite</c> generator: a streaming world whose chunks are a pure function of seed,
/// options, version and chunk coordinates (Dev-010). It builds no world up front: the engine asks for
/// a chunk when something reads it, and a pristine chunk is a cache that is never persisted.
/// </summary>
public static class EndlessWorld
{
    public const int GenVersion = 1;

    /// <summary>Chunk coordinates stay within this limit, so tile coordinates fit an <c>int</c> with room to spare.</summary>
    public const int MaxChunkCoord = 32768;

    private const int SpawnSearchRadius = 2000;
    private const int SpawnReachNeeded = 1200;
    private const int SpawnReachRadius = 40;

    public static bool InRange(int cx, int cy) => Math.Abs(cx) <= MaxChunkCoord && Math.Abs(cy) <= MaxChunkCoord;

    public static (Chunk Chunk, IReadOnlyList<ChunkLevel> Levels)? GenerateChunk(TerrainField field, int cx, int cy)
    {
        if (!InRange(cx, cy)) return null;
        var ground = new short[ChunkConst.CellCount];
        var surface = new ushort[ChunkConst.CellCount];
        for (var ly = 0; ly < ChunkConst.Size; ly++)
        for (var lx = 0; lx < ChunkConst.Size; lx++)
        {
            var cell = field.At(cx * ChunkConst.Size + lx, cy * ChunkConst.Size + ly);
            ground[ly * ChunkConst.Size + lx] = (short)cell.Height;
            surface[ly * ChunkConst.Size + lx] = (ushort)cell.Material;
        }
        var strata = field.StrataAt(cx * ChunkConst.Size + ChunkConst.Size / 2, cy * ChunkConst.Size + ChunkConst.Size / 2);
        return (new Chunk(cx, cy, ground, surface, strata, genVersion: GenVersion), Array.Empty<ChunkLevel>());
    }

    /// <summary>Whether a walkable tile belongs to a large enough connected patch of ground to start on.</summary>
    private static bool Roomy(TerrainField field, int x, int y)
    {
        var seen = new HashSet<(int, int)> { (x, y) };
        var queue = new Queue<(int X, int Y, int H)>();
        queue.Enqueue((x, y, field.At(x, y).Height));
        while (queue.Count > 0 && seen.Count < SpawnReachNeeded)
        {
            var (cx, cy, ch) = queue.Dequeue();
            foreach (var (dx, dy) in Orthogonal)
            {
                int nx = cx + dx, ny = cy + dy;
                if (Math.Abs(nx - x) > SpawnReachRadius || Math.Abs(ny - y) > SpawnReachRadius || !seen.Add((nx, ny))) continue;
                if (field.StandingHeight(nx, ny) is not { } nh || Math.Abs(nh - ch) > 1) continue;
                queue.Enqueue((nx, ny, nh));
            }
        }
        return seen.Count >= SpawnReachNeeded;
    }

    private static readonly (int, int)[] Orthogonal = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>
    /// The first inland, walkable tile on a square spiral out from the origin whose surroundings are
    /// connected ground, as a pure and cheap search over the field alone.
    /// </summary>
    public static (double X, double Y, int H) Spawn(TerrainField field)
    {
        // Grass first: a world that starts on bare dirt or sand reads as a desert. Any walkable ground is the fallback.
        foreach (var grassOnly in new[] { true, false })
        for (var ring = 0; ring <= SpawnSearchRadius; ring += 4)
        {
            for (var step = -ring; step <= ring; step += 4)
            {
                foreach (var (x, y) in RingPoints(ring, step))
                {
                    var cell = field.At(x, y);
                    if (!field.Walkable(cell) || (grassOnly && !field.IsGrass(cell))) continue;
                    if (cell.Height < field.SeaLevel + 4 || cell.Height > field.SeaLevel + 28) continue;
                    if (Roomy(field, x, y)) return (x + 0.5, y + 0.5, cell.Height);
                }
            }
        }
        // Practically unreachable (the whole disc would have to be sea or rock); a defined fallback.
        return (0.5, 0.5, field.At(0, 0).Height);
    }

    private static IEnumerable<(int X, int Y)> RingPoints(int ring, int step)
    {
        if (ring == 0)
        {
            yield return (0, 0);
            yield break;
        }
        yield return (step, -ring);
        yield return (step, ring);
        if (Math.Abs(step) != ring)
        {
            yield return (-ring, step);
            yield return (ring, step);
        }
    }

    /// <summary>
    /// The spawn kit, placed once at <c>NewGame</c> through the ordinary object commit. Streamed chunks
    /// carry no objects: ids are assigned in commit order, so objects created by a read would make
    /// them depend on the order in which the player explored.
    /// </summary>
    public static IReadOnlyList<GeneratedObject> SpawnKit(TerrainField field, (double X, double Y, int H) spawn)
    {
        int sx = (int)Math.Floor(spawn.X), sy = (int)Math.Floor(spawn.Y);
        var tiles = new List<(int X, int Y, int H)>();
        for (var ring = 2; ring <= 8 && tiles.Count < 4; ring++)
        for (var dy = -ring; dy <= ring && tiles.Count < 4; dy++)
        for (var dx = -ring; dx <= ring && tiles.Count < 4; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
            int x = sx + dx, y = sy + dy;
            if (field.StandingHeight(x, y) is not { } h || Math.Abs(h - spawn.H) > 2) continue;
            // Keep the kit on flat ground and apart, so no item rests on a slope or on another one.
            if (tiles.Any(t => Math.Abs(t.X - x) + Math.Abs(t.Y - y) < 2)) continue;
            tiles.Add((x, y, h));
        }
        if (tiles.Count < 4) return Array.Empty<GeneratedObject>();
        return new[]
        {
            new GeneratedObject("shovel", tiles[0].X, tiles[0].Y, tiles[0].H),
            new GeneratedObject("backpack", tiles[1].X, tiles[1].Y, tiles[1].H),
            new GeneratedObject("chest", tiles[2].X, tiles[2].Y, tiles[2].H),
            new GeneratedObject("apple", tiles[2].X, tiles[2].Y, tiles[2].H, Quantity: 3, Parent: 2),
            new GeneratedObject("bottle", tiles[2].X, tiles[2].Y, tiles[2].H, Quantity: 2, Parent: 2),
            new GeneratedObject("sledgehammer", tiles[3].X, tiles[3].Y, tiles[3].H),
        };
    }

    /// <summary>The streaming world as <see cref="GeneratedWorld"/>: no chunks, only the spawn and its kit.</summary>
    public static GeneratedWorld Generate(long seed, EndlessOptions options, MaterialRegistry registry)
    {
        var field = new TerrainField(seed, options, registry);
        var spawn = Spawn(field);
        return new GeneratedWorld(new Dictionary<(int, int), Chunk>(), new Dictionary<(int, int, int), ChunkLevel>(), spawn,
            GenVersion, SpawnKit(field, spawn), "infinite");
    }
}
