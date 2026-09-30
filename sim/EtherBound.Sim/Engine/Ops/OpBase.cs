using System.Globalization;
using System.Text.RegularExpressions;
using EtherBound.Sim.Core;
using EtherBound.Sim.Events;
using EtherBound.Sim.World;
using Tomlyn.Model;

namespace EtherBound.Sim.Engine.Ops;

/// <summary>What a handler sees: the session, the world row, the acting actor and the grid.</summary>
public sealed class ActionContext
{
    public ActionContext(Session session, WorldMetaRow world, ActorRow actor, WorldGrid grid, double deltaSeconds, double loadKg)
    {
        Session = session;
        World = world;
        Actor = actor;
        Grid = grid;
        DeltaSeconds = deltaSeconds;
        LoadKg = loadKg;
    }

    public Session Session { get; }
    public WorldMetaRow World { get; }
    public ActorRow Actor { get; }
    public WorldGrid Grid { get; }
    public double DeltaSeconds { get; }
    public double LoadKg { get; }

    public (int X, int Y) ActorTile => (Actor.TileX, Actor.TileY);

    public TilePos ActorPos => new(Actor.TileX, Actor.TileY, Actor.H);

    public List<ActorRow> OthersOn(int x, int y, int h) =>
        Session.Actors().Where(o => o.Id != Actor.Id && (o.TileX, o.TileY, o.H) == (x, y, h)).ToList();
}

/// <summary>An instant op's outcome: events, an optional line of text and a trajectory.</summary>
public sealed record Resolution(List<SimEvent> Events, string? Text = null, List<PhysicsPosition>? Trajectory = null)
{
    public Resolution() : this(new List<SimEvent>()) { }
}

/// <summary>
/// An op's behaviour (Dev-023 <c>BaseOp</c>): the defaults cover what most ops never need.
/// A handler is found by its key in <c>ops.toml</c>.
/// </summary>
public abstract class OpHandler
{
    public abstract string Op { get; }

    /// <summary>True when the op changes what the actor carries, so the load cache refreshes.</summary>
    public virtual bool ChangesLoad => false;

    /// <summary>Whether the op is shown in the menu at all for this target.</summary>
    public abstract bool Applies(ActionContext ctx, Target target);

    /// <summary>The exact action(s) the menu offers; one target can yield several entries.</summary>
    public abstract IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target);

    /// <summary>What the entry acts on, shown after the label; null for the target itself.</summary>
    public virtual string? Subject(ActionContext ctx, GameAction action) => null;

    /// <summary>The reason the action cannot be done now, or null.</summary>
    public abstract string? Validate(ActionContext ctx, GameAction action);

    /// <summary>Game minutes; 0 makes the op instant.</summary>
    public virtual int Duration(ActionContext ctx, GameAction action) => 0;

    /// <summary>Whether interrupted productive work can resume for the same action target.</summary>
    public virtual bool RetainsWorkProgress => false;

    public virtual Resolution Resolve(ActionContext ctx, GameAction action) =>
        throw new InvalidOperationException($"{Op} is an activity; it completes, it never resolves");

    public virtual List<SimEvent> Complete(ActionContext ctx, GameAction action) => new();
}

public sealed record OpSpec(string Key, string Label, string Group, IReadOnlyList<string> Targets, IReadOnlyList<string> Tags);

