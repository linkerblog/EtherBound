using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EtherBound.Host;

namespace EtherBound.Game.Spike;

/// <summary>
/// A render-only projection. JSON loading remains for the Stage-0 spike; the app builds its view from
/// detached simulation frames and never treats this data as world state.
/// </summary>
public sealed class WorldDump
{
    public const int ChunkSize = 32;
    public const int CellCount = ChunkSize * ChunkSize;
    public const int LevelH = 6;
    public const short NoFloor = -32768;
    public const byte EdgeNDoorway = 1, EdgeNWindow = 2, EdgeWDoorway = 4, EdgeWWindow = 8;
    public const byte SlotHalfH = 4, SlotHalfV = 8;
    public const byte LevelVoid = 1, LevelRoof = 4;

    public sealed record Material(int Id, string Key, string Color, bool Liquid);
    public sealed record Kind(string Material, int Height, bool Solid, bool Surface);
    public sealed record WorldObject(string Kind, int X, int Y, int H, int? Parent);

    public sealed class Chunk
    {
        public required short[] GroundH;
        public required ushort[] SurfaceMat;
        public required byte[] Dug;
    }

    public sealed class Level
    {
        public required int Z;
        public required short[] FloorH;
        public required ushort[] FloorMat, WallN, WallW;
        public required byte[] EdgeFlags, Flags;
        public required byte[] SlotMask;
        public required ushort[] SlotMat;
    }

    public string Generator = "";
    public (float X, float Y, int H) Spawn;
    public readonly Dictionary<int, Material> Materials = new();
    public readonly Dictionary<string, Kind> Kinds = new();
    public readonly Dictionary<(int, int), Chunk> Chunks = new();
    public readonly Dictionary<(int, int), List<Level>> Levels = new();
    public readonly List<WorldObject> Objects = new();
    public int MinCx = int.MaxValue, MinCy = int.MaxValue, MaxCx = int.MinValue, MaxCy = int.MinValue;

