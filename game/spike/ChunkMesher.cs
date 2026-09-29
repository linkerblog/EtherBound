using System;
using System.Collections.Generic;
using Godot;

namespace EtherBound.Game.Spike;

/// <summary>
/// Builds one chunk's exposed geometry in world units (x, metres up, y). Vertex data carries what the
/// iso-space shader needs to address BitCanvas sheets: UV is the owner tile, UV2 is (face top in
/// half-metres, sheet layer), COLOR is the material colour with the face code in alpha.
/// </summary>
public sealed class ChunkMesher
{
    // Face codes, read back by the shader from COLOR.a * 8. Codes 0-2 are natural ground, the only
    // ones the cliff-cutaway window cuts; 6/7 are built slabs, so floors and stairs stay whole.
    public const int Top = 0, SouthFace = 1, EastFace = 2, WallNorth = 3, WallWest = 4, Prop = 5;
    public const int SlabTop = 6, SlabSide = 7;
    // A face with no grass lip (void-cut or boundary) never gets a cap row.
    private const int FillOnly = -9999;
    private const int BoundaryBottom = -12;
    private const float WallT = 0.25f;
    // Width of the silhouette strips the outline pass draws along a cut terrain's outer edges.
    private const float OutlineW = 0.06f;

    private readonly WorldDump _world;
    private readonly Dictionary<int, int> _topLayers;
    private readonly Dictionary<int, int> _sideLayers;
    private readonly Dictionary<int, int> _cliffSideLayers;
    private readonly HashSet<int> _roofBands;
    private readonly ShaderMaterial _terrainMaterial;
    private readonly ShaderMaterial _structureMaterial;
    private readonly ShaderMaterial _glassMaterial;
    private readonly ShaderMaterial _outlineMaterial;

    public ChunkMesher(WorldDump world, Dictionary<int, int> topLayers, Dictionary<int, int> sideLayers,
        Dictionary<int, int> cliffSideLayers,
        ShaderMaterial terrainMaterial, ShaderMaterial structureMaterial, ShaderMaterial glassMaterial,
        ShaderMaterial outlineMaterial)
    {
        _world = world;
        _topLayers = topLayers;
        _sideLayers = sideLayers;
        _cliffSideLayers = cliffSideLayers;
        _roofBands = RoofBands(world);
        _terrainMaterial = terrainMaterial;
        _structureMaterial = structureMaterial;
        _glassMaterial = glassMaterial;
        _outlineMaterial = outlineMaterial;
    }

    public sealed class Builder
    {
        public readonly List<Vector3> Verts = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Color> Colors = new();
        public readonly List<Vector2> Uv = new();
        public readonly List<Vector2> Uv2 = new();
        public readonly List<int> Indices = new();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color color, Vector2 owner, Vector2 extra)
        {
            var start = Verts.Count;
            foreach (var v in new[] { a, b, c, d })
            {
                Verts.Add(v);
                Normals.Add(n);
                Colors.Add(color);
                Uv.Add(owner);
                Uv2.Add(extra);
            }
            Indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        public void AddSurface(ArrayMesh mesh, Material material)
        {
            if (Verts.Count == 0) return;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = Verts.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = Normals.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = Colors.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = Uv.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = Uv2.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = Indices.ToArray();
            var surface = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(surface, material);
        }
    }

    private sealed class BandBuilder
    {
        public readonly Builder Terrain = new();
        public readonly Builder Structure = new();
        public readonly Builder Glass = new();
        public readonly Builder Outline = new();
    }

    internal sealed record BandGeometry(Builder Terrain, Builder Structure, Builder Glass, Builder Outline);

    public sealed class ChunkGeometry
    {
        internal ChunkGeometry(SortedDictionary<int, BandGeometry> bands) => Bands = bands;

        internal SortedDictionary<int, BandGeometry> Bands { get; }
    }

    public sealed record ChunkMeshes(ArrayMesh? Mesh);

    private static float M(int halfMetres) => halfMetres * 0.5f;

    private Color MaterialColor(int id, int code) =>
        _world.Materials.TryGetValue(id, out var m) ? new Color(m.Color) { A = code / 8f } : new Color(1, 0, 1, code / 8f);

    private int TopLayer(int id) => _topLayers.GetValueOrDefault(id, -1);
    private int SideLayer(int id) => _sideLayers.GetValueOrDefault(id, -1);

