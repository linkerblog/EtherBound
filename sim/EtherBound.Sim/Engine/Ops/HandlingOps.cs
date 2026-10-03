using EtherBound.Sim.Core;
using EtherBound.Sim.Events;
using EtherBound.Sim.World;
using static EtherBound.Sim.Engine.ObjectHelpers;

namespace EtherBound.Sim.Engine.Ops;

/// <summary>Shared rules of the handling ops (<c>ops/handling.py</c>).</summary>
internal static class Handling
{
    public const double LiftLimitKg = 40.0;
    public const int MaxContainerDepth = 32;

    public static ObjectRow? Row(ActionContext ctx, Target? target) =>
        target is ObjectTarget o ? ctx.Session.GetObject(o.Id) : null;

    public static ObjectKind Kind(ActionContext ctx, ObjectRow row) => ctx.Grid.Catalog[row.Kind];

    public static string Subject(ActionContext ctx, ObjectRow row) =>
        row.Quantity > 1 ? $"{Kind(ctx, row).Name} ×{row.Quantity}" : Kind(ctx, row).Name;

    /// <summary>Another tile object rests on this one's top, or an actor stands on it.</summary>
    public static bool Supported(ActionContext ctx, ObjectRow row)
    {
        var kind = Kind(ctx, row);
        // A non-solid object has no top to rest on: you stand beside it, not on it.
        if (!kind.Solid || row.X is null || row.Y is null || row.H is null) return false;
        var top = row.H.Value + kind.Height;
        if (AtCell(ctx, row.X.Value, row.Y.Value, top).Any(other => other.Id != row.Id && other.H == top)) return true;
        return ctx.Session.ActorsOn(row.X.Value, row.Y.Value).Any(a => a.H == top);
    }

    public static bool CellsFree(ActionContext ctx, int x, int y, int h, int height)
    {
        for (var offset = 1; offset <= Math.Max(height, 1); offset++)
            if (ctx.Grid.SolidAt(x, y, h + offset)) return false;
        return true;
    }

    public static bool ActorOn(ActionContext ctx, int x, int y, int h) =>
        ctx.Session.ActorsOn(x, y).Any(a => a.H == h);

    public static void EmitBumps(ActionContext ctx, Location from, Location to, List<SimEvent> events)
    {
        foreach (var loc in new[] { from, to })
            if (ChunkOfLocation(ctx, loc) is { } cell) events.Add(BumpChunk(ctx, cell.Cx, cell.Cy));
    }

    /// <summary>Merge a stackable row into an identical stack on that tile; returns the survivor id.</summary>
    public static int? MergeTile(ActionContext ctx, ObjectRow row, int x, int y, int h)
    {
        if (!Kind(ctx, row).Stackable) return null;
        foreach (var other in AtCell(ctx, x, y, h))
        {
            if (other.Id == row.Id) continue;
            if (other.Kind == row.Kind && Json.Same(other.State, row.State) && other.Owner == row.Owner && other.Integrity.Equals(row.Integrity))
            {
                other.Quantity += row.Quantity;
                ctx.Session.DeleteObject(row);
                return other.Id;
            }
        }
        return null;
    }
}

public sealed class TakeOp : OpHandler
{
    public override string Op => "take";
    public override bool ChangesLoad => true;

    public override bool Applies(ActionContext ctx, Target target) =>
        target is ObjectTarget && Handling.Row(ctx, target) is { } row && row.Loc is not ("held" or "worn");

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => new[] { GameAction.On("take", target) };

    public override string? Subject(ActionContext ctx, GameAction action) =>
        Handling.Row(ctx, action.Target) is { } row ? Handling.Subject(ctx, row) : null;

