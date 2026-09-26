using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace EtherBound.Game.Spike;

public readonly record struct FurnitureVoxel(int X, int Y, int Z, Color Color);

/// <summary>A BitCanvas voxel piece (Dev-025 [Sec. 3.6]): its own grid, in <see cref="Unit"/> metres.</summary>
public sealed class FurniturePiece
{
    public required double Unit { get; init; }
    public required IReadOnlyList<FurnitureVoxel> Voxels { get; init; }
    public required HashSet<(int X, int Y, int Z)> Occupied { get; init; }
    public required int MinX { get; init; }
    public required int MinY { get; init; }
    public required int SpanX { get; init; }
    public required int SpanY { get; init; }
}

/// <summary>Loads `game/assets/furniture/*.json`, written by `scripts/export-furniture.mjs` from
/// `BitCanvas/furnitureData.js`. One piece per object kind key; kinds with none keep the plain box.</summary>
public static class FurnitureLibrary
{
    private static Dictionary<string, FurniturePiece>? _pieces;

    public static FurniturePiece? Find(string kind)
    {
        _pieces ??= Load();
        return _pieces.GetValueOrDefault(kind);
    }

    private static Dictionary<string, FurniturePiece> Load()
    {
        var pieces = new Dictionary<string, FurniturePiece>(System.StringComparer.Ordinal);
        var directory = Path.Combine(ProjectSettings.GlobalizePath("res://"), "assets", "furniture");
        if (!Directory.Exists(directory)) return pieces;
        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var key = root.GetProperty("key").GetString()!;
            var unit = root.GetProperty("unit").GetDouble();
            var voxels = new List<FurnitureVoxel>();
            var occupied = new HashSet<(int, int, int)>();
            foreach (var row in root.GetProperty("voxels").EnumerateArray())
            {
                var v = row.EnumerateArray().Select(item => item.GetInt32()).ToArray();
                voxels.Add(new FurnitureVoxel(v[0], v[1], v[2], Color.Color8((byte)v[3], (byte)v[4], (byte)v[5])));
                occupied.Add((v[0], v[1], v[2]));
            }
            var minX = voxels.Min(v => v.X);
            var minY = voxels.Min(v => v.Y);
            pieces[key] = new FurniturePiece
            {
                Unit = unit,
                Voxels = voxels,
                Occupied = occupied,
                MinX = minX,
                MinY = minY,
                SpanX = voxels.Max(v => v.X) - minX + 1,
                SpanY = voxels.Max(v => v.Y) - minY + 1,
            };
        }
        return pieces;
    }
}
