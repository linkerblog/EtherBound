using EtherBound.Sim.Core;
using EtherBound.Sim.Events;
using EtherBound.Sim.World;
using static EtherBound.Sim.Engine.ObjectHelpers;

namespace EtherBound.Sim.Engine.Ops;

/// <summary>
/// What the ops that satisfy a need share (<c>docs/Dev-011.md</c> [Sec. 4.2]). An actor with no needs
/// row (Niko, for now) can still eat, drink and sleep: nothing moves, and the op still happens.
/// </summary>
internal static class Needing
{
    /// <summary>A need this full is not worth a source: eating again would only waste the unit.</summary>
    public const double SatedLevel = 0.9;

    public static ActorNeeds? Needs(ActionContext ctx) => ActorNeeds.Parse(ctx.Actor.Needs);

    public static double Level(ActionContext ctx, string need) =>
        Needs(ctx)?.Level(need, ctx.World.GameMinute) ?? NeedModel.NeutralLevel;

    /// <summary>Raises <paramref name="need"/> by <paramref name="amount"/> and returns the new level, or null with no needs row.</summary>
    public static double? Satisfy(ActionContext ctx, string need, double amount)
    {
        if (Needs(ctx) is not { } needs) return null;
        var level = Math.Min(1.0, needs.Level(need, ctx.World.GameMinute) + amount);
        ctx.Actor.Needs = needs.With(need, level, ctx.World.GameMinute).ToJson();
        return level;
    }

    /// <summary>The same checks <c>take</c> makes before it touches a stack: reach, a closed lid, a load on top.</summary>
    public static string? ReachObject(ActionContext ctx, ObjectRow row)
    {
        if (!Reach.Object(ctx, row)) return "out of reach";
        if (row.Loc == "tile" && Handling.Supported(ctx, row)) return "something is on it";
        if (row.Loc == "in" && ctx.Session.GetObject(row.ContainerId!.Value) is { } container &&
            Handling.Kind(ctx, container) is { Openable: true } && !container.IsOpen) return "closed";
        return null;
    }

    /// <summary>Removes one unit of a stack (the whole row when it was the last) and refreshes the chunk it rested in.</summary>
    public static void TakeOne(ActionContext ctx, ObjectRow row, List<SimEvent> events)
    {
        var from = LocationOf(row);
        if (row.Quantity > 1) row.Quantity -= 1;
        else ctx.Session.DeleteObject(row);
        if (ChunkOfLocation(ctx, from) is { } cell) events.Add(BumpChunk(ctx, cell.Cx, cell.Cy));
    }
}

public sealed class EatOp : OpHandler
{
    public const int Minutes = 3;

    public override string Op => "eat";

    // A stack of food in the hands or a pack changes what the actor carries.
    public override bool ChangesLoad => true;

    public override bool Applies(ActionContext ctx, Target target) =>
        Handling.Row(ctx, target) is { } row && Handling.Kind(ctx, row).Edible is not null;

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => new[] { GameAction.On("eat", target) };

    public override string? Subject(ActionContext ctx, GameAction action) =>
        Handling.Row(ctx, action.Target) is { } row ? Handling.Subject(ctx, row) : null;

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        if (Handling.Row(ctx, action.Target) is not { } row) return "nothing there";
        if (Handling.Kind(ctx, row).Edible is null) return "not food";
        if (Needing.ReachObject(ctx, row) is { } reason) return reason;
        return Needing.Needs(ctx) is not null && Needing.Level(ctx, NeedCatalog.Hunger) > Needing.SatedLevel ? "not hungry" : null;
    }

    public override int Duration(ActionContext ctx, GameAction action) => Minutes;

    public override List<SimEvent> Complete(ActionContext ctx, GameAction action)
    {
        var row = Handling.Row(ctx, action.Target)!;
        var satiety = Handling.Kind(ctx, row).Edible!.Satiety;
        var kind = row.Kind;
        var events = new List<SimEvent>();
        Needing.TakeOne(ctx, row, events);
        var level = Needing.Satisfy(ctx, NeedCatalog.Hunger, satiety);
        events.Insert(0, SimEvent.ActorConsumed(ctx.Actor.Id, "eat", kind, 1, NeedCatalog.Hunger, level));
        return events;
    }
}