    private static string? Hand(ActionContext ctx, ObjectRow row)
    {
        if (Handling.Kind(ctx, row).TwoHanded) return Held(ctx.Session, ctx.Actor.Id).Count == 0 ? "both" : null;
        var free = FreeHands(ctx.Session, ctx.Actor.Id);
        return free.Count > 0 ? free[0] : null;
    }

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        if (Handling.Row(ctx, action.Target) is not { } row) return "nothing there";
        var kind = Handling.Kind(ctx, row);
        if (!Reach.Object(ctx, row)) return "out of reach";
        if (kind.Fixed) return "fixed in place";
        if (row.Loc == "tile" && Handling.Supported(ctx, row)) return "something is on it";
        if (TotalMass(ctx.Session, ctx.Grid.Catalog, row) > Handling.LiftLimitKg) return "too heavy";
        if (Hand(ctx, row) is null) return "hands full";
        if (row.Loc == "in" && ctx.Session.GetObject(row.ContainerId!.Value) is { } container)
        {
            var ckind = Handling.Kind(ctx, container);
            if (ckind.Openable && !container.IsOpen) return "closed";
        }
        return null;
    }

    public override Resolution Resolve(ActionContext ctx, GameAction action)
    {
        var row = Handling.Row(ctx, action.Target)!;
        var hand = Hand(ctx, row)!;
        var kind = Handling.Kind(ctx, row);
        var from = LocationOf(row);
        int? splitFrom = null;
        ObjectRow moved;
        if (kind.Stackable && row.Quantity > 1)
        {
            var split = new ObjectRow
            {
                Kind = row.Kind, Loc = "held", Quantity = 1, State = (System.Text.Json.Nodes.JsonObject)row.State.DeepClone(),
                Integrity = row.Integrity, Owner = row.Owner,
            };
            row.Quantity -= 1;
            SetHeld(split, ctx.Actor.Id, hand);
            ctx.Session.AddObject(split);
            moved = split;
            splitFrom = row.Id;
        }
        else
        {
            SetHeld(row, ctx.Actor.Id, hand);
            moved = row;
        }
        var events = new List<SimEvent>
        {
            SimEvent.ObjectMoved(ctx.Actor.Id, moved.Id, moved.Kind, moved.Quantity, "take", from, LocationOf(moved), splitFrom: splitFrom),
        };
        Handling.EmitBumps(ctx, from, LocationOf(moved), events);
        return new Resolution(events);
    }
}

public sealed class DropOp : OpHandler
{
    public override string Op => "drop";
    public override bool ChangesLoad => true;

    public override bool Applies(ActionContext ctx, Target target) =>
        target is ObjectTarget && Handling.Row(ctx, target) is { Loc: "held" };

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => new[] { GameAction.On("drop", target) };

    public override string? Subject(ActionContext ctx, GameAction action) =>
        Handling.Row(ctx, action.Target) is { } row ? Handling.Subject(ctx, row) : null;

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        if (Handling.Row(ctx, action.Target) is not { Loc: "held" } row) return "not holding it";
        var kind = Handling.Kind(ctx, row);
        var (x, y) = ctx.ActorTile;
        // The actor's own body fills the cells a solid object would need.
        if (kind.Solid) return "no room";
        return Handling.CellsFree(ctx, x, y, ctx.Actor.H, kind.Height) ? null : "no room";
    }

    public override Resolution Resolve(ActionContext ctx, GameAction action)
    {
        var row = Handling.Row(ctx, action.Target)!;
        var (x, y) = ctx.ActorTile;
        var from = LocationOf(row);
        var quantity = row.Quantity;
        var mergedInto = Handling.MergeTile(ctx, row, x, y, ctx.Actor.H);
        Location to;
        if (mergedInto is null)
        {
            SetTile(row, x, y, ctx.Actor.H);
            to = LocationOf(row);
        }
        else
        {
            to = new TileLoc(x, y, ctx.Actor.H);
        }
        var events = new List<SimEvent> { SimEvent.ObjectMoved(ctx.Actor.Id, row.Id, row.Kind, quantity, "drop", from, to, mergedInto: mergedInto) };
        Handling.EmitBumps(ctx, from, to, events);
        return new Resolution(events);
    }
}

public sealed class PutOp : OpHandler
{
    public override string Op => "put";
    public override bool ChangesLoad => true;

    /// <summary><c>(label, top_h)</c> for a destination; <c>top_h</c> is null for a container.</summary>
    private static (string Label, int? Top) Destination(ActionContext ctx, Target target)
    {
        if (target is TileTarget tile) return ("here", tile.H);
        if (target is not ObjectTarget || Handling.Row(ctx, target) is not { } row) return ("nowhere", null);
        var kind = Handling.Kind(ctx, row);
        return kind.Container is not null ? ($"into {kind.Name}", null) : ($"onto {kind.Name}", row.H!.Value + kind.Height);
    }

