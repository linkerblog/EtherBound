using System;
using Godot;

namespace EtherBound.Game.Spike;

/// <summary>
/// Stage-0 cutaway over the dump grid (VISION [Sec. 5]): roofed, or hidden behind a structure, means
/// every built storey one 3 m band above Niko is hidden, while a wall of his own band standing in
/// front of him is cut to a stub. In render space the ray toward the camera advances 1 m in x, 1 m in
/// y and 1 m in height per step, so it walks the grid directly.
/// </summary>
public static class Cutaway
{
    private const int HeadroomH = 4;
    // Radius of the round transparent cliff hole around Niko, in screen metres, shared with the
    // silhouette outline: the size the cliff shot reads well with. It can also reach walls and
    // stairs that do not cover him, so the window only opens when natural terrain itself hides his
    // body (TerrainHidden); a building that hides him opens its storey through the band cut.
    private const float CliffCutRadius = 6f;
    // Niko's capsule, shared with the mesh WorldClient builds for him.
    public const float BodyRadius = 0.22f;
    public const float BodyHeight = 1.7f;

    public static float LevelClipH(int actorH) => (WorldDump.FloorDiv(actorH, WorldDump.LevelH) + 1) * WorldDump.LevelH;

    public static void UpdateView(ShaderMaterial terrain, ShaderMaterial structure, ShaderMaterial glass,
        ShaderMaterial outline, WorldDump world, Vector3 feet, Vector3 actorPosition, Vector3? cursorTerrain,
        int actorH, Node3D root, Camera3D camera, bool enabled, bool levelOnly)
    {
        // A roof over Niko, or a building between him and the camera, opens his storey: every built
        // floor or wall one band above him is hidden (Dev-033). ClipH only reads floors and walls,
        // never natural terrain, so a cliff is left to the view cone and does not open a building. A
        // flat plane would hide the next floor's slab only from its bottom, so the cut follows the
        // band, not a height. The probe follows the true camera ray (in sim axes: x, y, height).
        var meshBack = RayToCamera(root, camera);
        var probe = new Vector3(meshBack.X, meshBack.Z, meshBack.Y);
        var covered = ClipH(world, feet.X, feet.Z, actorH, probe);
        var clip = !enabled ? float.PositiveInfinity
            : levelOnly ? Math.Min(LevelClipH(actorH), covered) : float.PositiveInfinity;
        var clipH = float.IsInfinity(clip) ? 100000f : clip;
        var bandCut = enabled && (levelOnly || !float.IsInfinity(covered));
        var viewerBand = WorldDump.FloorDiv(actorH, WorldDump.LevelH);
        structure.SetShaderParameter("clip_h", clipH);
        glass.SetShaderParameter("clip_h", clipH);
        terrain.SetShaderParameter("clip_enabled", enabled && levelOnly);
        terrain.SetShaderParameter("clip_h", clipH);
        foreach (var material in new[] { terrain, structure })
        {
            material.SetShaderParameter("band_cut", bandCut);
            material.SetShaderParameter("viewer_band", viewerBand);
            material.SetShaderParameter("viewer_h", (float)actorH);
            // The front-wall stub rides the same gate as the level cut: it opens Niko's own storey,
            // and outside a building the level view does not apply.
            material.SetShaderParameter("wall_stub", enabled && !levelOnly && bandCut);
        }
        glass.SetShaderParameter("band_cut", bandCut);
        glass.SetShaderParameter("viewer_band", viewerBand);
        // The outline pass draws the silhouette of the cut terrain, so it shares the window uniforms.
        outline.SetShaderParameter("viewer_h", (float)actorH);
        // The two windows are independent: Niko's follows its own occluder (a building that covers
        // him never cuts the ground), and the cursor's opens on its own while it rests on ground
        // above Niko's half-metre line, so the hole follows the mouse on cliffs and banks.
        var aroundPlayer = enabled && !levelOnly && TerrainHidden(world, feet.X, feet.Z, actorH, probe);
        var cursorOn = enabled && !levelOnly && cursorTerrain is { } point && point.Y * 2f > actorH + 1f;
        var cursorCenter = cursorTerrain ?? actorPosition;
        UpdateCliffShader(terrain, root, camera, actorPosition, aroundPlayer, cursorCenter, cursorOn);
        UpdateCliffShader(structure, root, camera, actorPosition, aroundPlayer, cursorCenter, cursorOn);
        UpdateCliffShader(outline, root, camera, actorPosition, aroundPlayer, cursorCenter, cursorOn);
    }

