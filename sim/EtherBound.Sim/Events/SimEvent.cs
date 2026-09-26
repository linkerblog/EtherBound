using System.Text.Json.Nodes;
using EtherBound.Sim.Core;

namespace EtherBound.Sim.Events;

/// <summary>
/// A committed world fact (<c>events/models.py</c>). <see cref="Data"/> is exactly what the event
/// log stores: every field except seq, game_minute, type and actor_id. Unlogged events are
/// replication signals only.
/// </summary>
public sealed class SimEvent
{
    public SimEvent(string type, string? actorId, JsonObject data, bool logged = true)
    {
        Type = type;
        ActorId = actorId;
        Data = data;
        Logged = logged;
    }

    public string Type { get; }
    public string? ActorId { get; }
    public JsonObject Data { get; }
    public bool Logged { get; }
    public int Seq { get; set; }
    public int GameMinute { get; set; }

    public static SimEvent WorldGenerated(long seed, int genVersion, string generator, JsonObject options) =>
        new("world.generated", null, Json.Obj(("seed", seed), ("gen_version", genVersion), ("generator", generator), ("options", options.DeepClone())));

    public static SimEvent ActorSpawned(string actorId, string kind, TilePos tile, string reason, string? name) =>
        new("actor.spawned", actorId, Json.Obj(("kind", kind), ("tile", tile.ToJson()), ("reason", reason), ("name", name)));

    public static SimEvent ActorMoved(string actorId, TilePos from, TilePos to, string mode = "walk") =>
        new("actor.moved", actorId, Json.Obj(("from_tile", from.ToJson()), ("to_tile", to.ToJson()), ("mode", mode)));

    public static SimEvent ActorGoalSet(string actorId, JsonObject? goal, string reason) =>
        new("actor.goal_set", actorId, Json.Obj(("goal", goal), ("reason", reason)));

    public static SimEvent ActivityStarted(string actorId, string op, JsonObject target, int endsMinute) =>
        new("activity.started", actorId, Json.Obj(("op", op), ("target", target), ("ends_minute", endsMinute)));

    public static SimEvent ActivityFinished(string actorId, string op, string outcome, string? reason = null) =>
        new("activity.finished", actorId, Json.Obj(("op", op), ("outcome", outcome), ("reason", reason)));

    public static SimEvent TerrainDug(string actorId, TilePos tile, string removed, string exposed, int dug) =>
        new("terrain.dug", actorId, Json.Obj(("tile", tile.ToJson()), ("removed", removed), ("exposed", exposed), ("dug", dug)));

    public static SimEvent ObjectMoved(string actorId, int objectId, string kind, int quantity, string op, Location from,
        Location to, int? splitFrom = null, int? mergedInto = null) =>
        new("object.moved", actorId, Json.Obj(("object_id", objectId), ("kind", kind), ("quantity", quantity), ("op", op),
            ("from", from.ToJson()), ("to", to.ToJson()), ("split_from", splitFrom), ("merged_into", mergedInto)));

    public static SimEvent ObjectChanged(string actorId, int objectId, string kind, string op, JsonObject changes) =>
        new("object.changed", actorId, Json.Obj(("object_id", objectId), ("kind", kind), ("op", op), ("changes", changes)));

    public static SimEvent Impact(string actorId, JsonObject target, double energy) =>
        new("impact", actorId, Json.Obj(("target", target), ("energy", energy)));

    public static SimEvent PhysicsResolved(string actorId, string op, IEnumerable<PhysicsPosition> trajectory, JsonArray damage, JsonArray broken) =>
        new("physics.resolved", actorId, Json.Obj(("op", op), ("trajectory", new JsonArray(trajectory.Select(p => (JsonNode)p.ToJson()).ToArray())),
            ("damage", damage), ("broken", broken)));

    public static SimEvent ChunkChanged(int cx, int cy, int revision) =>
        new("chunk.changed", null, Json.Obj(("cx", cx), ("cy", cy), ("revision", revision)), logged: false);

    public static SimEvent ClockTicked() => new("clock.ticked", null, new JsonObject(), logged: false);

    public static SimEvent ClockChanged(int speed, bool paused) =>
        new("clock.changed", null, Json.Obj(("speed", speed), ("paused", paused)));
}
