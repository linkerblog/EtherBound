using System.Globalization;
using EtherBound.Sim.Core;
using EtherBound.Sim.Events;
using EtherBound.Sim.World;
using static EtherBound.Sim.Engine.ObjectHelpers;

namespace EtherBound.Sim.Engine.Ops;

public sealed class MoveOp : OpHandler
{
    public const double WalkingSpeed = 4.0;

    public override string Op => "move";

    // Movement is WASD, never a menu entry.
    public override bool Applies(ActionContext ctx, Target target) => false;

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) =>
        throw new InvalidOperationException("move is never offered in a menu");

    public override string? Validate(ActionContext ctx, GameAction action) => null;

    public override Resolution Resolve(ActionContext ctx, GameAction action)
    {
        var from = ctx.ActorPos;
        var (x, y, h) = Movement.MoveInWorld(ctx.Actor.X, ctx.Actor.Y, ctx.Actor.H, action.Dx, action.Dy,
            WalkingSpeed * ctx.DeltaSeconds, ctx.Grid, ctx.LoadKg);
        (ctx.Actor.X, ctx.Actor.Y, ctx.Actor.H) = (x, y, h);
        ctx.Actor.Z = PyMath.FloorDiv(h, 6);
        var to = ctx.ActorPos;
        return from == to ? new Resolution() : new Resolution(new List<SimEvent> { SimEvent.ActorMoved(ctx.Actor.Id, from, to) });
    }
}

public sealed class WaitOp : OpHandler
{
    public const int Minutes = 15;

    public override string Op => "wait";

    public override bool Applies(ActionContext ctx, Target target) => target is SelfTarget;

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => new[] { GameAction.Wait() };

    public override string? Validate(ActionContext ctx, GameAction action) => null;

    public override int Duration(ActionContext ctx, GameAction action) => Minutes;

    // Waiting leaves no mark on the world; activity.finished is the whole record.
    public override Resolution Resolve(ActionContext ctx, GameAction action) => new();
}

public sealed class ClimbOp : OpHandler
{
    public const int Minutes = 1;
    // 1 m to 1.5 m: a person pulls up onto that, or lowers down from it, without a ladder.
    public const int MinH = 2;
    public const int MaxH = 3;
    public const int WalkableH = 1;

    public override string Op => "climb";

    private static bool Neighbour(ActionContext ctx, int x, int y) =>
        Math.Abs(x - ctx.ActorTile.X) + Math.Abs(y - ctx.ActorTile.Y) == 1;

    private static bool Climbable(ActionContext ctx, StandingSurface s) =>
        MinH <= Math.Abs(s.H - ctx.Actor.H) && Math.Abs(s.H - ctx.Actor.H) <= MaxH;

    public override bool Applies(ActionContext ctx, Target target)
    {
        if (target is not TileTarget tile || !Neighbour(ctx, tile.X, tile.Y)) return false;
        var surfaces = ctx.Grid.StandingSurfaces(tile.X, tile.Y);
        // A surface within a step means the actor would simply walk there.
        return surfaces.Count > 0 && !surfaces.Any(s => Math.Abs(s.H - ctx.Actor.H) <= WalkableH);
    }

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target)
    {
        var tile = (TileTarget)target;
        var surfaces = ctx.Grid.StandingSurfaces(tile.X, tile.Y);
        var climbable = surfaces.Where(s => Climbable(ctx, s)).ToList();
        var pool = climbable.Count > 0 ? climbable : surfaces.ToList();
        var nearest = pool.MinByFirst(s => Math.Abs(s.H - ctx.Actor.H));
        return new[] { GameAction.On("climb", new TileTarget(tile.X, tile.Y, nearest.H)) };
    }

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        var target = (TileTarget)action.Target!;
        if (!Neighbour(ctx, target.X, target.Y)) return "out of reach";
        var surface = ctx.Grid.StandingSurfaces(target.X, target.Y).Where(s => s.H == target.H).Cast<StandingSurface?>().FirstOrDefault();
        if (surface is null) return "nothing to stand on";
        if (!Climbable(ctx, surface.Value)) return "too high to climb";
        var (ax, ay) = ctx.ActorTile;
        if (ctx.Grid.WallBetween(ax, ay, target.X, target.Y, ctx.Actor.H) || ctx.Grid.WallBetween(ax, ay, target.X, target.Y, target.H))
            return "wall in the way";
        return null;
    }

    public override int Duration(ActionContext ctx, GameAction action) => Minutes;

    public override List<SimEvent> Complete(ActionContext ctx, GameAction action)
    {
        var target = (TileTarget)action.Target!;
        var from = ctx.ActorPos;
        (ctx.Actor.X, ctx.Actor.Y) = (target.X + 0.5, target.Y + 0.5);
        (ctx.Actor.H, ctx.Actor.Z) = (target.H, PyMath.FloorDiv(target.H, 6));
        return new List<SimEvent> { SimEvent.ActorMoved(ctx.Actor.Id, from, new TilePos(target.X, target.Y, target.H), "climb") };
    }
}