    /// <summary>The camera's backward direction in unscaled mesh space, unit length.</summary>
    private static Vector3 RayToCamera(Node3D root, Camera3D camera) =>
        (root.GlobalTransform.Basis.Inverse() * camera.GlobalBasis.Z).Normalized();

    public static void UpdateCliffShader(ShaderMaterial material, Node3D root, Camera3D camera,
        Vector3 nikoCenter, bool nikoOn, Vector3 cursorCenter, bool cursorOn)
    {
        // Project local mesh coordinates through the root's vertical scale onto the camera axes.
        var projection = root.GlobalBasis.Transposed();
        material.SetShaderParameter("cliff_cut_center", nikoCenter);
        material.SetShaderParameter("cliff_cut_right", projection * camera.GlobalBasis.X);
        material.SetShaderParameter("cliff_cut_up", projection * camera.GlobalBasis.Y);
        // The camera ray in unscaled mesh coordinates, so the windows only open toward the camera.
        material.SetShaderParameter("cliff_cut_back", RayToCamera(root, camera));
        // One radius for both windows and the silhouette outline, so they never drift.
        material.SetShaderParameter("cliff_cut_radius", CliffCutRadius);
        material.SetShaderParameter("cliff_cutaway_enabled", nikoOn);
        material.SetShaderParameter("cursor_cut_center", cursorCenter);
        material.SetShaderParameter("cursor_cutaway_enabled", cursorOn);
    }

    public static float ClipH(WorldDump world, float x, float y, int viewerH, Vector3 ray)
    {
        int nx = (int)MathF.Floor(x), ny = (int)MathF.Floor(y);
        if (Roofed(world, nx, ny, viewerH) || Occluded(world, x, y, viewerH, ray)) return viewerH + HeadroomH;
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

    private static bool Occluded(WorldDump world, float x, float y, int viewerH, Vector3 ray)
    {
        var feetM = viewerH * 0.5f;
        for (var t = 0.25f; t < 72f; t += 0.2f)
        {
            float px = x + ray.X * t, py = y + ray.Y * t;
            var hh = (feetM + 0.9f + ray.Z * t) * 2f;
            if (hh <= viewerH + HeadroomH) continue;
            if (Structure(world, px, py, hh)) return true;
        }
        return false;
    }

    // A body-height ray from Niko toward the camera: anything solid above it hides him. The three
    // heights cover legs, torso and head, so the circle only lights up when he is really covered.
    private static readonly float[] BodyHeights = { 0.5f, 1.0f, 1.5f };

    /// <summary>True when natural terrain stands between the camera and Niko's body.</summary>
    public static bool TerrainHidden(WorldDump world, float x, float y, int viewerH, Vector3 ray)
    {
        var feetM = viewerH * 0.5f;
        foreach (var body in BodyHeights)
        for (var t = 0.25f; t < 72f; t += 0.2f)
        {
            float px = x + ray.X * t, py = y + ray.Y * t;
            int tx = (int)MathF.Floor(px), ty = (int)MathF.Floor(py);
            if (world.SolidTopH(tx, ty) is { } top && top * 0.5f >= feetM + body + ray.Z * t) return true;
        }
        return false;
    }

    /// <summary>
    /// The first natural-terrain point under a cursor ray, in unscaled mesh space (x, metres of
    /// height, y), or null when a structure or the sky is hit first: the window follows the cursor
    /// only over ground.
    /// </summary>
    public static Vector3? CursorTerrain(WorldDump world, Vector3 origin, Vector3 direction)
    {
        for (var t = 0f; t < 420f; t += 0.25f)
        {
            var p = origin + direction * t;
            if (Structure(world, p.X, p.Z, p.Y * 2f)) return null;
            int tx = (int)MathF.Floor(p.X), ty = (int)MathF.Floor(p.Z);
            if (world.SolidTopH(tx, ty) is { } top && p.Y <= top * 0.5f) return p;
        }
        return null;
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
