using System.Text.Json.Nodes;

namespace EtherBound.Sim.Core;

/// <summary>Leaf value types every layer shares (Dev-023's <c>core/types.py</c>).</summary>
public static class Ids
{
    /// <summary>The client and every actor use this id; a world has exactly one player.</summary>
    public const string Player = "niko";
}

public readonly record struct TilePos(int X, int Y, int H)
{
    public JsonObject ToJson() => Json.Obj(("x", X), ("y", Y), ("h", H));
}

/// <summary>What an action acts on: <c>self</c>, <c>tile</c>, <c>object</c>, <c>actor</c> or <c>edge</c>.</summary>
public abstract record Target(string Kind)
{
    public abstract JsonObject ToJson();

    public static Target Parse(JsonNode node) => node.Str("kind") switch
    {
        "self" => new SelfTarget(),
        "tile" => new TileTarget(node.Int("x"), node.Int("y"), node.Int("h")),
        "object" => new ObjectTarget(node.Int("id")),
        "actor" => new ActorTarget(node.Str("id")),
        "edge" => new EdgeTarget(node.Int("x"), node.Int("y"), node.Int("z"), ParseDirection(node.Str("direction"))),
        var kind => throw new ArgumentException($"unknown target kind {kind}"),
    };

    private static string ParseDirection(string value) =>
        value is "north" or "south" or "east" or "west" ? value : throw new ArgumentException($"bad direction {value}");
}

public sealed record SelfTarget() : Target("self")
{
    public override JsonObject ToJson() => Json.Obj(("kind", "self"));
}

public sealed record TileTarget(int X, int Y, int H) : Target("tile")
{
    public override JsonObject ToJson() => Json.Obj(("kind", "tile"), ("x", X), ("y", Y), ("h", H));
}

public sealed record ObjectTarget(int Id) : Target("object")
{
    public override JsonObject ToJson() => Json.Obj(("kind", "object"), ("id", Id));
}

public sealed record ActorTarget(string Id) : Target("actor")
{
    public override JsonObject ToJson() => Json.Obj(("kind", "actor"), ("id", Id));
}

public sealed record EdgeTarget(int X, int Y, int Z, string Direction) : Target("edge")
{
    public override JsonObject ToJson() => Json.Obj(("kind", "edge"), ("x", X), ("y", Y), ("z", Z), ("direction", Direction));
}

/// <summary>Where an object is: on a tile, in a container, held or worn.</summary>
public abstract record Location(string Kind)
{
    public abstract JsonObject ToJson();
}

public sealed record TileLoc(int X, int Y, int H) : Location("tile")
{
    public override JsonObject ToJson() => Json.Obj(("kind", "tile"), ("x", X), ("y", Y), ("h", H));
}

public sealed record InLoc(int ObjectId) : Location("in")
{
    public override JsonObject ToJson() => Json.Obj(("kind", "in"), ("object_id", ObjectId));
}

public sealed record HeldLoc(string ActorId, string Hand) : Location("held")
{
    public override JsonObject ToJson() => Json.Obj(("kind", "held"), ("actor_id", ActorId), ("hand", Hand));
}

public sealed record WornLoc(string ActorId, string Slot) : Location("worn")
{
    public override JsonObject ToJson() => Json.Obj(("kind", "worn"), ("actor_id", ActorId), ("slot", Slot));
}

/// <summary>One point of a resolved trajectory. <c>Id</c> is an object id or an actor id.</summary>
public sealed record PhysicsPosition(string Kind, JsonNode IdValue, double X, double Y, int H)
{
    public JsonObject ToJson() => Json.Obj(("kind", Kind), ("id", IdValue.DeepClone()), ("x", X), ("y", Y), ("h", H));

    public static PhysicsPosition Actor(string id, double x, double y, int h) => new("actor", JsonValue.Create(id), x, y, h);

    public static PhysicsPosition Object(int id, double x, double y, int h) => new("object", JsonValue.Create(id), x, y, h);
}

/// <summary>A standing tile; the tile is a metre square and <c>H</c> is in half-metres.</summary>
public sealed record GoalSpot(int X, int Y, int H, string Kind = "wander")
{
    public JsonObject ToJson() => Json.Obj(("x", X), ("y", Y), ("h", H), ("kind", Kind));

    public static GoalSpot Parse(JsonNode node) => new(node.Int("x"), node.Int("y"), node.Int("h"), node["kind"]?.GetValue<string>() ?? "wander");
}

/// <summary>The persisted sliver of an Extra: where it lives and, at most, one goal.</summary>
public sealed record Mind(TilePos Anchor, GoalSpot? Goal = null)
{
    public JsonObject ToJson() => Json.Obj(("anchor", Anchor.ToJson()), ("goal", Goal?.ToJson()));

    public static Mind Parse(JsonNode node) =>
        new(new TilePos(node["anchor"]!.Int("x"), node["anchor"]!.Int("y"), node["anchor"]!.Int("h")),
            node["goal"] is { } goal ? GoalSpot.Parse(goal) : null);
}