public sealed class DigOp : OpHandler
{
    // Bare hands are about four times slower than a shovel.
    public const double BareHandTool = 0.25;
    public const double HandDigMaxCost = 2.0;
    public const int MinutesPerCost = 30;
    public const int ReachH = 2;

    public override string Op => "dig";

    /// <summary><c>(ground_h, dug, removed, exposed)</c>; depth counts from the original ground.</summary>
    private static (int GroundH, int Dug, Material Removed, Material Exposed) Layers(ActionContext ctx, int x, int y)
    {
        var ground = ctx.Grid.GroundAt(x, y)!.Value;
        var registry = ctx.Grid.Registry;
        return (ground.GroundH, ground.Dug, registry[ctx.Grid.StratumAt(x, y, ground.Dug + 1)], registry[ctx.Grid.StratumAt(x, y, ground.Dug + 2)]);
    }

    /// <summary>The best <c>tool.dig</c> among the objects in the actor's hands, else bare hands.</summary>
    private static double ToolFactor(ActionContext ctx)
    {
        var best = BareHandTool;
        foreach (var row in Held(ctx.Session, ctx.Actor.Id))
            if (ctx.Grid.Catalog.Get(row.Kind)?.Tool?.Dig is { } dig) best = Math.Max(best, dig);
        return best;
    }

    public override bool Applies(ActionContext ctx, Target target)
    {
        if (target is not TileTarget tile) return false;
        // Only the ground surface is dug; a floor or roof above it is not ground.
        if (ctx.Grid.GroundAt(tile.X, tile.Y) is not { } ground || ground.GroundH != tile.H) return false;
        // Hides asphalt, water and glass; concrete and rock stay visible, as too hard.
        return ctx.Grid.Registry.Get(ground.SurfaceMat) is { Diggable: true };
    }

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => new[] { GameAction.On("dig", target) };

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        var tile = (TileTarget)action.Target!;
        int x = tile.X, y = tile.Y;
        if (ctx.Grid.GroundAt(x, y) is not { } ground) return "out of reach";
        var (groundH, _, removed, exposed) = Layers(ctx, x, y);
        var surface = ctx.Grid.Registry[ground.SurfaceMat];
        if (!Reach.InCloseReach(ctx, x, y) || Math.Abs(groundH - ctx.Actor.H) > ReachH) return "out of reach";
        if (ctx.Grid.FloorsAt(x, y).Any(floor => floor >= groundH)) return "floor in the way";
        if (!surface.Diggable || surface.DigCost > HandDigMaxCost || !removed.Diggable || removed.DigCost > HandDigMaxCost)
            return "too hard to dig by hand";
        if (!ctx.Grid.SolidAt(x, y, groundH - 1)) return "hollow below";
        if (!exposed.Walkable) return "hard layer below";
        if (AtCell(ctx, x, y, groundH).Count > 0) return "something is on it";
        if (ctx.OthersOn(x, y, groundH).Count > 0) return "someone is standing there";
        return null;
    }

    public override int Duration(ActionContext ctx, GameAction action)
    {
        var tile = (TileTarget)action.Target!;
        var removed = Layers(ctx, tile.X, tile.Y).Removed;
        return (int)Math.Ceiling(MinutesPerCost * removed.DigCost / ToolFactor(ctx));
    }

    public override List<SimEvent> Complete(ActionContext ctx, GameAction action)
    {
        var tile = (TileTarget)action.Target!;
        int x = tile.X, y = tile.Y;
        var (groundH, _, removed, exposed) = Layers(ctx, x, y);
        // Whoever stands on the tile goes down with the ground, or the standing rule would leave
        // them floating 0.5 m up and frozen there.
        var standing = ctx.Session.Actors().Where(a => (a.TileX, a.TileY, a.H) == (x, y, groundH)).ToList();
        var chunk = ctx.Grid.LowerGround(x, y);
        ctx.Session.MarkChunk(chunk.Cx, chunk.Cy);
        var newH = groundH - 1;
        var lowered = ctx.Grid.GroundAt(x, y)!.Value;
        var events = new List<SimEvent> { SimEvent.TerrainDug(ctx.Actor.Id, new TilePos(x, y, newH), removed.Key, exposed.Key, lowered.Dug) };
        foreach (var actor in standing)
        {
            var from = new TilePos(x, y, actor.H);
            (actor.H, actor.Z) = (newH, PyMath.FloorDiv(newH, 6));
            events.Add(SimEvent.ActorMoved(actor.Id, from, new TilePos(x, y, newH), "lowered"));
        }
        events.Add(SimEvent.ChunkChanged(chunk.Cx, chunk.Cy, chunk.Revision));
        return events;
    }
}

public sealed class InspectOp : OpHandler
{
    public const int RangeM = 30;

    public override string Op => "inspect";