/// <summary>The op vocabulary from <c>ops.toml</c> and the handlers that give ops behaviour.</summary>
public static class OpCatalog
{
    private static readonly HashSet<string> TargetKinds = new() { "self", "tile", "edge", "object", "actor" };
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]*$");

    public static readonly IReadOnlyList<OpSpec> Specs = Load();

    private static readonly Dictionary<string, OpHandler> Handlers = Register(
        new MoveOp(), new ClimbOp(), new WaitOp(), new InspectOp(), new DigOp(), new BuildOp(),
        new TakeOp(), new DropOp(), new PutOp(), new OpenOp(), new CloseOp(), new WearOp(), new RemoveOp(),
        new PhysicsOp("push"), new PhysicsOp("pull"), new PhysicsOp("drag"), new PhysicsOp("throw"),
        new PhysicsOp("hit"), new PhysicsOp("break"));

    private static List<OpSpec> Load()
    {
        var document = DataFiles.Read("ops.toml");
        if (document["ops"] is not TomlTableArray definitions) throw new InvalidDataException("ops.toml must contain an ops array");
        var specs = new List<OpSpec>();
        foreach (var raw in definitions)
        {
            var key = raw.TryGetValue("key", out var k) ? (string)k : "";
            if (!KeyPattern.IsMatch(key)) throw new InvalidDataException($"op key must be snake_case ASCII: {key}");
            if (specs.Any(s => s.Key == key)) throw new InvalidDataException($"duplicate op key: {key}");
            var targets = ((TomlArray)raw["targets"]).Select(t => (string)t!).ToList();
            var unknown = targets.Where(t => !TargetKinds.Contains(t)).ToList();
            if (unknown.Count > 0) throw new InvalidDataException($"op {key}: unknown target kinds {string.Join(", ", unknown)}");
            var tags = ((TomlArray)raw["tags"]).Select(t => (string)t!).ToList();
            if (tags.Count == 0) throw new InvalidDataException($"op {key}: tags must not be empty");
            var label = raw.TryGetValue("label", out var l) ? (string)l : char.ToUpperInvariant(key[0]) + key[1..];
            specs.Add(new OpSpec(key, label, (string)raw["group"], targets, tags));
        }
        return specs;
    }

    internal static Dictionary<string, OpHandler> Register(params OpHandler[] handlers)
    {
        var registered = new Dictionary<string, OpHandler>();
        foreach (var handler in handlers)
        {
            if (Specs.All(s => s.Key != handler.Op)) throw new InvalidDataException($"op handler for a key missing from ops.toml: {handler.Op}");
            if (!registered.TryAdd(handler.Op, handler)) throw new InvalidDataException($"op handler already registered: {handler.Op}");
            if (!GameAction.Ops.Contains(handler.Op)) throw new InvalidDataException($"op handler without an action schema: {handler.Op}");
        }
        foreach (var op in GameAction.Ops)
            if (!registered.ContainsKey(op)) throw new InvalidDataException($"action schema without a handler: {op}");
        return registered;
    }

    public static OpHandler HandlerFor(string op) =>
        Handlers.TryGetValue(op, out var handler) ? handler : throw new KeyNotFoundException($"unknown op: {op}");

    /// <summary>Catalog entries that have behaviour, in catalog order.</summary>
    public static IEnumerable<(OpSpec Spec, OpHandler Handler)> Handled() =>
        Specs.Where(s => Handlers.ContainsKey(s.Key)).Select(s => (s, Handlers[s.Key]));
}

/// <summary>Reach and naming rules shared by the handlers (<c>ops/base.py</c>).</summary>
public static class Reach
{
    // From 1 m below the feet to 1.5 m above them.
    public const int DownH = 2;
    public const int UpH = 3;

    public static string Metres(int h) =>
        (h * 0.5).ToString("F1", CultureInfo.InvariantCulture).Replace(".0", "", StringComparison.Ordinal) + " m";

    /// <summary>What to call an actor in a menu or an inspect line; the player is always Niko.</summary>
    public static string ActorName(ActorRow actor) => actor.Name ?? (actor.Id == Ids.Player ? "Niko" : actor.Id);

    /// <summary>The actor's own tile, or an orthogonal neighbour with no wall on the shared edge.</summary>
    public static bool InCloseReach(ActionContext ctx, int x, int y)
    {
        var (ax, ay) = ctx.ActorTile;
        if ((x, y) == (ax, ay)) return true;
        if (Math.Abs(x - ax) + Math.Abs(y - ay) != 1) return false;
        return !ctx.Grid.WallBetween(ax, ay, x, y, ctx.Actor.H);
    }

    public static bool WithinHeight(ActionContext ctx, int h) => ctx.Actor.H - DownH <= h && h <= ctx.Actor.H + UpH;

    /// <summary>Whether an object can be touched, honouring carried objects and worn containers.</summary>
    public static bool Object(ActionContext ctx, ObjectRow obj) => obj.Loc switch
    {
        "held" or "worn" => true,
        "tile" => obj.X is not null && obj.Y is not null && obj.H is not null && InCloseReach(ctx, obj.X.Value, obj.Y.Value) && WithinHeight(ctx, obj.H.Value),
        "in" => ctx.Session.GetObject(obj.ContainerId ?? -1) is { } container && Object(ctx, container),
        _ => false,
    };

    public static bool Tile(ActionContext ctx, int x, int y, int h) => InCloseReach(ctx, x, y) && WithinHeight(ctx, h);
}
