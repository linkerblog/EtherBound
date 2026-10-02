using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Engine;

/// <summary>A running activity as stored on the actor: the action is re-parsed at completion.</summary>
public sealed record ActivityState(string Op, JsonObject Action, int StartedMinute, int EndsMinute)
{
    public JsonObject ToJson() => Json.Obj(("op", Op), ("action", Action.DeepClone()), ("started_minute", StartedMinute), ("ends_minute", EndsMinute));

    public static ActivityState? From(JsonObject? json) => json is null ? null
        : new ActivityState(json.Str("op"), (JsonObject)json["action"]!.DeepClone(), json.Int("started_minute"), json.Int("ends_minute"));
}

public sealed record CarriedObject(int Id, string Kind, string Name, int Quantity, string Slot)
{
    public JsonObject ToJson() => Json.Obj(("id", Id), ("kind", Kind), ("name", Name), ("quantity", Quantity), ("slot", Slot));
}

/// <summary>The answer to one submit; the client and the minds read the actor from it.</summary>
public sealed record ActionResult(bool Accepted, string ActorId, GameAction Action, double X, double Y, int Z, int H,
    string? Reason, string? Text, ActivityState? Activity, IReadOnlyList<CarriedObject> Carried, double LoadKg,
    IReadOnlyList<PhysicsPosition> Trajectory)
{
    public JsonObject ToJson() => Json.Obj(("accepted", Accepted), ("actor_id", ActorId), ("action", Action.ToJson()),
        ("x", X), ("y", Y), ("z", Z), ("h", H), ("reason", Reason), ("text", Text), ("activity", Activity?.ToJson()),
        ("carried", new JsonArray(Carried.Select(c => (JsonNode)c.ToJson()).ToArray())), ("load_kg", LoadKg),
        ("trajectory", new JsonArray(Trajectory.Select(p => (JsonNode)p.ToJson()).ToArray())));
}

public sealed record ActorState(string Id, string Kind, double X, double Y, int Z, int H, ActivityState? Activity,
    IReadOnlyList<CarriedObject> Carried, double LoadKg, string? Name, Mind? Mind, ActorNeeds? Needs = null);

/// <summary>
/// Something an actor could use to satisfy a need: a food object or a drinkable tile within perception,
/// and a tile to stand on to reach it (<c>Need</c> is <c>hunger</c> or <c>thirst</c>).
/// </summary>
public sealed record Percept(string Need, int? ObjectId, int X, int Y, int H, int StandX, int StandY, int StandH);

public sealed record WorldState(long Seed, int GameMinute, int Speed, bool Paused, IReadOnlyList<ActorState> Actors,
    int GenVersion, string Generator, JsonObject GenOptions);

public sealed record MenuEntry(string Op, string Label, IReadOnlyList<string> Tags, bool Available, string? Reason,
    string? Subject, GameAction Action, int TileDx, int TileDy)
{
    public JsonObject ToJson() => Json.Obj(("op", Op), ("label", Label),
        ("tags", new JsonArray(Tags.Select(t => (JsonNode)JsonValue.Create(t)).ToArray())), ("available", Available),
        ("reason", Reason), ("subject", Subject), ("action", Action.ToJson()), ("tile_dx", TileDx), ("tile_dy", TileDy));
}

/// <summary>One scanned tile of a menu: its offset from the origin, surface height and material.</summary>
public sealed record MenuPlace(int Dx, int Dy, int H, string Label)
{
    public JsonObject ToJson() => Json.Obj(("dx", Dx), ("dy", Dy), ("h", H), ("label", Label));
}

public sealed record MenuPayload(double X, double Y, int Z, string Target, IReadOnlyList<MenuEntry> Entries, IReadOnlyList<MenuPlace> Places)
{
    public JsonObject ToJson() => Json.Obj(("x", X), ("y", Y), ("z", Z), ("target", Target),
        ("entries", new JsonArray(Entries.Select(e => (JsonNode)e.ToJson()).ToArray())),
        ("places", new JsonArray(Places.Select(p => (JsonNode)p.ToJson()).ToArray())));
}

/// <summary>A camera ray expressed in sim metres; vertical height is independent of the x/y grid.</summary>
public readonly record struct WorldRay(double X, double Y, double Height, double Dx, double Dy, double DHeight);

/// <summary>The first sim surface or volume touched by a ray.</summary>
public sealed record WorldPickHit(Target Target, int TileX, int TileY, int TileH, double HitX, double HitY, double HitHeight);

public sealed record ObjectPayload(int Id, string Kind, int X, int Y, int H, int Quantity, bool? Open);

/// <summary>What the client draws for one chunk; arrays are the grid's own, never mutated.</summary>
public sealed record ChunkPayload(int Cx, int Cy, int Revision, Chunk Chunk, IReadOnlyList<ChunkLevel> Levels, IReadOnlyList<ObjectPayload> Objects)
{
    public static ChunkPayload? Of(WorldGrid grid, int cx, int cy)
    {
        if (grid.Chunk(cx, cy) is not { } chunk) return null;
        var levels = grid.LevelsOfChunk(cx, cy).OrderBy(l => l.Z).ToList();
        var objects = grid.ObjectsAtChunk(cx, cy).Select(o => new ObjectPayload(o.Id, o.Kind, o.X, o.Y, o.H, o.Quantity, o.Open)).ToList();
        return new ChunkPayload(cx, cy, chunk.Revision, chunk, levels, objects);
    }
}
