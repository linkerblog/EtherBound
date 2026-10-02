namespace EtherBound.Sim.World;

public readonly record struct StandingSurface(int H, int MaterialId, int Z);

/// <summary>An object lying directly on a tile, as the standing rule needs to know it.</summary>
public sealed record TileObject(int Id, string Kind, int X, int Y, int H, int Quantity, bool? Open = null);

public enum EdgeDirection
{
    North,
    West,
}

/// <summary>
/// In-memory sparse world cache used by movement and navigation (<c>world/grid.py</c>). Level
/// order per chunk is insertion order, as Python's dict keeps it, because a few rules take the
/// first or the last match.
/// </summary>
public sealed class WorldGrid
{
    private readonly Dictionary<(int, int), Chunk> _chunks = new();
    private readonly Dictionary<(int, int, int), ChunkLevel> _levels = new();
    private readonly Dictionary<(int, int), List<ChunkLevel>> _levelsByChunk = new();
    private readonly Dictionary<(int, int), TileObject[]> _objectChunks = new();
    private readonly Dictionary<(int, int), IReadOnlyList<StandingSurface>> _standingCache = new();
    private readonly Dictionary<(int, int, int, int, int?), bool> _stepCache = new();
    private readonly Func<int, int, (Chunk Chunk, IEnumerable<ChunkLevel> Levels)?>? _chunkLoader;
    private readonly Func<int, int, IEnumerable<TileObject>>? _objectIndexer;
    private readonly HashSet<(int, int)> _missingChunks = new();

    /// <param name="chunkLoader">Streaming worlds (Dev-010): builds a chunk the first time something reads it.</param>
    /// <param name="objectIndexer">Gives a freshly loaded chunk its tile-object index, since only loaded chunks have one.</param>
    public WorldGrid(IEnumerable<Chunk>? chunks = null, IEnumerable<ChunkLevel>? levels = null,
        MaterialRegistry? registry = null, Func<int, int, (Chunk, IEnumerable<ChunkLevel>)?>? chunkLoader = null,
        ObjectCatalog? catalog = null, Func<int, int, IEnumerable<TileObject>>? objectIndexer = null)
    {
        Registry = registry ?? MaterialRegistry.Load();
        Catalog = catalog ?? ObjectCatalog.Load();
        _chunkLoader = chunkLoader;
        _objectIndexer = objectIndexer;
        foreach (var chunk in chunks ?? Array.Empty<Chunk>()) AddChunk(chunk);
        foreach (var level in levels ?? Array.Empty<ChunkLevel>()) AddLevel(level);
    }

    public MaterialRegistry Registry { get; set; }
    public ObjectCatalog Catalog { get; }
    public long NavigationRevision { get; private set; }

    public IReadOnlyDictionary<(int, int), Chunk> Chunks => _chunks;
    public IReadOnlyDictionary<(int, int, int), ChunkLevel> Levels => _levels;

    private void InvalidateNavigation()
    {
        NavigationRevision++;
        _standingCache.Clear();
        _stepCache.Clear();
    }

    public void AddChunk(Chunk chunk)
    {
        _chunks[(chunk.Cx, chunk.Cy)] = chunk;
        _missingChunks.Remove((chunk.Cx, chunk.Cy));
        InvalidateNavigation();
    }

    public void AddLevel(ChunkLevel level)
    {
        _levels[(level.Cx, level.Cy, level.Z)] = level;
        if (!_levelsByChunk.TryGetValue((level.Cx, level.Cy), out var list)) _levelsByChunk[(level.Cx, level.Cy)] = list = new();
        var at = list.FindIndex(l => l.Z == level.Z);
        if (at >= 0) list[at] = level;
        else list.Add(level);
        InvalidateNavigation();
    }

