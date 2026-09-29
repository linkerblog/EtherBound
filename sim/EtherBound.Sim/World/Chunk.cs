using System.Text;
using System.Text.Json;

namespace EtherBound.Sim.World;

public static class ChunkConst
{
    public const int Size = 32;
    public const int CellCount = Size * Size;
    public const short NoFloor = -32768;
    public const int LevelH = 6;

    public const byte EdgeNDoorway = 1;
    public const byte EdgeNWindow = 2;
    public const byte EdgeWDoorway = 4;
    public const byte EdgeWWindow = 8;
    public const byte LevelVoid = 1;
    public const byte LevelClimbable = 2;
    public const byte LevelRoof = 4;

    // The six wall slots of a tile (Dev-036 [Sec. 2]): two on the edges the world already owns and
    // four through the interior. One byte per cell is the region graph's cache key from phase 2.
    public const byte SlotNorth = 1;
    public const byte SlotWest = 2;
    public const byte SlotHalfH = 4;
    public const byte SlotHalfV = 8;
    public const byte SlotDiag1 = 16;
    public const byte SlotDiag2 = 32;
    public const byte SlotAll = SlotNorth | SlotWest | SlotHalfH | SlotHalfV | SlotDiag1 | SlotDiag2;
}

/// <summary>Slot letters, as stored in <c>wall_slot</c> and as the <see cref="ChunkConst"/> bitmask.</summary>
public static class WallSlots
{
    public const string North = "north";
    public const string West = "west";
    public const string HalfH = "H";
    public const string HalfV = "V";
    public const string Diag1 = "D1";
    public const string Diag2 = "D2";

    public static readonly IReadOnlyList<string> All = new[] { North, West, HalfH, HalfV, Diag1, Diag2 };

    public static byte Bit(string slot) => slot switch
    {
        North => ChunkConst.SlotNorth,
        West => ChunkConst.SlotWest,
        HalfH => ChunkConst.SlotHalfH,
        HalfV => ChunkConst.SlotHalfV,
        Diag1 => ChunkConst.SlotDiag1,
        Diag2 => ChunkConst.SlotDiag2,
        _ => 0,
    };

    public static string Name(byte bit) => bit switch
    {
        ChunkConst.SlotNorth => North,
        ChunkConst.SlotWest => West,
        ChunkConst.SlotHalfH => HalfH,
        ChunkConst.SlotHalfV => HalfV,
        ChunkConst.SlotDiag1 => Diag1,
        ChunkConst.SlotDiag2 => Diag2,
        _ => "",
    };

    /// <summary>Metres of wall the slot is: edges and halves are 1 m, diagonals √2 (Dev-036 [Sec. 2]).</summary>
    public static double LengthM(string slot) => slot is Diag1 or Diag2 ? Math.Sqrt(2.0) : 1.0;
}

/// <summary>One stratum boundary: from <c>Depth</c> half-metres below the original ground down.</summary>
public readonly record struct Stratum(int Depth, string Key);

/// <summary>
/// A 32×32 surface chunk. Arrays are never mutated after construction; a change builds a new
/// chunk (the Python dataclass is frozen). Blobs are little-endian, exactly as stored.
/// </summary>
public sealed class Chunk
{
    public Chunk(int cx, int cy, short[] groundH, ushort[] surfaceMat, IReadOnlyList<Stratum>? strata = null,
        int revision = 0, int genVersion = 1, byte[]? dug = null)
    {
        if (groundH.Length != ChunkConst.CellCount) throw new ArgumentException("ground_h must contain 1024 values");
        if (surfaceMat.Length != ChunkConst.CellCount) throw new ArgumentException("surface_mat must contain 1024 values");
        if (dug is not null && dug.Length != ChunkConst.CellCount) throw new ArgumentException("dug must contain 1024 values");
        Cx = cx;
        Cy = cy;
        GroundH = groundH;
        SurfaceMat = surfaceMat;
        Strata = strata ?? Array.Empty<Stratum>();
        if (Strata.Any(s => s.Depth < 0)) throw new ArgumentException("strata depths must be non-negative");
        Revision = revision;
        GenVersion = genVersion;
        Dug = dug ?? new byte[ChunkConst.CellCount];
    }

