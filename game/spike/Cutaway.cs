using System;
using Godot;

namespace EtherBound.Game.Spike;

/// <summary>
/// Stage-0 cutaway over the dump grid (VISION [Sec. 5]): roofed or hidden behind a structure means
/// everything built more than 2 m above Niko's feet is clipped. In render space the ray toward the
/// camera advances 1 m in x, 1 m in y and 1 m in height per step, so it walks the grid directly.
/// </summary>
public static class Cutaway
{
    private const int HeadroomH = 4;

    public static float LevelClipH(int actorH) => (WorldDump.FloorDiv(actorH, WorldDump.LevelH) + 1) * WorldDump.LevelH;

    public static void UpdateView(ShaderMaterial terrain, ShaderMaterial structure, ShaderMaterial glass,
        WorldDump world, Vector3 feet, Vector3 actorPosition, int actorH, Node3D root, Camera3D camera,
        bool enabled, bool levelOnly)
    {
        var aroundPlayer = enabled && !levelOnly && Hidden(world, feet.X, feet.Z, actorH);
        var clip = !enabled ? float.PositiveInfinity : levelOnly
            ? Math.Min(LevelClipH(actorH), ClipH(world, feet.X, feet.Z, actorH)) : float.PositiveInfinity;
        var clipH = float.IsInfinity(clip) ? 100000f : clip;
        structure.SetShaderParameter("clip_h", clipH);
        glass.SetShaderParameter("clip_h", clipH);
        terrain.SetShaderParameter("clip_enabled", enabled && levelOnly);
        terrain.SetShaderParameter("clip_h", clipH);
        UpdateCliffShader(terrain, root, camera, actorPosition, aroundPlayer);
        UpdateCliffShader(structure, root, camera, actorPosition, aroundPlayer);
    }

    public static void UpdateCliffShader(ShaderMaterial material, Node3D root, Camera3D camera,
        Vector3 actorPosition, bool enabled)
    {
        // Project local mesh coordinates through the root's vertical scale onto the camera axes.
        var projection = root.GlobalBasis.Transposed();
        material.SetShaderParameter("cliff_cut_center", actorPosition);
        material.SetShaderParameter("cliff_cut_right", projection * camera.GlobalBasis.X);
        material.SetShaderParameter("cliff_cut_up", projection * camera.GlobalBasis.Y);
        material.SetShaderParameter("cliff_cutaway_enabled", enabled);
    }

    public static float ClipH(WorldDump world, float x, float y, int viewerH)
    {
        int nx = (int)MathF.Floor(x), ny = (int)MathF.Floor(y);
        if (Roofed(world, nx, ny, viewerH) || Occluded(world, x, y, viewerH)) return viewerH + HeadroomH;
        return float.PositiveInfinity;
    }

    private static bool Roofed(WorldDump world, int nx, int ny, int viewerH)
    {
        if (world.GroundH(nx, ny) is { } ground && ground > viewerH) return true;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx != 0 && dy != 0) continue;
            int tx = nx + dx, ty = ny + dy;
            var covered = false;
            foreach (var (level, i) in world.LevelsAt(tx, ty))
                covered |= level.FloorH[i] != WorldDump.NoFloor && level.FloorH[i] > viewerH;
            if (covered && (dx == 0 && dy == 0 || !WallBetween(world, nx, ny, tx, ty, viewerH))) return true;
        }
        return false;
    }

    private static bool WallBetween(WorldDump world, int x1, int y1, int x2, int y2, int h)
    {
        var (x, y, north) = y2 < y1 ? (x1, y1, true) : y2 > y1 ? (x2, y2, true) : x2 < x1 ? (x1, y1, false) : (x2, y2, false);
        foreach (var (level, i) in world.LevelsAt(x, y))
        {
            var wall = north ? level.WallN[i] : level.WallW[i];
            var doorway = north ? WorldDump.EdgeNDoorway : WorldDump.EdgeWDoorway;
            if (wall != 0 && (level.EdgeFlags[i] & doorway) == 0 && h >= level.Z * WorldDump.LevelH - 6
                && h < (level.Z + 1) * WorldDump.LevelH) return true;
        }
        return false;
    }

    private static bool Occluded(WorldDump world, float x, float y, int viewerH)
    {
        var feetM = viewerH * 0.5f;
        for (var s = 0.3f; s < 40f; s += 0.05f)
        {
            float px = x + s, py = y + s, hm = feetM + 0.9f + s;
            var hh = hm * 2;
            if (hh <= viewerH + HeadroomH) continue;
            if (Structure(world, px, py, hh)) return true;
        }
        return false;
    }

    // A body-height ray from Niko toward the camera: anything solid above it hides him. The three
    // heights cover legs, torso and head, so the circle only lights up when he is really covered.
    private static readonly float[] BodyHeights = { 0.5f, 1.0f, 1.5f };

    /// <summary>True when terrain or a structure stands between the camera and Niko's body.</summary>
    public static bool Hidden(WorldDump world, float x, float y, int viewerH)
    {
        var feetM = viewerH * 0.5f;
        foreach (var body in BodyHeights)
        for (var s = 0.25f; s < 40f; s += 0.25f)
        {
            float px = x + s, py = y + s;
            if (Blocked(world, px, py, feetM + body + s)) return true;
        }
        return false;
    }

    private static bool Blocked(WorldDump world, float px, float py, float hm)
    {
        int tx = (int)MathF.Floor(px), ty = (int)MathF.Floor(py);
        if (world.SolidTopH(tx, ty) is { } top && top * 0.5f >= hm) return true;
        return Structure(world, px, py, hm * 2f);
    }

    private static bool Structure(WorldDump world, float px, float py, float hh)
    {
        int tx = (int)MathF.Floor(px), ty = (int)MathF.Floor(py);
        float fx = px - tx, fy = py - ty;
        foreach (var (level, i) in world.LevelsAt(tx, ty))
        {
            var f = level.FloorH[i];
            if (f != WorldDump.NoFloor && hh >= f - 1 && hh <= f) return true;
        }
        var band = (int)MathF.Floor(hh / WorldDump.LevelH);
        if (fy > 0.75f && world.LevelCell(tx, ty + 1, band) is { } n && n.Level.WallN[n.Index] != 0) return true;
        if (fx > 0.75f && world.LevelCell(tx + 1, ty, band) is { } w && w.Level.WallW[w.Index] != 0) return true;
        return false;
    }

    /// <summary>The highest standing surface in a tile within a 0.5 m step of <paramref name="fromH"/>.</summary>
    public static int? StandingH(WorldDump world, int x, int y, int fromH)
    {
        int? best = null;
        void Consider(int h)
        {
            if (h <= fromH + 1 && h >= fromH - 8 && (best is null || h > best)) best = h;
        }
        if (world.GroundH(x, y) is { } g && !world.IsVoid(x, y, g)) Consider(g);
        foreach (var (level, i) in world.LevelsAt(x, y))
            if (level.FloorH[i] != WorldDump.NoFloor) Consider(level.FloorH[i]);
        return best;
    }
}