    public Chunk? Chunk(int cx, int cy)
    {
        if (_chunks.TryGetValue((cx, cy), out var chunk) || _chunkLoader is null || _missingChunks.Contains((cx, cy)))
            return chunk;
        var loaded = _chunkLoader(cx, cy);
        if (loaded is null)
        {
            _missingChunks.Add((cx, cy));
            return null;
        }
        var (loadedChunk, loadedLevels) = loaded.Value;
        if ((loadedChunk.Cx, loadedChunk.Cy) != (cx, cy)) throw new InvalidOperationException("chunk loader returned a different chunk");
        var levels = loadedLevels.ToList();
        if (levels.Any(l => (l.Cx, l.Cy) != (cx, cy))) throw new InvalidOperationException("chunk loader returned levels from another chunk");
        // A loaded chunk changes nothing the caches already answered (nothing was cached for its tiles),
        // so, unlike a modification, it must not bump the navigation revision: Extras would replan on
        // every chunk the player merely walks towards.
        _chunks[(cx, cy)] = loadedChunk;
        foreach (var level in levels)
        {
            _levels[(level.Cx, level.Cy, level.Z)] = level;
            if (!_levelsByChunk.TryGetValue((cx, cy), out var list)) _levelsByChunk[(cx, cy)] = list = new();
            list.Add(level);
        }
        if (_objectIndexer is not null)
        {
            var objects = _objectIndexer(cx, cy).ToArray();
            if (objects.Length > 0) _objectChunks[(cx, cy)] = objects;
        }
        return loadedChunk;
    }

    public IReadOnlyList<ChunkLevel> LevelsOfChunk(int cx, int cy) => ChunkLevels(cx, cy);

    /// <summary>Whether the grid builds chunks on demand instead of holding a fixed set.</summary>
    public bool Streaming => _chunkLoader is not null;

    /// <summary>
    /// Forgets a loaded chunk, its levels and its object index; the loader rebuilds it on the next read.
    /// Only the engine calls this, and only for a chunk with nothing uncommitted.
    /// </summary>
    public void Evict(int cx, int cy)
    {
        if (!_chunks.Remove((cx, cy))) return;
        foreach (var level in ChunkLevels(cx, cy)) _levels.Remove((level.Cx, level.Cy, level.Z));
        _levelsByChunk.Remove((cx, cy));
        _objectChunks.Remove((cx, cy));
        InvalidateNavigation();
    }

    public ChunkLevel? Level(int cx, int cy, int z) => _levels.GetValueOrDefault((cx, cy, z));

    private IReadOnlyList<ChunkLevel> ChunkLevels(int cx, int cy) =>
        _levelsByChunk.TryGetValue((cx, cy), out var list) ? list : Array.Empty<ChunkLevel>();

    /// <summary>Replace a chunk's tile-object index; the engine calls this after every commit.</summary>
    public void SetChunkObjects(int cx, int cy, IEnumerable<TileObject> objects)
    {
        var loaded = objects.ToArray();
        if (loaded.Length > 0) _objectChunks[(cx, cy)] = loaded;
        else _objectChunks.Remove((cx, cy));
        InvalidateNavigation();
    }

    public IReadOnlyList<TileObject> ObjectsAt(int x, int y)
    {
        var (cx, cy, _, _) = ChunkCoords(x, y);
        return _objectChunks.TryGetValue((cx, cy), out var objects)
            ? objects.Where(o => o.X == x && o.Y == y).ToList()
            : Array.Empty<TileObject>();
    }

    public IReadOnlyList<TileObject> ObjectsAtChunk(int cx, int cy) =>
        _objectChunks.TryGetValue((cx, cy), out var objects) ? objects.OrderBy(o => o.Id).ToList() : Array.Empty<TileObject>();

    public ObjectKind? KindOf(TileObject obj) => Catalog.Get(obj.Kind);

    private bool ObjectSolidAt(int x, int y, int h)
    {
        foreach (var obj in ObjectsAt(x, y))
        {
            var kind = KindOf(obj);
            if (kind is null || !kind.Solid) continue;
            // A solid object resting at `obj.H` fills the cells `obj.H + 1 .. obj.H + height`.
            if (obj.H < h && h <= obj.H + kind.Height) return true;
        }
        return false;
    }

    public static (int Cx, int Cy, int LocalX, int LocalY) ChunkCoords(int x, int y)
    {
        var cx = PyMath.FloorDiv(x, ChunkConst.Size);
        var cy = PyMath.FloorDiv(y, ChunkConst.Size);
        return (cx, cy, x - cx * ChunkConst.Size, y - cy * ChunkConst.Size);
    }