    public ChunkGeometry BuildGeometry(int cx, int cy)
    {
        var bands = new SortedDictionary<int, BandBuilder>();
        BandBuilder Band(int z)
        {
            if (!bands.TryGetValue(z, out var band)) bands[z] = band = new BandBuilder();
            return band;
        }

        for (var ly = 0; ly < WorldDump.ChunkSize; ly++)
        {
            for (var lx = 0; lx < WorldDump.ChunkSize; lx++)
            {
                var x = cx * WorldDump.ChunkSize + lx;
                var y = cy * WorldDump.ChunkSize + ly;
                if (_world.SolidTopH(x, y) is { } ground)
                {
                    var band = Band(WorldDump.FloorDiv(ground, WorldDump.LevelH));
                    Ground(band.Terrain, band.Outline, x, y);
                }
                foreach (var (level, index) in _world.LevelsAt(x, y))
                {
                    // A slab resting on the solid ground is ground for the cutaway: clipping it would
                    // open a hole through the excavated column.
                    var rests = level.FloorH[index] != WorldDump.NoFloor && level.FloorH[index] <= _world.SolidTopH(x, y);
                    var band = Band(level.Z);
                    Level(rests ? band.Terrain : band.Structure, band.Structure, band.Glass, x, y, level, index);
                }
            }
        }
        foreach (var o in _world.Objects)
        {
            if (o.Parent is not null || WorldDump.FloorDiv(o.X, 32) != cx || WorldDump.FloorDiv(o.Y, 32) != cy) continue;
            ObjectBox(Band(WorldDump.FloorDiv(o.H, WorldDump.LevelH)).Terrain, o);
        }
        var result = new SortedDictionary<int, BandGeometry>();
        foreach (var (z, band) in bands)
            result[z] = new BandGeometry(band.Terrain, band.Structure, band.Glass, band.Outline);
        return new ChunkGeometry(result);
    }

    public SortedDictionary<int, ChunkMeshes> ToMeshes(ChunkGeometry geometry)
    {
        var result = new SortedDictionary<int, ChunkMeshes>();
        foreach (var (z, band) in geometry.Bands)
        {
            var mesh = new ArrayMesh();
            band.Terrain.AddSurface(mesh, _terrainMaterial);
            band.Structure.AddSurface(mesh, _structureMaterial);
            band.Glass.AddSurface(mesh, _glassMaterial);
            band.Outline.AddSurface(mesh, _outlineMaterial);
            result[z] = new ChunkMeshes(mesh.GetSurfaceCount() == 0 ? null : mesh);
        }
        return result;
    }

    private void Ground(Builder b, Builder outline, int x, int y)
    {
        if (_world.GroundH(x, y) is not { } ground || _world.SolidTopH(x, y) is not { } solid) return;
        var mat = _world.SurfaceMat(x, y);
        var owner = new Vector2(x, y);
        var voidCut = solid != ground;
        if (!voidCut)
        {
            var h = M(ground);
            b.Quad(new(x, h, y), new(x + 1, h, y), new(x + 1, h, y + 1), new(x, h, y + 1), GroundNormal(x, y, ground),
                MaterialColor(mat, Top), owner, new(ground, TopLayer(mat)));
        }
        var faceTop = voidCut ? FillOnly : ground;
        var sideLayer = !voidCut ? _cliffSideLayers.GetValueOrDefault(mat, SideLayer(mat)) : SideLayer(mat);
        var south = _world.SolidTopH(x, y + 1) ?? BoundaryBottom;
        if (south < solid && !AnyWall(x, y + 1, true))
        {
            b.Quad(new(x, M(south), y + 1), new(x + 1, M(south), y + 1), new(x + 1, M(solid), y + 1),
                new(x, M(solid), y + 1), Vector3.Back, MaterialColor(mat, SouthFace), owner, new(faceTop, sideLayer));
            OutlineFace(outline, x, y, solid, south, south: true);
        }
        var east = _world.SolidTopH(x + 1, y) ?? BoundaryBottom;
        if (east < solid && !AnyWall(x + 1, y, false))
        {
            b.Quad(new(x + 1, M(east), y), new(x + 1, M(east), y + 1), new(x + 1, M(solid), y + 1),
                new(x + 1, M(solid), y), Vector3.Right, MaterialColor(mat, EastFace), owner, new(faceTop, sideLayer));
            OutlineFace(outline, x, y, solid, east, south: false);
        }
        // A drop away from the camera draws no face, so its crest stays invisible until the cliff
        // window makes the surrounding ground transparent; it keeps the cut's far (north and west)
        // boundary readable as a silhouette (Dev-033).
        var north = _world.SolidTopH(x, y - 1) ?? BoundaryBottom;
        if (north < solid && !AnyWall(x, y, true) && !RestsOnGround(x, y))
            Crest(outline, x, y, solid, north: true);
        var west = _world.SolidTopH(x - 1, y) ?? BoundaryBottom;
        if (west < solid && !AnyWall(x, y, false) && !RestsOnGround(x, y))
            Crest(outline, x, y, solid, north: false);
    }

