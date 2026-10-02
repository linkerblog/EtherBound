using System;
using EtherBound.Host;
using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The arithmetic of the minimap panel, apart from any node so a test needs no scene. The map is
/// axis-aligned with north (world -y) up and east (+x) right, which is also what W and D do; the sim
/// already coloured the cells, so nothing here samples terrain.
/// </summary>
public static class MinimapModel
{
    /// <summary>Cells across the square the panel shows; smaller than the window so Niko can sit anywhere in his chunk.</summary>
    public const int ViewCells = 112;

    /// <summary>Niko's position in the map's cell space (a cell is <see cref="HostMinimap.TilesPerCell"/> tiles).</summary>
    public static Vector2 CellOf(HostMinimap map, double x, double y)
    {
        var chunkTiles = map.CellsPerChunk * map.TilesPerCell;
        return new Vector2((float)((x - map.OriginCx * chunkTiles) / map.TilesPerCell),
            (float)((y - map.OriginCy * chunkTiles) / map.TilesPerCell));
    }

    /// <summary>The cells of the map to draw, centred on Niko and clamped to the image so it never reads past its edge.</summary>
    public static Rect2 SourceRegion(HostMinimap map, double x, double y)
    {
        var cell = CellOf(map, x, y);
        var view = Math.Min(ViewCells, map.Cells);
        var left = Math.Clamp(cell.X - view * 0.5f, 0f, map.Cells - view);
        var top = Math.Clamp(cell.Y - view * 0.5f, 0f, map.Cells - view);
        return new Rect2(left, top, view, view);
    }

    /// <summary>Where Niko's marker sits inside the drawn square, as a fraction of its side (0.5, 0.5 is the centre).</summary>
    public static Vector2 MarkerFraction(HostMinimap map, double x, double y)
    {
        var region = SourceRegion(map, x, y);
        var cell = CellOf(map, x, y);
        return new Vector2((cell.X - region.Position.X) / region.Size.X, (cell.Y - region.Position.Y) / region.Size.Y);
    }

    /// <summary>The image's RGB bytes in the layout Godot's <c>Image.Format.Rgb8</c> expects.</summary>
    public static byte[] Rgb(HostMinimap map) => map.Rgb.AsSpan().ToArray();
}