    private (Chunk Chunk, int Index)? Cell(int x, int y)
    {
        var (cx, cy, lx, ly) = ChunkCoords(x, y);
        var chunk = Chunk(cx, cy);
        return chunk is null ? null : (chunk, World.Chunk.Index(lx, ly));
    }

    private IEnumerable<(ChunkLevel Level, int Index)> LevelsAt(int x, int y)
    {
        if (Cell(x, y) is not { } cell) yield break;
        foreach (var level in ChunkLevels(cell.Chunk.Cx, cell.Chunk.Cy)) yield return (level, cell.Index);
    }

    private bool VoidAt(int x, int y, int h)
    {
        var z = PyMath.FloorDiv(h, ChunkConst.LevelH);
        if (Cell(x, y) is not { } cell) return false;
        var level = Level(cell.Chunk.Cx, cell.Chunk.Cy, z);
        return level is not null && (level.Flags[cell.Index] & ChunkConst.LevelVoid) != 0;
    }

    public bool IsVoid(int x, int y, int h) => VoidAt(x, y, h);

    private int Stratum(Chunk chunk, int depth)
    {
        var selected = "rock";
        foreach (var layer in chunk.Strata.OrderBy(s => s.Depth).ThenBy(s => s.Key, StringComparer.Ordinal))
        {
            if (depth >= layer.Depth) selected = layer.Key;
            else break;
        }
        return Registry.Get(selected)?.Id ?? 0;
    }

    // Depth counts from the original ground, or a dug surface would drag the strata down.
    private int UndergroundMaterial(Chunk chunk, int index, int h) => Stratum(chunk, chunk.GroundH[index] + chunk.Dug[index] - h);

    /// <summary>Material id of the volume <paramref name="depthFromOriginal"/> half-metres below the original ground.</summary>
    public int StratumAt(int x, int y, int depthFromOriginal) => Cell(x, y) is { } cell ? Stratum(cell.Chunk, depthFromOriginal) : 0;

    /// <summary><c>(ground_h, surface_mat, dug)</c> of a tile, or null outside the loaded world.</summary>
    public (int GroundH, int SurfaceMat, int Dug)? GroundAt(int x, int y)
    {
        if (Cell(x, y) is not { } cell) return null;
        return (cell.Chunk.GroundH[cell.Index], cell.Chunk.SurfaceMat[cell.Index], cell.Chunk.Dug[cell.Index]);
    }

    /// <summary>Floor heights of every level slab on a tile.</summary>
    public IReadOnlyList<int> FloorsAt(int x, int y) =>
        LevelsAt(x, y).Where(l => l.Level.FloorH[l.Index] != ChunkConst.NoFloor).Select(l => (int)l.Level.FloorH[l.Index]).ToList();

    /// <summary>Dig 0.5 m out of a tile's ground. Only the world engine calls this.</summary>
    public Chunk LowerGround(int x, int y)
    {
        if (Cell(x, y) is not { } cell) throw new KeyNotFoundException($"no chunk at tile {x},{y}");
        var (chunk, index) = cell;
        var ground = (short[])chunk.GroundH.Clone();
        var surface = (ushort[])chunk.SurfaceMat.Clone();
        var dug = (byte[])chunk.Dug.Clone();
        ground[index] -= 1;
        dug[index] += 1;
        // The new surface is the top of the volume below the removed one.
        surface[index] = (ushort)Stratum(chunk, dug[index] + 1);
        var lowered = chunk.With(ground, surface, dug, chunk.Revision + 1);
        AddChunk(lowered);
        return lowered;
    }

    /// <summary>Whether a half-metre volume is occupied, by terrain or a solid object.</summary>
    public bool SolidAt(int x, int y, int h) => TerrainSolidAt(x, y, h) || ObjectSolidAt(x, y, h);

    /// <summary>Whether a half-metre volume is occupied by terrain, excluding objects.</summary>
    public bool TerrainSolidAt(int x, int y, int h)
    {
        if (Cell(x, y) is not { } cell) return true;
        var (chunk, index) = cell;
        foreach (var level in ChunkLevels(chunk.Cx, chunk.Cy))
        {
            if (level.FloorH[index] == h)
            {
                var material = Registry.Get(level.FloorMat[index]);
                return material is null || material.Solid;
            }
        }
        if (h >= chunk.GroundH[index] || VoidAt(x, y, h)) return false;
        var underground = Registry.Get(UndergroundMaterial(chunk, index, h));
        return underground is null || underground.Solid;
    }