    public override bool Applies(ActionContext ctx, Target target) => target switch
    {
        ActorTarget a => ctx.Session.GetActor(a.Id) is not null,
        ObjectTarget o => ctx.Session.GetObject(o.Id) is not null,
        TileTarget t => ctx.Grid.StandingSurfaces(t.X, t.Y).Count > 0,
        _ => false,
    };

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => new[] { GameAction.On("inspect", target) };

    public override string? Subject(ActionContext ctx, GameAction action) => action.Target switch
    {
        ActorTarget a => ctx.Session.GetActor(a.Id) is { } actor ? Reach.ActorName(actor) : null,
        ObjectTarget o => ctx.Session.GetObject(o.Id) is { } row ? ctx.Grid.Catalog.Get(row.Kind)?.Name : null,
        _ => null,
    };

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        var (ax, ay) = ctx.ActorTile;
        switch (action.Target)
        {
            case ActorTarget a:
                var actor = ctx.Session.GetActor(a.Id);
                if (actor is null) return "nobody there";
                return PyMath.Hypot(actor.X - ax, actor.Y - ay) > RangeM ? "too far to see" : null;
            case ObjectTarget o:
                var row = ctx.Session.GetObject(o.Id);
                if (row is null) return "nothing to see";
                if (row.Loc == "tile")
                {
                    if (row.X is null || row.Y is null) return "nothing to see";
                    if (PyMath.Hypot(row.X.Value - ax, row.Y.Value - ay) > RangeM) return "too far to see";
                }
                return null;
            default:
                var tile = (TileTarget)action.Target!;
                if (!ctx.Grid.StandingSurfaces(tile.X, tile.Y).Any(s => s.H == tile.H)) return "nothing to see";
                // No line of sight yet: that arrives with perception.
                return PyMath.Hypot(tile.X - ax, tile.Y - ay) > RangeM ? "too far to see" : null;
        }
    }

    // Looking changes nothing and emits no event; witnesses noticing it come later.
    public override Resolution Resolve(ActionContext ctx, GameAction action)
    {
        if (action.Target is ActorTarget a) return new Resolution(new List<SimEvent>(), ActorText(ctx, a.Id));
        if (action.Target is ObjectTarget o) return new Resolution(new List<SimEvent>(), ObjectText(ctx, o.Id));
        var tile = (TileTarget)action.Target!;
        var surface = ctx.Grid.StandingSurfaces(tile.X, tile.Y).First(s => s.H == tile.H);
        var material = ctx.Grid.Registry.Get(surface.MaterialId);
        var parts = new List<string> { material?.Name ?? "Unknown", Reach.Metres(surface.H) };
        if (ctx.Grid.GroundAt(tile.X, tile.Y) is { } ground && ground.GroundH == surface.H && ground.Dug > 0)
            parts.Add($"dug {Reach.Metres(ground.Dug)}");
        // Only the surface is visible: the strata below are never revealed.
        var visible = new List<string>();
        if (material is not null)
        {
            if (material.Diggable && material.DigCost <= DigOp.HandDigMaxCost) visible.Add("diggable");
            if (material.Flammable) visible.Add("flammable");
            if (material.Liquid) visible.Add("liquid");
        }
        if (visible.Count > 0) parts.Add(string.Join(", ", visible));
        return new Resolution(new List<SimEvent>(), string.Join(" · ", parts));
    }

    private static string ActorText(ActionContext ctx, string id)
    {
        if (ctx.Session.GetActor(id) is not { } actor) return "Unknown";
        var state = actor.Activity?["op"]?.GetValue<string>() == "wait" ? "Waiting"
            : actor.Mind?["goal"] is not null ? "Walking" : "Standing";
        return $"{Reach.ActorName(actor)}. {state}.";
    }

    private static string ObjectText(ActionContext ctx, int id)
    {
        if (ctx.Session.GetObject(id) is not { } row) return "Unknown";
        var kind = ctx.Grid.Catalog[row.Kind];
        var material = ctx.Grid.Registry.Get(kind.Material);
        var parts = new List<string> { kind.Name, material?.Name ?? kind.Material };
        if (kind.Openable) parts.Add(row.IsOpen ? "open" : "closed");
        if (kind.Container is not null && (!kind.Openable || row.IsOpen)) parts.Add($"holds {Children(ctx.Session, row.Id).Count} things");
        if (row.Loc is "held" or "worn")
            parts.Add(TotalMass(ctx.Session, ctx.Grid.Catalog, row).ToString("F1", CultureInfo.InvariantCulture) + " kg");
        return string.Join(" · ", parts);
    }
}

internal static class Seq
{
    /// <summary>Python's <c>min(items, key=...)</c>: the first item with the smallest key.</summary>
    public static T MinByFirst<T, TKey>(this IEnumerable<T> items, Func<T, TKey> key) where TKey : IComparable<TKey>
    {
        var found = false;
        T best = default!;
        TKey bestKey = default!;
        foreach (var item in items)
        {
            var k = key(item);
            if (!found || k.CompareTo(bestKey) < 0)
            {
                best = item;
                bestKey = k;
                found = true;
            }
        }
        return found ? best : throw new InvalidOperationException("min() arg is an empty sequence");
    }
}