    /// <summary>A bare drop's top edge, for the drops whose riser faces away from the camera. A
    /// wall on the shared edge or a floor resting on the ground hides the drop, so those are
    /// skipped: a strip there would poke through the wall or the floor.</summary>
    private void Crest(Builder o, int x, int y, int top, bool north)
    {
        float h = M(top);
        if (north)
        {
            float z = y - 0.01f;
            o.Quad(new(x, h - OutlineW, z), new(x + 1, h - OutlineW, z), new(x + 1, h, z), new(x, h, z),
                Vector3.Forward, OutlineColor, default, default);
        }
        else
        {
            float wx = x - 0.01f;
            o.Quad(new(wx, h - OutlineW, y), new(wx, h - OutlineW, y + 1), new(wx, h, y + 1), new(wx, h, y),
                Vector3.Left, OutlineColor, default, default);
        }
    }

    private bool RestsOnGround(int x, int y)
    {
        if (_world.SolidTopH(x, y) is not { } solid) return false;
        foreach (var (level, i) in _world.LevelsAt(x, y))
            if (level.FloorH[i] != WorldDump.NoFloor && level.FloorH[i] <= solid) return true;
        return false;
    }

    /// <summary>The outer edges of one ground face: its top crest and the ends it does not share
    /// with the next tile. The outline pass draws them only where the mass is made transparent.</summary>
    private void OutlineFace(Builder o, int x, int y, int top, int bottom, bool south)
    {
        float h = M(top), lo = M(bottom);
        if (south)
        {
            float z = y + 1f + 0.01f;
            o.Quad(new(x, h - OutlineW, z), new(x + 1, h - OutlineW, z), new(x + 1, h, z), new(x, h, z), Vector3.Back,
                OutlineColor, default, default);
            if (!SouthContinues(x - 1, y, top))
                o.Quad(new(x, lo, z), new(x + OutlineW, lo, z), new(x + OutlineW, h, z), new(x, h, z), Vector3.Back,
                    OutlineColor, default, default);
            if (!SouthContinues(x + 1, y, top))
                o.Quad(new(x + 1 - OutlineW, lo, z), new(x + 1, lo, z), new(x + 1, h, z), new(x + 1 - OutlineW, h, z),
                    Vector3.Back, OutlineColor, default, default);
        }
        else
        {
            float wx = x + 1f + 0.01f;
            o.Quad(new(wx, h - OutlineW, y), new(wx, h - OutlineW, y + 1), new(wx, h, y + 1), new(wx, h, y), Vector3.Right,
                OutlineColor, default, default);
            if (!EastContinues(x, y - 1, top))
                o.Quad(new(wx, lo, y), new(wx, lo, y + OutlineW), new(wx, h, y + OutlineW), new(wx, h, y), Vector3.Right,
                    OutlineColor, default, default);
            if (!EastContinues(x, y + 1, top))
                o.Quad(new(wx, lo, y + 1 - OutlineW), new(wx, lo, y + 1), new(wx, h, y + 1), new(wx, h, y + 1 - OutlineW),
                    Vector3.Right, OutlineColor, default, default);
        }
    }

    private static readonly Color OutlineColor = new(0.93f, 0.90f, 0.82f, 1f);

    private bool SouthContinues(int x, int y, int top) =>
        _world.SolidTopH(x, y) == top && (_world.SolidTopH(x, y + 1) ?? BoundaryBottom) < top
        && !AnyWall(x, y + 1, true);

    private bool EastContinues(int x, int y, int top) =>
        _world.SolidTopH(x, y) == top && (_world.SolidTopH(x + 1, y) ?? BoundaryBottom) < top
        && !AnyWall(x + 1, y, false);