    private bool Headroom(int x, int y, int h)
    {
        for (var offset = 1; offset < 4; offset++)
            if (SolidAt(x, y, h + offset)) return false;
        return true;
    }

    public IReadOnlyList<StandingSurface> StandingSurfaces(int x, int y)
    {
        if (_standingCache.TryGetValue((x, y), out var cached)) return cached;
        if (Cell(x, y) is not { } cell) return _standingCache[(x, y)] = Array.Empty<StandingSurface>();
        var (chunk, index) = cell;
        var result = new List<StandingSurface>();
        int ground = chunk.GroundH[index];
        var groundMaterial = Registry.Get(chunk.SurfaceMat[index]);
        if (!VoidAt(x, y, ground) && groundMaterial is not null && groundMaterial.Walkable && Headroom(x, y, ground))
            result.Add(new StandingSurface(ground, groundMaterial.Id, PyMath.FloorDiv(ground, 6)));
        foreach (var level in ChunkLevels(chunk.Cx, chunk.Cy))
        {
            int floor = level.FloorH[index];
            if (floor == ChunkConst.NoFloor || !Headroom(x, y, floor)) continue;
            var material = Registry.Get(level.FloorMat[index]);
            if (material is not null && material.Walkable) result.Add(new StandingSurface(floor, material.Id, PyMath.FloorDiv(floor, 6)));
        }
        foreach (var obj in ObjectsAt(x, y))
        {
            var kind = KindOf(obj);
            if (kind is null || !kind.Surface) continue;
            var top = obj.H + kind.Height;
            var material = Registry.Get(kind.Material);
            if (material is not null && material.Walkable && Headroom(x, y, top))
                result.Add(new StandingSurface(top, material.Id, PyMath.FloorDiv(top, 6)));
        }
        return _standingCache[(x, y)] = Dedupe(result);
    }

    // `{surface.h: surface}` keeps the last surface per height; then sorted by height.
    private static IReadOnlyList<StandingSurface> Dedupe(List<StandingSurface> surfaces)
    {
        var byH = new Dictionary<int, StandingSurface>();
        foreach (var surface in surfaces) byH[surface.H] = surface;
        return Array.AsReadOnly(byH.Values.OrderBy(s => s.H).ToArray());
    }

    /// <summary>Where an object can be placed: standable surfaces without headroom, plus surface tops.</summary>
    public IReadOnlyList<StandingSurface> RestingSurfaces(int x, int y)
    {
        if (Cell(x, y) is not { } cell) return Array.Empty<StandingSurface>();
        var (chunk, index) = cell;
        var result = new List<StandingSurface>();
        int ground = chunk.GroundH[index];
        var groundMaterial = Registry.Get(chunk.SurfaceMat[index]);
        if (!VoidAt(x, y, ground) && groundMaterial is not null && groundMaterial.Walkable)
            result.Add(new StandingSurface(ground, groundMaterial.Id, PyMath.FloorDiv(ground, 6)));
        foreach (var level in ChunkLevels(chunk.Cx, chunk.Cy))
        {
            int floor = level.FloorH[index];
            if (floor == ChunkConst.NoFloor) continue;
            var material = Registry.Get(level.FloorMat[index]);
            if (material is not null && material.Walkable) result.Add(new StandingSurface(floor, material.Id, PyMath.FloorDiv(floor, 6)));
        }
        foreach (var obj in ObjectsAt(x, y))
        {
            var kind = KindOf(obj);
            if (kind is null || !kind.Surface) continue;
            var top = obj.H + kind.Height;
            var material = Registry.Get(kind.Material);
            if (material is not null && material.Walkable) result.Add(new StandingSurface(top, material.Id, PyMath.FloorDiv(top, 6)));
        }
        return Dedupe(result);
    }