    public int Cx { get; }
    public int Cy { get; }
    public short[] GroundH { get; }
    public ushort[] SurfaceMat { get; }
    public IReadOnlyList<Stratum> Strata { get; }
    public int Revision { get; }
    public int GenVersion { get; }
    public byte[] Dug { get; }

    public static Chunk Flat(int cx, int cy, int groundH, int materialId, IReadOnlyList<Stratum>? strata = null) =>
        new(cx, cy, Enumerable.Repeat((short)groundH, ChunkConst.CellCount).ToArray(),
            Enumerable.Repeat((ushort)materialId, ChunkConst.CellCount).ToArray(), strata);

    public static int Index(int x, int y)
    {
        if (x is < 0 or >= ChunkConst.Size || y is < 0 or >= ChunkConst.Size)
            throw new IndexOutOfRangeException("chunk coordinates must be in [0, 32)");
        return y * ChunkConst.Size + x;
    }

    public Chunk With(short[]? groundH = null, ushort[]? surfaceMat = null, byte[]? dug = null, int? revision = null) =>
        new(Cx, Cy, groundH ?? GroundH, surfaceMat ?? SurfaceMat, Strata, revision ?? Revision, GenVersion, dug ?? Dug);

    public byte[] GroundBlob => Blobs.Encode(GroundH);
    public byte[] SurfaceBlob => Blobs.Encode(SurfaceMat);
    public byte[] DugBlob => (byte[])Dug.Clone();

    /// <summary><c>json.dumps(strata)</c> as Python writes it (with spaces), for the JSON column.</summary>
    public string StrataJson() =>
        "[" + string.Join(", ", Strata.Select(s => $"[{s.Depth}, {JsonSerializer.Serialize(s.Key)}]")) + "]";

    /// <summary><c>json.dumps(strata, separators=(",", ":"))</c>, as in <c>GeneratedWorld.blob_bytes</c>.</summary>
    public byte[] StrataCompactJson() =>
        Encoding.ASCII.GetBytes("[" + string.Join(",", Strata.Select(s => $"[{s.Depth},{JsonSerializer.Serialize(s.Key)}]")) + "]");

    public static Chunk FromBlobs(int cx, int cy, byte[] groundBlob, byte[] surfaceBlob, IReadOnlyList<Stratum> strata,
        int revision, int genVersion, byte[]? dugBlob) =>
        new(cx, cy, Blobs.DecodeInt16(groundBlob), Blobs.DecodeUInt16(surfaceBlob), strata, revision, genVersion,
            dugBlob is null ? null : Blobs.DecodeUInt8(dugBlob));
}

/// <summary>One <c>z</c> band of a chunk: floors, north/west walls, edge and level flags.</summary>
public sealed class ChunkLevel
{
    public ChunkLevel(int cx, int cy, int z, short[] floorH, ushort[] floorMat, ushort[] wallN, ushort[] wallW,
        byte[] edgeFlags, byte[] flags, byte[]? slotMask = null, ushort[]? slotMat = null)
    {
        foreach (var length in new[] { floorH.Length, floorMat.Length, wallN.Length, wallW.Length, edgeFlags.Length, flags.Length })
            if (length != ChunkConst.CellCount) throw new ArgumentException("level arrays must contain 1024 values");
        if (slotMask is not null && slotMask.Length != ChunkConst.CellCount) throw new ArgumentException("slot_mask must contain 1024 values");
        if (slotMat is not null && slotMat.Length != ChunkConst.CellCount) throw new ArgumentException("slot_mat must contain 1024 values");
        Cx = cx;
        Cy = cy;
        Z = z;
        FloorH = floorH;
        FloorMat = floorMat;
        WallN = wallN;
        WallW = wallW;
        EdgeFlags = edgeFlags;
        Flags = flags;
        SlotMask = slotMask ?? new byte[ChunkConst.CellCount];
        SlotMat = slotMat ?? new ushort[ChunkConst.CellCount];
    }