    public static WorldDump Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var dump = new WorldDump { Generator = root.GetProperty("generator").GetString()! };
        var spawn = root.GetProperty("spawn");
        dump.Spawn = ((float)spawn.GetProperty("x").GetDouble(), (float)spawn.GetProperty("y").GetDouble(),
            spawn.GetProperty("h").GetInt32());
        foreach (var m in root.GetProperty("materials").EnumerateArray())
        {
            var id = m.GetProperty("id").GetInt32();
            dump.Materials[id] = new Material(id, m.GetProperty("key").GetString()!,
                m.GetProperty("color").GetString()!, m.GetProperty("liquid").GetBoolean());
        }
        foreach (var k in root.GetProperty("kinds").EnumerateObject())
        {
            dump.Kinds[k.Name] = new Kind(k.Value.GetProperty("material").GetString()!,
                k.Value.GetProperty("height").GetInt32(), k.Value.GetProperty("solid").GetBoolean(),
                k.Value.GetProperty("surface").GetBoolean());
        }
        foreach (var c in root.GetProperty("chunks").EnumerateArray())
        {
            int cx = c.GetProperty("cx").GetInt32(), cy = c.GetProperty("cy").GetInt32();
            dump.Chunks[(cx, cy)] = new Chunk
            {
                GroundH = Int16s(c, "ground_h"),
                SurfaceMat = UInt16s(c, "surface_mat"),
                Dug = Convert.FromBase64String(c.GetProperty("dug").GetString()!),
            };
            dump.MinCx = Math.Min(dump.MinCx, cx);
            dump.MinCy = Math.Min(dump.MinCy, cy);
            dump.MaxCx = Math.Max(dump.MaxCx, cx);
            dump.MaxCy = Math.Max(dump.MaxCy, cy);
        }
        foreach (var l in root.GetProperty("levels").EnumerateArray())
        {
            int cx = l.GetProperty("cx").GetInt32(), cy = l.GetProperty("cy").GetInt32();
            if (!dump.Levels.TryGetValue((cx, cy), out var list)) dump.Levels[(cx, cy)] = list = new();
            list.Add(new Level
            {
                Z = l.GetProperty("z").GetInt32(),
                FloorH = Int16s(l, "floor_h"),
                FloorMat = UInt16s(l, "floor_mat"),
                WallN = UInt16s(l, "wall_n"),
                WallW = UInt16s(l, "wall_w"),
                EdgeFlags = Convert.FromBase64String(l.GetProperty("edge_flags").GetString()!),
                Flags = Convert.FromBase64String(l.GetProperty("flags").GetString()!),
                SlotMask = l.TryGetProperty("slot_mask", out var slotMask)
                    ? Convert.FromBase64String(slotMask.GetString()!) : new byte[CellCount],
                SlotMat = l.TryGetProperty("slot_mat", out var slotMat) ? UInt16s(l, "slot_mat") : new ushort[CellCount],
            });
        }
        foreach (var o in root.GetProperty("objects").EnumerateArray())
        {
            var parent = o.GetProperty("parent");
            dump.Objects.Add(new WorldObject(o.GetProperty("kind").GetString()!, o.GetProperty("x").GetInt32(),
                o.GetProperty("y").GetInt32(), o.GetProperty("h").GetInt32(),
                parent.ValueKind == JsonValueKind.Null ? null : parent.GetInt32()));
        }
        return dump;
    }

    /// <summary>Copies immutable host data into the renderer's query-friendly view model.</summary>
    public static WorldDump FromFrame(WorldFrame frame)
    {
        var player = frame.Actors.First(actor => actor.Id == EtherBound.Sim.Core.Ids.Player);
        var dump = new WorldDump
        {
            Generator = frame.Generator,
            Spawn = ((float)player.X, (float)player.Y, player.H),
        };
        foreach (var material in frame.Materials)
            dump.Materials[material.Id] = new Material(material.Id, material.Key, material.Color, material.Liquid);
        foreach (var kind in frame.ObjectKinds)
            dump.Kinds[kind.Key] = new Kind(kind.Material, kind.Height, kind.Solid, kind.Surface);
        foreach (var chunk in frame.Chunks)
        {
            dump.Chunks[(chunk.Cx, chunk.Cy)] = new Chunk
            {
                GroundH = chunk.GroundH.ToArray(),
                SurfaceMat = chunk.SurfaceMat.ToArray(),
                Dug = chunk.Dug.ToArray(),
            };
            dump.MinCx = Math.Min(dump.MinCx, chunk.Cx);
            dump.MinCy = Math.Min(dump.MinCy, chunk.Cy);
            dump.MaxCx = Math.Max(dump.MaxCx, chunk.Cx);
            dump.MaxCy = Math.Max(dump.MaxCy, chunk.Cy);
            foreach (var level in chunk.Levels)
            {
                if (!dump.Levels.TryGetValue((chunk.Cx, chunk.Cy), out var levels))
                    dump.Levels[(chunk.Cx, chunk.Cy)] = levels = new List<Level>();
                levels.Add(new Level
                {
                    Z = level.Z,
                    FloorH = level.FloorH.ToArray(),
                    FloorMat = level.FloorMat.ToArray(),
                    WallN = level.WallN.ToArray(),
                    WallW = level.WallW.ToArray(),
                    EdgeFlags = level.EdgeFlags.ToArray(),
                    Flags = level.Flags.ToArray(),
                    SlotMask = level.SlotMask.ToArray(),
                    SlotMat = level.SlotMat.ToArray(),
                });
            }
            dump.Objects.AddRange(chunk.Objects.Select(o => new WorldObject(o.Kind, o.X, o.Y, o.H, null)));
        }
        return dump;
    }

    // Blobs are little-endian on disk; BitConverter matches on every platform Godot ships for Windows.
    private static short[] Int16s(JsonElement e, string name)
    {
        var bytes = Convert.FromBase64String(e.GetProperty(name).GetString()!);
        var values = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private static ushort[] UInt16s(JsonElement e, string name)
    {
        var bytes = Convert.FromBase64String(e.GetProperty(name).GetString()!);
        var values = new ushort[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private static (int C, int L) Split(int v)
    {
        var c = FloorDiv(v, ChunkSize);
        return (c, v - c * ChunkSize);
    }

    public bool TryCell(int x, int y, out Chunk chunk, out int index)
    {
        var (cx, lx) = Split(x);
        var (cy, ly) = Split(y);
        index = ly * ChunkSize + lx;
        return Chunks.TryGetValue((cx, cy), out chunk!);
    }

    public int? GroundH(int x, int y) => TryCell(x, y, out var c, out var i) ? c.GroundH[i] : null;

    public int SurfaceMat(int x, int y) => TryCell(x, y, out var c, out var i) ? c.SurfaceMat[i] : 0;

    public IEnumerable<(Level Level, int Index)> LevelsAt(int x, int y)
    {
        var (cx, lx) = Split(x);
        var (cy, ly) = Split(y);
        if (!Levels.TryGetValue((cx, cy), out var list)) yield break;
        foreach (var level in list) yield return (level, ly * ChunkSize + lx);
    }

    public (Level Level, int Index)? LevelCell(int x, int y, int z)
    {
        foreach (var cell in LevelsAt(x, y)) if (cell.Level.Z == z) return cell;
        return null;
    }

    public bool IsVoid(int x, int y, int h)
    {
        var cell = LevelCell(x, y, FloorDiv(h, LevelH));
        return cell is { } c && (c.Level.Flags[c.Index] & LevelVoid) != 0;
    }

    /// <summary>The top of the solid column: the ground, or the bottom of the VOID bands under it.</summary>
    public int? SolidTopH(int x, int y)
    {
        if (GroundH(x, y) is not { } ground) return null;
        var z = FloorDiv(ground, LevelH);
        if (!IsVoid(x, y, z * LevelH)) return ground;
        while (IsVoid(x, y, (z - 1) * LevelH)) z -= 1;
        return z * LevelH;
    }

    /// <summary>Same rule as the web client: a floorless wall stands on the highest support below its band.</summary>
    public int WallBaseH(int x, int y, int z, int floorH)
    {
        if (floorH != NoFloor) return floorH;
        var limit = (z + 1) * LevelH;
        var best = int.MinValue;
        if (GroundH(x, y) is { } g && g < limit) best = g;
        foreach (var (level, index) in LevelsAt(x, y))
        {
            var f = level.FloorH[index];
            if (level.Z < z && f != NoFloor && f < limit) best = Math.Max(best, f);
        }
        return best == int.MinValue ? z * LevelH : best;
    }

    public static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}