    private Vector3 GroundNormal(int x, int y, int height)
    {
        var west = _world.SolidTopH(x - 1, y) ?? height;
        var east = _world.SolidTopH(x + 1, y) ?? height;
        var north = _world.SolidTopH(x, y - 1) ?? height;
        var south = _world.SolidTopH(x, y + 1) ?? height;
        var slope = new Vector2((east - west) * 0.25f, (south - north) * 0.25f);

        // Cliffs already have vertical faces; their top should not shade like a ramp.
        if (slope.Length() > 0.5f) slope = slope.Normalized() * 0.5f;
        return new Vector3(-slope.X, 1f, -slope.Y).Normalized();
    }

    private int NeighbourFloor(int x, int y, int z) =>
        _world.LevelCell(x, y, z) is { } c ? c.Level.FloorH[c.Index] : WorldDump.NoFloor;

    private void Level(Builder slab, Builder b, Builder glass, int x, int y, WorldDump.Level level, int i)
    {
        var owner = new Vector2(x, y);
        var floor = level.FloorH[i];
        if (floor != WorldDump.NoFloor)
        {
            var mat = level.FloorMat[i];
            var h = M(floor);
            slab.Quad(new(x, h, y), new(x + 1, h, y), new(x + 1, h, y + 1), new(x, h, y + 1), Vector3.Up,
                MaterialColor(mat, SlabTop), owner, new(floor, TopLayer(mat)));
            var lo = M(floor - 1);
            var south = NeighbourFloor(x, y + 1, level.Z);
            // A wall on the shared edge covers the slab's edge face; drawing both z-fights.
            if ((south == WorldDump.NoFloor || south < floor) && !HasWall(x, y + 1, level.Z, true))
            {
                slab.Quad(new(x, lo, y + 1), new(x + 1, lo, y + 1), new(x + 1, h, y + 1), new(x, h, y + 1),
                    Vector3.Back, MaterialColor(mat, SlabSide), owner, new(floor, SideLayer(mat)));
            }
            var east = NeighbourFloor(x + 1, y, level.Z);
            if ((east == WorldDump.NoFloor || east < floor) && !HasWall(x + 1, y, level.Z, false))
            {
                slab.Quad(new(x + 1, lo, y), new(x + 1, lo, y + 1), new(x + 1, h, y + 1), new(x + 1, h, y),
                    Vector3.Right, MaterialColor(mat, SlabSide), owner, new(floor, SideLayer(mat)));
            }
        }
        Wall(b, glass, x, y, level, i, north: true);
        Wall(b, glass, x, y, level, i, north: false);
        InteriorWall(b, x, y, level, i, horizontal: true);
        InteriorWall(b, x, y, level, i, horizontal: false);
    }

    private bool IsRoofLevel(WorldDump.Level level) => _roofBands.Contains(level.Z);

    /// <summary>
    /// A band is a roof band if any chunk flags a roof in it; perimeter-only chunks carry no flag.
    /// It spans every loaded chunk, so a change here can reshape walls far from the chunk that caused it.
    /// </summary>
    public static HashSet<int> RoofBands(WorldDump world)
    {
        var bands = new HashSet<int>();
        foreach (var list in world.Levels.Values)
        foreach (var l in list)
            if (Array.Exists(l.Flags, f => (f & WorldDump.LevelRoof) != 0)) bands.Add(l.Z);
        return bands;
    }

    // The lowest wall on an edge stands on the ground, so it covers the terrain face in its plane.
    private bool AnyWall(int x, int y, bool north)
    {
        foreach (var (level, i) in _world.LevelsAt(x, y))
            if ((north ? level.WallN[i] : level.WallW[i]) != 0) return true;
        return false;
    }

    private bool HasWall(int x, int y, int z, bool north) =>
        _world.LevelCell(x, y, z) is { } c && (north ? c.Level.WallN[c.Index] : c.Level.WallW[c.Index]) != 0;