    public override bool Applies(ActionContext ctx, Target target)
    {
        if (target is TileTarget) return true;
        if (target is not ObjectTarget || Handling.Row(ctx, target) is not { } row) return false;
        var kind = Handling.Kind(ctx, row);
        return kind.Container is not null || kind.Surface;
    }

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target)
    {
        if (target is not (TileTarget or ObjectTarget)) return Array.Empty<GameAction>();
        return Held(ctx.Session, ctx.Actor.Id)
            .Where(held => !(target is ObjectTarget o && o.Id == held.Id))
            .Select(held => GameAction.Put(new ObjectTarget(held.Id), target)).ToList();
    }

    public override string? Subject(ActionContext ctx, GameAction action)
    {
        if (Handling.Row(ctx, action.Target) is not { } row) return null;
        return $"{Handling.Kind(ctx, row).Name} {Destination(ctx, action.Into!).Label}";
    }

    private static bool Cycle(ActionContext ctx, int destId, int targetId)
    {
        int? current = destId;
        for (var i = 0; i < Handling.MaxContainerDepth; i++)
        {
            if (current is null) return false;
            if (current == targetId) return true;
            if (ctx.Session.GetObject(current.Value) is not { Loc: "in" } row) return false;
            current = row.ContainerId;
        }
        return true;
    }

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        if (Handling.Row(ctx, action.Target) is not { Loc: "held" } row) return "not holding it";
        if (action.Into is ObjectTarget into)
        {
            if (ctx.Session.GetObject(into.Id) is not { } dest) return "nothing there";
            if (!Reach.Object(ctx, dest)) return "out of reach";
            var destKind = Handling.Kind(ctx, dest);
            if (destKind.Container is not null)
            {
                if (destKind.Openable && !dest.IsOpen) return "closed";
                if (ContentsBulk(ctx.Session, ctx.Grid.Catalog, dest.Id) + Handling.Kind(ctx, row).Bulk * row.Quantity > destKind.Container.Capacity)
                    return "doesn't fit";
            }
            if (Cycle(ctx, dest.Id, row.Id)) return "not into itself";
            if (destKind.Container is null)
            {
                var top = dest.H is not null ? dest.H.Value + destKind.Height : 0;
                return Placement(ctx, row, dest.X!.Value, dest.Y!.Value, top);
            }
            return null;
        }
        var tile = (TileTarget)action.Into!;
        if (!Reach.Tile(ctx, tile.X, tile.Y, tile.H)) return "out of reach";
        return Placement(ctx, row, tile.X, tile.Y, tile.H);
    }

    private static string? Placement(ActionContext ctx, ObjectRow row, int x, int y, int h)
    {
        if (!Handling.CellsFree(ctx, x, y, h, Handling.Kind(ctx, row).Height)) return "no room";
        return Handling.ActorOn(ctx, x, y, h) ? "someone is there" : null;
    }

    public override Resolution Resolve(ActionContext ctx, GameAction action)
    {
        var row = Handling.Row(ctx, action.Target)!;
        var from = LocationOf(row);
        var quantity = row.Quantity;
        int? mergedInto = null;
        Location to;
        if (action.Into is ObjectTarget into)
        {
            var dest = ctx.Session.GetObject(into.Id)!;
            var destKind = Handling.Kind(ctx, dest);
            if (destKind.Container is not null)
            {
                SetIn(row, dest.Id);
                to = new InLoc(dest.Id);
            }
            else
            {
                var top = dest.H!.Value + destKind.Height;
                mergedInto = Handling.MergeTile(ctx, row, dest.X!.Value, dest.Y!.Value, top);
                if (mergedInto is null) SetTile(row, dest.X.Value, dest.Y.Value, top);
                to = new TileLoc(dest.X.Value, dest.Y.Value, top);
            }
        }
        else
        {
            var tile = (TileTarget)action.Into!;
            mergedInto = Handling.MergeTile(ctx, row, tile.X, tile.Y, tile.H);
            if (mergedInto is null) SetTile(row, tile.X, tile.Y, tile.H);
            to = new TileLoc(tile.X, tile.Y, tile.H);
        }
        var events = new List<SimEvent> { SimEvent.ObjectMoved(ctx.Actor.Id, row.Id, row.Kind, quantity, "put", from, to, mergedInto: mergedInto) };
        Handling.EmitBumps(ctx, from, to, events);
        return new Resolution(events);
    }
}

public abstract class LidOp : OpHandler
{
    public override bool ChangesLoad => true;

    public override bool Applies(ActionContext ctx, Target target) =>
        target is ObjectTarget && Handling.Row(ctx, target) is { } row && Handling.Kind(ctx, row).Openable && Reach.Object(ctx, row);

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => new[] { GameAction.On(Op, target) };