    public int Cx { get; }
    public int Cy { get; }
    public int Z { get; }
    public short[] FloorH { get; }
    public ushort[] FloorMat { get; }
    public ushort[] WallN { get; }
    public ushort[] WallW { get; }
    public byte[] EdgeFlags { get; }
    public byte[] Flags { get; }

    /// <summary>Which of the six slots carry a wall; phases 2 and 3 fill the interior bits.</summary>
    public byte[] SlotMask { get; }

    /// <summary>Material of each interior slot, parallel to <see cref="SlotMask"/>.</summary>
    public ushort[] SlotMat { get; }

    public static ChunkLevel Empty(int cx, int cy, int z) => new(cx, cy, z,
        Enumerable.Repeat(ChunkConst.NoFloor, ChunkConst.CellCount).ToArray(), new ushort[ChunkConst.CellCount],
        new ushort[ChunkConst.CellCount], new ushort[ChunkConst.CellCount], new byte[ChunkConst.CellCount],
        new byte[ChunkConst.CellCount]);

    public ChunkLevel With(short[]? floorH = null, ushort[]? floorMat = null, ushort[]? wallN = null,
        ushort[]? wallW = null, byte[]? edgeFlags = null, byte[]? flags = null, byte[]? slotMask = null,
        ushort[]? slotMat = null) =>
        new(Cx, Cy, Z, floorH ?? FloorH, floorMat ?? FloorMat, wallN ?? WallN, wallW ?? WallW,
            edgeFlags ?? EdgeFlags, flags ?? Flags, slotMask ?? SlotMask, slotMat ?? SlotMat);

    public byte[] FloorBlob => Blobs.Encode(FloorH);
    public byte[] FloorMatBlob => Blobs.Encode(FloorMat);
    public byte[] WallNBlob => Blobs.Encode(WallN);
    public byte[] WallWBlob => Blobs.Encode(WallW);
    public byte[] EdgeFlagsBlob => (byte[])EdgeFlags.Clone();
    public byte[] FlagsBlob => (byte[])Flags.Clone();
    public byte[] SlotMaskBlob => (byte[])SlotMask.Clone();
    public byte[] SlotMatBlob => Blobs.Encode(SlotMat);

    public static ChunkLevel FromBlobs(int cx, int cy, int z, byte[] floor, byte[] floorMat, byte[] wallN, byte[] wallW,
        byte[] edgeFlags, byte[] flags, byte[]? slotMask = null, byte[]? slotMat = null) =>
        new(cx, cy, z, Blobs.DecodeInt16(floor), Blobs.DecodeUInt16(floorMat), Blobs.DecodeUInt16(wallN),
            Blobs.DecodeUInt16(wallW), Blobs.DecodeUInt8(edgeFlags), Blobs.DecodeUInt8(flags),
            slotMask is null ? null : Blobs.DecodeUInt8(slotMask), slotMat is null ? null : Blobs.DecodeUInt16(slotMat));
}

/// <summary>Little-endian blob codecs (F5): the same bytes the Python server stores.</summary>
public static class Blobs
{
    public static byte[] Encode(short[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), values[i]);
        return bytes;
    }

    public static byte[] Encode(ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), values[i]);
        return bytes;
    }

    public static short[] DecodeInt16(byte[] blob)
    {
        if (blob.Length != ChunkConst.CellCount * 2) throw new ArgumentException($"expected {ChunkConst.CellCount} values");
        var values = new short[ChunkConst.CellCount];
        for (var i = 0; i < values.Length; i++) values[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(blob.AsSpan(i * 2));
        return values;
    }

    public static ushort[] DecodeUInt16(byte[] blob)
    {
        if (blob.Length != ChunkConst.CellCount * 2) throw new ArgumentException($"expected {ChunkConst.CellCount} values");
        var values = new ushort[ChunkConst.CellCount];
        for (var i = 0; i < values.Length; i++) values[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(i * 2));
        return values;
    }

    public static byte[] DecodeUInt8(byte[] blob)
    {
        if (blob.Length != ChunkConst.CellCount) throw new ArgumentException($"expected {ChunkConst.CellCount} values");
        return (byte[])blob.Clone();
    }
}