    private void Wall(Builder b, Builder glass, int x, int y, WorldDump.Level level, int i, bool north)
    {
        var mat = north ? level.WallN[i] : level.WallW[i];
        if (mat == 0) return;
        var flags = level.EdgeFlags[i];
        if ((flags & (north ? WorldDump.EdgeNDoorway : WorldDump.EdgeWDoorway)) != 0) return;
        var window = (flags & (north ? WorldDump.EdgeNWindow : WorldDump.EdgeWWindow)) != 0;
        var z = level.Z;
        // A wall stacked on the storey below starts at its band; the lowest one reaches down to its support.
        var baseH = HasWall(x, y, z - 1, north)
            ? z * WorldDump.LevelH
            : _world.WallBaseH(x, y, z, level.FloorH[i]);
        // The roof band stores full-height walls; drawn whole they read as a fourth storey, so the
        // spike shows them as a 1 m parapet (open question in Dev-025 stage 0 notes).
        var top = IsRoofLevel(level) ? baseH + 2 : (z + 1) * WorldDump.LevelH;
        var sill = Math.Max(baseH, z * WorldDump.LevelH);
        var code = north ? WallNorth : WallWest;
        var color = MaterialColor(mat, code);
        var owner = new Vector2(x, y);
        var extra = new Vector2(top, SideLayer(mat));
        var (x0, x1, y0, y1) = north ? ((float)x, x + 1f, y - WallT, (float)y) : (x - WallT, (float)x, (float)y, y + 1f);
        // Close the corner where a west run turns into a north run that has no west neighbour.
        if (north && HasWall(x, y, z, false) && !HasWall(x - 1, y, z, true) && !HasWall(x, y - 1, z, false)) x0 -= WallT;
        if (!window)
        {
            Box(b, x0, x1, y0, y1, M(baseH), M(top), color, owner, extra, TopLayer(mat));
            return;
        }
        Box(b, x0, x1, y0, y1, M(baseH), M(sill + 2), color, owner, extra, TopLayer(mat));
        Box(b, x0, x1, y0, y1, M(sill + 4), M(top), color, owner, extra, TopLayer(mat));
        Box(glass, x0 + 0.08f * (north ? 0 : 1), x1, y0 + 0.08f * (north ? 1 : 0), y1, M(sill + 2), M(sill + 4),
            new Color(0.847f, 0.902f, 0.941f, 0.45f), owner, Vector2.Zero, -1);
    }

    private void InteriorWall(Builder b, int x, int y, WorldDump.Level level, int i, bool horizontal)
    {
        var slot = horizontal ? WorldDump.SlotHalfH : WorldDump.SlotHalfV;
        if ((level.SlotMask[i] & slot) == 0) return;
        var mat = level.SlotMat[i];
        if (mat == 0) return;
        var z = level.Z;
        var baseH = HasInteriorWall(x, y, z - 1, slot)
            ? z * WorldDump.LevelH
            : _world.WallBaseH(x, y, z, level.FloorH[i]);
        var top = IsRoofLevel(level) ? baseH + 2 : (z + 1) * WorldDump.LevelH;
        float half = WallT * 0.5f;
        var x0 = horizontal ? (float)x : x + 0.5f - half;
        var x1 = horizontal ? x + 1f : x + 0.5f + half;
        var y0 = horizontal ? y + 0.5f - half : (float)y;
        var y1 = horizontal ? y + 0.5f + half : y + 1f;
        var code = horizontal ? WallNorth : WallWest;
        Box(b, x0, x1, y0, y1, M(baseH), M(top), MaterialColor(mat, code), new Vector2(x, y),
            new Vector2(top, SideLayer(mat)), TopLayer(mat));
    }

    private bool HasInteriorWall(int x, int y, int z, byte slot) =>
        _world.LevelCell(x, y, z) is { } cell && (cell.Level.SlotMask[cell.Index] & slot) != 0;

    private void ObjectBox(Builder b, WorldDump.WorldObject o)
    {
        if (!_world.Kinds.TryGetValue(o.Kind, out var kind)) return;
        // Seed 7 buries the fixed near-spawn layout (ground 5, objects at 2); lift the whole stack.
        var lowest = int.MaxValue;
        foreach (var other in _world.Objects)
            if (other.X == o.X && other.Y == o.Y && other.Parent is null) lowest = Math.Min(lowest, other.H);
        var ground = _world.GroundH(o.X, o.Y) is { } g && !_world.IsVoid(o.X, o.Y, g) ? g : lowest;
        var h = o.H + Math.Max(0, ground - lowest);
        if (FurnitureLibrary.Find(o.Kind) is { } piece)
        {
            FurnitureMesh(b, o, piece, h);
            return;
        }
        var matId = -1;
        foreach (var m in _world.Materials.Values) if (m.Key == kind.Material) matId = m.Id;
        var size = kind.Height > 0 ? 0.8f : 0.3f;
        var height = kind.Height > 0 ? M(kind.Height) : 0.15f;
        var inset = (1 - size) / 2;
        Box(b, o.X + inset, o.X + 1 - inset, o.Y + inset, o.Y + 1 - inset, M(h), M(h) + height,
            MaterialColor(matId, Prop), new Vector2(o.X, o.Y), new Vector2(h + kind.Height, -1), -1);
    }

