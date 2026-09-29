using EtherBound.Sim.Core;
using EtherBound.Sim.Engine.Ops;
using EtherBound.Sim.World;
using static EtherBound.Sim.Engine.ObjectHelpers;

namespace EtherBound.Sim.Engine;

/// <summary>Generated right-click entries (<c>engine/menu.py</c>). A read: the session is discarded.</summary>
public static class Menu
{
    private static readonly (int, int)[] Orthogonal = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int, int)[] Diagonal = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

    private static StandingSurface? Surface(WorldGrid grid, int x, int y, int z)
    {
        var surfaces = grid.StandingSurfaces(x, y);
        var visible = surfaces.Where(s => s.Z == z).ToList();
        if (visible.Count == 0) visible = surfaces.ToList();
        return visible.Count > 0 ? visible.MinByFirst(s => Math.Abs(s.Z - z)) : null;
    }

    /// <summary>Height and material name of the surface a menu reads on this tile.</summary>
    private static (int H, string Name)? Place(WorldGrid grid, MaterialRegistry registry, int x, int y, int z) =>
        Surface(grid, x, y, z) is { } chosen ? (chosen.H, registry.Get(chosen.MaterialId)?.Name ?? "unknown") : null;

    private static List<Target> Candidates(Session session, WorldGrid grid, ActorRow actor, IReadOnlyList<ActorRow> actors,
        int x, int y, int z, bool onOwnTile)
    {
        var candidates = new List<Target>();
        var chosen = Surface(grid, x, y, z);
        if (chosen is { } c) candidates.Add(new TileTarget(x, y, c.H));
        if (onOwnTile) candidates.Add(new SelfTarget());
        var surfaceZ = chosen?.Z ?? z;
        var tileRows = session.Objects().Where(o => o.Loc == "tile" && o.X == x && o.Y == y && o.H is not null && PyMath.FloorDiv(o.H.Value, 6) == surfaceZ).ToList();
        candidates.AddRange(tileRows.Select(o => new ObjectTarget(o.Id)));
        var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(x, y);
        var cell = ly * ChunkConst.Size + lx;
        var edges = new HashSet<(int, string)>();
        foreach (var level in grid.Levels.Values.OrderBy(l => l.Z))
        {
            if ((level.Cx, level.Cy) != (cx, cy)) continue;
            if (level.WallN[cell] != 0 && edges.Add((level.Z, "north"))) candidates.Add(new EdgeTarget(x, y, level.Z, "north"));
            if (level.WallW[cell] != 0 && edges.Add((level.Z, "west"))) candidates.Add(new EdgeTarget(x, y, level.Z, "west"));
            if ((level.SlotMask[cell] & ChunkConst.SlotHalfH) != 0 && edges.Add((level.Z, WallSlots.HalfH)))
                candidates.Add(new EdgeTarget(x, y, level.Z, WallSlots.HalfH));
            if ((level.SlotMask[cell] & ChunkConst.SlotHalfV) != 0 && edges.Add((level.Z, WallSlots.HalfV)))
                candidates.Add(new EdgeTarget(x, y, level.Z, WallSlots.HalfV));
        }
        // A bare edge has to be addressable or there is nowhere to build a wall (Dev-036 [Sec. 1]
        // F4). Only the actor's own tile at the band being read, or every tile of a radius-1 menu
        // would carry two dead edges.
        if (onOwnTile)
        {
            if (edges.Add((z, "north"))) candidates.Add(new EdgeTarget(x, y, z, "north"));
            if (edges.Add((z, "west"))) candidates.Add(new EdgeTarget(x, y, z, "west"));
        }
        var (actorX, actorY) = actor.TileX == x && actor.TileY == y ? (0, 0) : (x - actor.TileX, y - actor.TileY);
        var closeInterior = Math.Abs(actorX) + Math.Abs(actorY) == 0 ||
                            Math.Abs(actorX) + Math.Abs(actorY) == 1 &&
                            !grid.WallBetween(actor.TileX, actor.TileY, x, y, actor.H);
        if (closeInterior)
        {
            if (edges.Add((z, WallSlots.HalfH))) candidates.Add(new EdgeTarget(x, y, z, WallSlots.HalfH));
            if (edges.Add((z, WallSlots.HalfV))) candidates.Add(new EdgeTarget(x, y, z, WallSlots.HalfV));
        }
        var standH = chosen?.H ?? actor.H;
        candidates.AddRange(actors.Where(o => o.Id != actor.Id && (o.TileX, o.TileY, o.H) == (x, y, standH)).Select(o => new ActorTarget(o.Id)));
        foreach (var row in tileRows)
            if (grid.Catalog.Get(row.Kind) is { } kind && IsAccessible(kind, row))
                candidates.AddRange(Children(session, row.Id).Select(child => new ObjectTarget(child.Id)));
        if (onOwnTile)
        {
            candidates.AddRange(Held(session, actor.Id).Concat(Worn(session, actor.Id)).Select(o => new ObjectTarget(o.Id)));
            foreach (var row in Worn(session, actor.Id))
                if (grid.Catalog.Get(row.Kind) is { } kind && IsAccessible(kind, row))
                    candidates.AddRange(Children(session, row.Id).Select(child => new ObjectTarget(child.Id)));
        }
        return candidates;
    }

    public static MenuPayload Build(Session session, WorldGrid grid, MaterialRegistry registry, double loadKg, string actorId,
        double x, double y, int z, int radius = 0)
    {
        int tileX = PyMath.Floor(x), tileY = PyMath.Floor(y);
        var origin = Place(grid, registry, tileX, tileY, z);
        var label = origin is { } o ? $"{o.Name} · {Reach.Metres(o.H)}" : "nothing";
        var entries = new List<MenuEntry>();
        var places = new List<MenuPlace>();
        var actor = session.GetActor(actorId) ?? throw new KeyNotFoundException($"unknown actor: {actorId}");
        var ctx = new ActionContext(session, session.World, actor, grid, 0, loadKg);
        var actorTile = ctx.ActorTile;
        var offsets = new List<(int, int)> { (0, 0) };
        if (radius >= 1)
        {
            offsets.AddRange(Orthogonal.Where(d => Reach.InCloseReach(ctx, tileX + d.Item1, tileY + d.Item2)));
            offsets.AddRange(Diagonal.Where(d => Reach.InCloseReach(ctx, tileX + d.Item1, tileY) || Reach.InCloseReach(ctx, tileX, tileY + d.Item2)));
        }
        var actors = session.Actors();
        // Each candidate keeps the offset of the tile it came from, so its entries can say where
        // they act without the client resolving object ids to tiles.
        var candidates = new List<(Target Target, int Dx, int Dy)>();
        foreach (var (dx, dy) in offsets)
        {
            int nx = tileX + dx, ny = tileY + dy;
            var (h, name) = Place(grid, registry, nx, ny, z) ?? (z * 6, "nothing");
            places.Add(new MenuPlace(dx, dy, h, name));
            candidates.AddRange(Candidates(session, grid, actor, actors, nx, ny, z, (nx, ny) == actorTile).Select(t => (t, dx, dy)));
        }
        foreach (var (spec, handler) in OpCatalog.Handled())
        {
            foreach (var (target, dx, dy) in candidates)
            {
                if (!spec.Targets.Contains(target.Kind) || !handler.Applies(ctx, target)) continue;
                foreach (var action in handler.Builds(ctx, target))
                {
                    var reason = handler.Validate(ctx, action);
                    entries.Add(new MenuEntry(spec.Key, spec.Label, spec.Tags, reason is null, reason, handler.Subject(ctx, action), action, dx, dy));
                }
            }
        }
        return new MenuPayload(x, y, z, label, entries, places);
    }
}
