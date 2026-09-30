using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.World.Gen;

namespace EtherBound.Sim.Engine;

/// <summary>Replays the engine's ordered mutating-input journal through the public engine API.</summary>
public static class InputReplay
{
    internal static JsonNode? EncodeDelta(double deltaSeconds) => double.IsFinite(deltaSeconds)
        ? Json.Of(deltaSeconds)
        : Json.Of(double.IsNaN(deltaSeconds) ? "NaN" : deltaSeconds > 0 ? "Infinity" : "-Infinity");

    public static WorldState Replay(WorldEngine engine, IEnumerable<InputJournalEntry> inputs)
    {
        long previous = 0;
        foreach (var input in inputs)
        {
            if (input.Sequence <= previous) throw new InvalidOperationException("input journal is not in strictly increasing sequence order");
            previous = input.Sequence;
            var payload = input.Payload;
            if (input.RepeatCount <= 0) throw new InvalidOperationException("input journal repeat count must be positive");
            for (var repeat = 0; repeat < input.RepeatCount; repeat++)
            {
                switch (input.Kind)
                {
                    case "ensure_world":
                        CheckGenerator(payload);
                        engine.EnsureWorld(payload["seed"]!.GetValue<long>());
                        break;
                    case "new_game":
                        CheckGenerator(payload);
                        engine.NewGame(payload["seed"]!.GetValue<long>(), payload.Str("generator"), payload["options"] as JsonObject,
                            payload["paused"]!.GetValue<bool>());
                        break;
                    case "submit":
                        engine.Submit(payload.Str("actor_id"), GameAction.Parse(payload["action"]!), ReadDelta(payload["delta_seconds"]));
                        break;
                    case "set_clock":
                        engine.SetClock(OptionalBool(payload, "paused"), OptionalInt(payload, "speed"));
                        break;
                    case "advance_time":
                        engine.AdvanceTime();
                        break;
                    case "set_goal":
                        engine.SetGoal(payload.Str("actor_id"), payload["goal"] is JsonObject goal ? GoalSpot.Parse(goal) : null,
                            payload.Str("reason"));
                        break;
                    default:
                        throw new InvalidOperationException($"unknown input journal kind: {input.Kind}");
                }
            }
        }
        return engine.GetState();
    }

    private static bool? OptionalBool(JsonObject payload, string key) =>
        payload[key] is { } value ? value.GetValue<bool>() : null;

    private static int? OptionalInt(JsonObject payload, string key) =>
        payload[key] is { } value ? Json.ToInt(value) : null;

    private static double ReadDelta(JsonNode? value)
    {
        if (value is JsonValue number && number.TryGetValue<double>(out var delta)) return delta;
        return value?.GetValue<string>() switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ => throw new InvalidOperationException("input journal has an invalid movement delta"),
        };
    }

    private static void CheckGenerator(JsonObject payload)
    {
        var key = payload.Str("generator");
        var spec = Generators.Get(key) ?? throw new InvalidOperationException($"input journal uses unknown generator: {key}");
        if (spec.Version != payload.Int("gen_version"))
            throw new InvalidOperationException($"input journal generator {key} is version {payload.Int("gen_version")}, current version is {spec.Version}");
    }
}