    // A piece's own footprint (SpanX/SpanY voxels) is centered on the object's tile; height stacks
    // on the same lifted base `h` (half-metres) the plain box would use. Faces shared by two of the
    // piece's own voxels are skipped so touching cubes of the same colour never z-fight.
    private static void FurnitureMesh(Builder b, WorldDump.WorldObject o, FurniturePiece piece, int h)
    {
        var unit = (float)piece.Unit;
        var originX = o.X + 0.5f - piece.SpanX * unit / 2f - piece.MinX * unit;
        var originY = o.Y + 0.5f - piece.SpanY * unit / 2f - piece.MinY * unit;
        var baseH = M(h);
        var owner = new Vector2(o.X, o.Y);
        foreach (var voxel in piece.Voxels)
        {
            var x0 = originX + voxel.X * unit;
            var y0 = originY + voxel.Y * unit;
            var z0 = baseH + voxel.Z * unit;
            var z1 = z0 + unit;
            var faceTop = new Vector2(z1 * 2f, -1);
            VoxelFaces(b, x0, x0 + unit, y0, y0 + unit, z0, z1, voxel.Color, owner, faceTop,
                top: !piece.Occupied.Contains((voxel.X, voxel.Y, voxel.Z + 1)),
                south: !piece.Occupied.Contains((voxel.X, voxel.Y + 1, voxel.Z)),
                east: !piece.Occupied.Contains((voxel.X + 1, voxel.Y, voxel.Z)),
                north: !piece.Occupied.Contains((voxel.X, voxel.Y - 1, voxel.Z)),
                west: !piece.Occupied.Contains((voxel.X - 1, voxel.Y, voxel.Z)));
        }
    }

    private static void VoxelFaces(Builder b, float x0, float x1, float y0, float y1, float h0, float h1, Color color,
        Vector2 owner, Vector2 extra, bool top, bool south, bool east, bool north, bool west)
    {
        if (top) b.Quad(new(x0, h1, y0), new(x1, h1, y0), new(x1, h1, y1), new(x0, h1, y1), Vector3.Up, color, owner, extra);
        if (south) b.Quad(new(x0, h0, y1), new(x1, h0, y1), new(x1, h1, y1), new(x0, h1, y1), Vector3.Back, color, owner, extra);
        if (east) b.Quad(new(x1, h0, y0), new(x1, h0, y1), new(x1, h1, y1), new(x1, h1, y0), Vector3.Right, color, owner, extra);
        if (north) b.Quad(new(x0, h0, y0), new(x1, h0, y0), new(x1, h1, y0), new(x0, h1, y0), Vector3.Forward, color, owner, extra);
        if (west) b.Quad(new(x0, h0, y0), new(x0, h0, y1), new(x0, h1, y1), new(x0, h1, y0), Vector3.Left, color, owner, extra);
    }

    /// <summary>An axis-aligned box without its bottom; the top face samples <paramref name="topLayer"/>.</summary>
    private static void Box(Builder b, float x0, float x1, float y0, float y1, float h0, float h1, Color color,
        Vector2 owner, Vector2 extra, int topLayer)
    {
        // A wall keeps its code on the top strip, so the circular window (codes 0-2 only) cannot
        // punch it; other boxes drop to the terrain top code as before.
        var code = (int)MathF.Round(color.A * 8f);
        var topCode = code is Prop or WallNorth or WallWest ? code : Top;
        var topColor = color with { A = topCode / 8f };
        b.Quad(new(x0, h1, y0), new(x1, h1, y0), new(x1, h1, y1), new(x0, h1, y1), Vector3.Up, topColor, owner,
            new(extra.X, topLayer));
        b.Quad(new(x0, h0, y1), new(x1, h0, y1), new(x1, h1, y1), new(x0, h1, y1), Vector3.Back, color, owner, extra);
        b.Quad(new(x1, h0, y0), new(x1, h0, y1), new(x1, h1, y1), new(x1, h1, y0), Vector3.Right, color, owner, extra);
        b.Quad(new(x0, h0, y0), new(x1, h0, y0), new(x1, h1, y0), new(x0, h1, y0), Vector3.Forward, color, owner, extra);
        b.Quad(new(x0, h0, y0), new(x0, h0, y1), new(x0, h1, y1), new(x0, h1, y0), Vector3.Left, color, owner, extra);
    }
}