    public override string? Subject(ActionContext ctx, GameAction action) =>
        Handling.Row(ctx, action.Target) is { } row ? Handling.Kind(ctx, row).Name : null;

    protected Resolution SetOpen(ActionContext ctx, GameAction action, bool opened)
    {
        var row = Handling.Row(ctx, action.Target)!;
        row.State["open"] = opened;
        var events = new List<SimEvent> { SimEvent.ObjectChanged(ctx.Actor.Id, row.Id, row.Kind, opened ? "open" : "close", Json.Obj(("open", opened))) };
        if (row.Loc == "tile" && row.Cx is not null && row.Cy is not null) events.Add(BumpChunk(ctx, row.Cx.Value, row.Cy.Value));
        return new Resolution(events);
    }
}

public sealed class OpenOp : LidOp
{
    public override string Op => "open";

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        if (Handling.Row(ctx, action.Target) is not { } row) return "nothing there";
        if (!Reach.Object(ctx, row)) return "out of reach";
        if (row.Loc == "tile" && Handling.Supported(ctx, row)) return "something is on it";
        return row.IsOpen ? "already open" : null;
    }

    public override Resolution Resolve(ActionContext ctx, GameAction action) => SetOpen(ctx, action, true);
}

public sealed class CloseOp : LidOp
{
    public override string Op => "close";

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        if (Handling.Row(ctx, action.Target) is not { } row) return "nothing there";
        if (!Reach.Object(ctx, row)) return "out of reach";
        return !row.IsOpen ? "already closed" : null;
    }

    public override Resolution Resolve(ActionContext ctx, GameAction action) => SetOpen(ctx, action, false);
}

public sealed class WearOp : OpHandler
{
    public override string Op => "wear";
    public override bool ChangesLoad => true;

    public override bool Applies(ActionContext ctx, Target target) =>
        target is ObjectTarget && Handling.Row(ctx, target) is { Loc: "held" } row && Handling.Kind(ctx, row).Wearable is not null;

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => new[] { GameAction.On("wear", target) };

    public override string? Subject(ActionContext ctx, GameAction action) =>
        Handling.Row(ctx, action.Target) is { } row ? Handling.Kind(ctx, row).Name : null;

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        if (Handling.Row(ctx, action.Target) is not { Loc: "held" } row) return "not holding it";
        var wearable = Handling.Kind(ctx, row).Wearable!;
        foreach (var worn in Worn(ctx.Session, ctx.Actor.Id))
            if (Handling.Kind(ctx, worn).Wearable is { } w && w.Slot == wearable.Slot) return "slot taken";
        return null;
    }

    public override Resolution Resolve(ActionContext ctx, GameAction action)
    {
        var row = Handling.Row(ctx, action.Target)!;
        var from = LocationOf(row);
        SetWorn(row, ctx.Actor.Id, Handling.Kind(ctx, row).Wearable!.Slot);
        var events = new List<SimEvent> { SimEvent.ObjectMoved(ctx.Actor.Id, row.Id, row.Kind, row.Quantity, "wear", from, LocationOf(row)) };
        Handling.EmitBumps(ctx, from, LocationOf(row), events);
        return new Resolution(events);
    }
}

public sealed class RemoveOp : OpHandler
{
    public override string Op => "remove";
    public override bool ChangesLoad => true;

    public override bool Applies(ActionContext ctx, Target target) =>
        target is ObjectTarget && Handling.Row(ctx, target) is { Loc: "worn" };

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => new[] { GameAction.On("remove", target) };

    public override string? Subject(ActionContext ctx, GameAction action) =>
        Handling.Row(ctx, action.Target) is { } row ? Handling.Kind(ctx, row).Name : null;

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        if (Handling.Row(ctx, action.Target) is not { Loc: "worn" }) return "not wearing it";
        return FreeHands(ctx.Session, ctx.Actor.Id).Count == 0 ? "hands full" : null;
    }

    public override Resolution Resolve(ActionContext ctx, GameAction action)
    {
        var row = Handling.Row(ctx, action.Target)!;
        var hand = FreeHands(ctx.Session, ctx.Actor.Id)[0];
        var from = LocationOf(row);
        SetHeld(row, ctx.Actor.Id, hand);
        var events = new List<SimEvent> { SimEvent.ObjectMoved(ctx.Actor.Id, row.Id, row.Kind, row.Quantity, "remove", from, LocationOf(row)) };
        Handling.EmitBumps(ctx, from, LocationOf(row), events);
        return new Resolution(events);
    }
}
