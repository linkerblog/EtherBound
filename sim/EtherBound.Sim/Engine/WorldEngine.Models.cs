using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Db;
using EtherBound.Sim.Events;

namespace EtherBound.Sim.Engine;

/// <summary>What models leave in the world's records: their decisions as events and their calls as a log (Dev-007, Dev-008).</summary>
public sealed partial class WorldEngine
{
    /// <summary>
    /// The only way a model's decision reaches the log (Dev-007 D8). The caller has already made the
    /// decision off the sim thread; the engine stamps it, journals it and commits it like any input,
    /// so a replay re-commits the recorded decision and never calls a model. Only declared kinds.
    /// </summary>
    public void RecordDecision(string actorId, string kind, JsonObject data)
    {
        if (!DecisionKinds.Contains(kind)) throw new ArgumentException($"undeclared decision kind: {kind}", nameof(kind));
        var session = NewSession();
        var world = session.World;
        if (session.GetActor(actorId) is null) throw new KeyNotFoundException($"unknown actor: {actorId}");
        session.RecordInput("record_decision", Json.Obj(("actor_id", Json.Of(actorId)), ("kind", Json.Of(kind)),
            ("data", data.DeepClone())));
        Publish(session, world, new List<SimEvent> { new(kind, actorId, (JsonObject)data.DeepClone()) });
        Bus.Drain();
    }

    /// <summary>
    /// Logs a model call with the game minute it happened at. Not a gameplay input: replay ignores it, and
    /// the table is pruned on startup (<see cref="PruneLlmCalls"/>).
    /// </summary>
    public void RecordLlmCall(string role, string model, int tokensIn, int tokensOut, double? costUsd, string outcome,
        string? error, string prompt, string response) =>
        _db.InsertLlmCall(new LlmCallRow(0, ClockState().GameMinute, role, model, tokensIn, tokensOut, costUsd, outcome, error, prompt, response));

    /// <summary>The newest <paramref name="limit"/> events of one type, oldest first.</summary>
    public List<EventRow> ReadLastEvents(string type, int limit) => _db.ReadLastEvents(type, limit);

    public List<LlmCallRow> ReadLlmCalls(int limit, string? role = null) => _db.ReadLlmCalls(limit, role);

    public void PruneLlmCalls(int keepFull, int keepRows, int cutTo) => _db.PruneLlmCalls(keepFull, keepRows, cutTo);

    public static readonly IReadOnlySet<string> DecisionKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "llm.interpreted",
        "llm.narrated",
    };
}