    private bool WallOnEdge(int x, int y, EdgeDirection direction, int h)
    {
        if (Cell(x, y) is not { } cell) return true;
        var (chunk, index) = cell;
        var zMin = PyMath.FloorDiv(h, 6) - 1;
        var zMax = PyMath.FloorDiv(h + 4, 6) + 1;
        for (var z = zMin; z <= zMax; z++)
        {
            var level = Level(chunk.Cx, chunk.Cy, z);
            if (level is null) continue;
            var wall = direction == EdgeDirection.North ? level.WallN[index] : level.WallW[index];
            if (wall == 0) continue;
            int bottom = level.FloorH[index];
            if (bottom == ChunkConst.NoFloor) bottom = WallBase(chunk, index, level);
            if (bottom < h + 4 && bottom + 6 > h)
            {
                var flags = level.EdgeFlags[index];
                var doorway = direction == EdgeDirection.North
                    ? (flags & ChunkConst.EdgeNDoorway) != 0
                    : (flags & ChunkConst.EdgeWDoorway) != 0;
                if (!doorway) return true;
            }
        }
        return false;
    }

    /// <summary>A floorless wall stands on the highest support below its band.</summary>
    private int WallBase(Chunk chunk, int index, ChunkLevel level)
    {
        var limit = (level.Z + 1) * 6;
        var best = int.MinValue;
        if (chunk.GroundH[index] < limit) best = chunk.GroundH[index];
        foreach (var lower in ChunkLevels(chunk.Cx, chunk.Cy))
        {
            if (lower.Z >= level.Z) continue;
            int floor = lower.FloorH[index];
            if (floor != ChunkConst.NoFloor && floor < limit) best = Math.Max(best, floor);
        }
        return best == int.MinValue ? level.Z * 6 : best;
    }

    /// <summary>Whether the shared edge blocks a two-metre body at <paramref name="h"/>.</summary>
    public bool WallBetween(int x1, int y1, int x2, int y2, int h) => (x2 - x1, y2 - y1) switch
    {
        (0, -1) => WallOnEdge(x1, y1, EdgeDirection.North, h),
        (0, 1) => WallOnEdge(x2, y2, EdgeDirection.North, h),
        (-1, 0) => WallOnEdge(x1, y1, EdgeDirection.West, h),
        (1, 0) => WallOnEdge(x2, y2, EdgeDirection.West, h),
        _ => throw new ArgumentException("wall_between requires orthogonally adjacent tiles"),
    };

    /// <summary>Interior H/V walls whose vertical span intersects a standing body at <paramref name="h"/>.</summary>
    public byte InteriorWallMaskAt(int x, int y, int h)
    {
        if (Cell(x, y) is not { } cell) return 0;
        var (chunk, index) = cell;
        var zMin = PyMath.FloorDiv(h, ChunkConst.LevelH) - 1;
        var zMax = PyMath.FloorDiv(h + 4, ChunkConst.LevelH) + 1;
        byte result = 0;
        for (var z = zMin; z <= zMax; z++)
        {
            var level = Level(chunk.Cx, chunk.Cy, z);
            if (level is null || level.SlotMat[index] == 0) continue;
            var slots = (byte)(level.SlotMask[index] & (ChunkConst.SlotHalfH | ChunkConst.SlotHalfV));
            if (slots == 0) continue;
            int bottom = level.FloorH[index];
            if (bottom == ChunkConst.NoFloor) bottom = WallBase(chunk, index, level);
            if (bottom < h + 4 && bottom + ChunkConst.LevelH > h) result |= slots;
        }
        return result;
    }

    /// <summary>Whether a specific interior slot occupies an exact half-metre height.</summary>
    public int? InteriorWallLevelAtHeight(int x, int y, byte slot, int h)
    {
        if (slot is not (ChunkConst.SlotHalfH or ChunkConst.SlotHalfV) || Cell(x, y) is not { } cell) return null;
        var (chunk, index) = cell;
        foreach (var level in ChunkLevels(chunk.Cx, chunk.Cy))
        {
            if (level.SlotMat[index] == 0 || (level.SlotMask[index] & slot) == 0) continue;
            int bottom = level.FloorH[index];
            if (bottom == ChunkConst.NoFloor) bottom = WallBase(chunk, index, level);
            if (bottom <= h && h < bottom + ChunkConst.LevelH) return level.Z;
        }
        return null;
    }

    /// <summary>Region occupied by a continuous world position in a tile at body height <paramref name="h"/>.</summary>
    public byte RegionAt(int x, int y, int h, double worldX, double worldY)
    {
        var mask = InteriorWallMaskAt(x, y, h);
        return WallRegions.At(mask, worldX - x, worldY - y);
    }