public sealed class DrinkOp : OpHandler
{
    public const int Minutes = 2;

    public override string Op => "drink";

    public override bool ChangesLoad => true;

    private static Material? Water(ActionContext ctx, TileTarget tile) =>
        ctx.Grid.GroundAt(tile.X, tile.Y) is { } ground && ground.GroundH == tile.H &&
        ctx.Grid.Registry.Get(ground.SurfaceMat) is { Drinkable: true } material ? material : null;

    public override bool Applies(ActionContext ctx, Target target) => target switch
    {
        TileTarget t => Water(ctx, t) is not null,
        ObjectTarget => Handling.Row(ctx, target) is { } row && Handling.Kind(ctx, row).Drinkable is not null,
        _ => false,
    };

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) => new[] { GameAction.On("drink", target) };

    public override string? Subject(ActionContext ctx, GameAction action) => action.Target switch
    {
        TileTarget t => Water(ctx, t)?.Name,
        ObjectTarget => Handling.Row(ctx, action.Target) is { } row ? Handling.Subject(ctx, row) : null,
        _ => null,
    };

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        if (action.Target is TileTarget tile)
        {
            if (Water(ctx, tile) is null) return "nothing to drink";
            if (!Reach.Tile(ctx, tile.X, tile.Y, tile.H)) return "out of reach";
        }
        else
        {
            if (Handling.Row(ctx, action.Target) is not { } row) return "nothing there";
            if (Handling.Kind(ctx, row).Drinkable is null) return "not drinkable";
            if (Needing.ReachObject(ctx, row) is { } reason) return reason;
        }
        return Needing.Needs(ctx) is not null && Needing.Level(ctx, NeedCatalog.Thirst) > Needing.SatedLevel ? "not thirsty" : null;
    }

    public override int Duration(ActionContext ctx, GameAction action) => Minutes;

    public override List<SimEvent> Complete(ActionContext ctx, GameAction action)
    {
        var events = new List<SimEvent>();
        string kind;
        double hydration;
        if (action.Target is TileTarget tile)
        {
            var water = Water(ctx, tile)!;
            (kind, hydration) = (water.Key, water.Hydration!.Value);
        }
        else
        {
            var row = Handling.Row(ctx, action.Target)!;
            (kind, hydration) = (row.Kind, Handling.Kind(ctx, row).Drinkable!.Hydration);
            Needing.TakeOne(ctx, row, events);
        }
        var level = Needing.Satisfy(ctx, NeedCatalog.Thirst, hydration);
        events.Insert(0, SimEvent.ActorConsumed(ctx.Actor.Id, "drink", kind, 1, NeedCatalog.Thirst, level));
        return events;
    }
}

public sealed class SleepOp : OpHandler
{
    /// <summary>Game minutes to go from empty to full rest.</summary>
    public const int FullMinutes = 480;

    /// <summary>Rested above this, there is nothing to sleep off.</summary>
    public const double RestedLevel = 0.95;

    public override string Op => "sleep";

    public override bool Applies(ActionContext ctx, Target target) => target is SelfTarget;

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target) =>
        new[] { GameAction.On("sleep", new SelfTarget()) };

    public override string? Validate(ActionContext ctx, GameAction action) =>
        Needing.Needs(ctx) is not null && Needing.Level(ctx, NeedCatalog.Rest) >= RestedLevel ? "not tired" : null;

    // Interrupted sleep recovers nothing: it is rest, not productive work.
    public override int Duration(ActionContext ctx, GameAction action) =>
        (int)Math.Ceiling((1.0 - Needing.Level(ctx, NeedCatalog.Rest)) * FullMinutes);

    public override List<SimEvent> Complete(ActionContext ctx, GameAction action)
    {
        var level = Needing.Satisfy(ctx, NeedCatalog.Rest, 1.0);
        return new List<SimEvent> { SimEvent.ActorSlept(ctx.Actor.Id, level) };
    }
}
