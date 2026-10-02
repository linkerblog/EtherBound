using EtherBound.Sim.Core;
using EtherBound.Sim.Engine.Ops;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Engine;

public sealed partial class WorldEngine
{
    private const int MaxPerceptsPerNeed = 8;

    /// <summary>
    /// A read of what could feed or water the actor within <paramref name="radiusM"/>: food on tiles or in
    /// open containers on tiles, and shallow water. <b>A proxy for perception</b>: there is no line of
    /// sight and no memory yet, so the witnesses and knowledge work replaces this body and no caller.
    /// Carried food is not listed; the actor's own <c>Menu</c> already offers it. Nearest first.
    /// </summary>
    public IReadOnlyList<Percept> Percepts(string actorId, int radiusM)
    {
        var actor = _store.Actors.GetValueOrDefault(actorId) ?? throw new KeyNotFoundException($"unknown actor: {actorId}");
        var found = new List<(double Distance, Percept Percept)>();
        foreach (var row in _store.Objects.Values)
        {
            if (Catalog.Get(row.Kind) is not { } kind) continue;
            var need = kind.Edible is not null ? NeedCatalog.Hunger : kind.Drinkable is not null ? NeedCatalog.Thirst : null;
            if (need is null || RestingTile(row) is not { } at) continue;
            if (Math.Sqrt(Square(at.X + 0.5 - actor.X) + Square(at.Y + 0.5 - actor.Y)) > radiusM) continue;
            if (StandNear(actor, at.X, at.Y, at.H) is { } stand)
                found.Add((stand.Distance, new Percept(need, row.Id, at.X, at.Y, at.H, stand.X, stand.Y, stand.H)));
        }
        int ax = actor.TileX, ay = actor.TileY;
        for (var dy = -radiusM; dy <= radiusM; dy++)
        for (var dx = -radiusM; dx <= radiusM; dx++)
        {
            if (dx * dx + dy * dy > radiusM * radiusM) continue;
            int x = ax + dx, y = ay + dy;
            if (Grid.GroundAt(x, y) is not { } ground || Registry.Get(ground.SurfaceMat) is not { Drinkable: true }) continue;
            if (StandNear(actor, x, y, ground.GroundH) is { } stand)
                found.Add((stand.Distance, new Percept(NeedCatalog.Thirst, null, x, y, ground.GroundH, stand.X, stand.Y, stand.H)));
        }
        return found
            .OrderBy(p => p.Distance).ThenBy(p => p.Percept.X).ThenBy(p => p.Percept.Y).ThenBy(p => p.Percept.ObjectId ?? -1)
            .GroupBy(p => p.Percept.Need).SelectMany(g => g.Take(MaxPerceptsPerNeed))
            .OrderBy(p => p.Distance).ThenBy(p => p.Percept.X).ThenBy(p => p.Percept.Y).ThenBy(p => p.Percept.ObjectId ?? -1)
            .Select(p => p.Percept).ToList();
    }

    private static double Square(double value) => value * value;

    /// <summary>
    /// Where an object rests, following it out of containers that are open or lidless; null when it is
    /// carried, hidden in a closed container or inside something with no tile.
    /// </summary>
    private (int X, int Y, int H)? RestingTile(ObjectRow row)
    {
        for (var depth = 0; depth < ObjectHelpers.MaxContentDepth; depth++)
        {
            switch (row.Loc)
            {
                case "tile" when row.X is { } x && row.Y is { } y && row.H is { } h:
                    return (x, y, h);
                case "in" when row.ContainerId is { } id && _store.Objects.GetValueOrDefault(id) is { } container &&
                               Catalog.Get(container.Kind) is { } containerKind && ObjectHelpers.IsAccessible(containerKind, container):
                    row = container;
                    continue;
                default:
                    return null;
            }
        }
        return null;
    }

    /// <summary>The standing tile nearest the actor from which something at (x, y, h) is within the ops' close reach.</summary>
    private (int X, int Y, int H, double Distance)? StandNear(ActorRow actor, int x, int y, int h)
    {
        (int X, int Y, int H, double Distance)? best = null;
        foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int sx = x + dx, sy = y + dy;
            foreach (var surface in Grid.StandingSurfaces(sx, sy))
            {
                if (surface.H - Reach.DownH > h || h > surface.H + Reach.UpH) continue;
                if ((dx, dy) != (0, 0) && Grid.WallBetween(sx, sy, x, y, surface.H)) continue;
                var distance = Math.Sqrt(Square(sx + 0.5 - actor.X) + Square(sy + 0.5 - actor.Y)) + Math.Abs(surface.H - actor.H) * 0.5;
                if (best is null || distance < best.Value.Distance) best = (sx, sy, surface.H, distance);
            }
        }
        return best;
    }
}