    /// <summary>Regions in a tile that can be entered from the direction of travel.</summary>
    public IReadOnlyList<byte> EntryRegions(int x, int y, int h, int dx, int dy)
    {
        var edge = (dx, dy) switch
        {
            (0, -1) => WallRegions.SouthEdge,
            (0, 1) => WallRegions.NorthEdge,
            (-1, 0) => WallRegions.EastEdge,
            (1, 0) => WallRegions.WestEdge,
            _ => throw new ArgumentException("region entry requires an orthogonal direction"),
        };
        return WallRegions.AtEdge(InteriorWallMaskAt(x, y, h), edge).ToArray();
    }

    private HashSet<(int, int)> StepPairs(int x1, int y1, int x2, int y2, int? h = null)
    {
        var source = StandingSurfaces(x1, y1).Where(s => h is null || s.H == h);
        var target = StandingSurfaces(x2, y2);
        var pairs = new HashSet<(int, int)>();
        foreach (var start in source)
        foreach (var end in target)
            if (Math.Abs(end.H - start.H) <= 1 && !WallBetween(x1, y1, x2, y2, start.H)) pairs.Add((start.H, end.H));
        return pairs;
    }

    private bool ClimbableAt(int x, int y) =>
        Cell(x, y) is { } cell && ChunkLevels(cell.Chunk.Cx, cell.Chunk.Cy).Any(l => (l.Flags[cell.Index] & ChunkConst.LevelClimbable) != 0);

    public bool CanStep(int x1, int y1, int x2, int y2, int? h = null)
    {
        var key = (x1, y1, x2, y2, h);
        if (_stepCache.TryGetValue(key, out var cached)) return cached;
        return _stepCache[key] = CanStepCore(x1, y1, x2, y2, h);
    }

    private bool CanStepCore(int x1, int y1, int x2, int y2, int? h)
    {
        int dx = x2 - x1, dy = y2 - y1;
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > 1) return false;
        if (dx == 0 && dy == 0)
        {
            if (!ClimbableAt(x1, y1)) return false;
            var all = StandingSurfaces(x1, y1);
            var from = all.Where(s => h is null || s.H == h);
            return from.Any(start => all.Any(end => start.H != end.H));
        }
        if (Math.Abs(dx) == 1 && Math.Abs(dy) == 1)
        {
            // A diagonal is legal only when both L-shaped detours around the corner are open at
            // consistent per-leg heights, and the move gains at most 0.5 m.
            var starts = StandingSurfaces(x1, y1);
            var firstMiddle = StandingSurfaces(x2, y1);
            var secondMiddle = StandingSurfaces(x1, y2);
            var ends = StandingSurfaces(x2, y2);
            foreach (var start in starts)
            {
                if (h is not null && start.H != h) continue;
                foreach (var end in ends)
                {
                    if (Math.Abs(end.H - start.H) > 1) continue;
                    foreach (var middle in firstMiddle)
                        if (Math.Abs(middle.H - start.H) <= 1 && !WallBetween(x1, y1, x2, y1, start.H) &&
                            Math.Abs(end.H - middle.H) <= 1 && !WallBetween(x2, y1, x2, y2, middle.H)) return true;
                    foreach (var middle in secondMiddle)
                        if (Math.Abs(middle.H - start.H) <= 1 && !WallBetween(x1, y1, x1, y2, start.H) &&
                            Math.Abs(end.H - middle.H) <= 1 && !WallBetween(x1, y2, x2, y2, middle.H)) return true;
                }
            }
            return false;
        }
        return StepPairs(x1, y1, x2, y2, h).Count > 0;
    }

    public (int MinX, int MinY, int MaxX, int MaxY)? Bounds()
    {
        if (_chunks.Count == 0) return null;
        var xs = _chunks.Keys.Select(k => k.Item1).ToList();
        var ys = _chunks.Keys.Select(k => k.Item2).ToList();
        return (xs.Min() * ChunkConst.Size, ys.Min() * ChunkConst.Size, (xs.Max() + 1) * ChunkConst.Size - 1, (ys.Max() + 1) * ChunkConst.Size - 1);
    }
}
