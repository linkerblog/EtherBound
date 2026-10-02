using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EtherBound.Llm.Narration;

/// <summary>
/// Turns committed player events into plain sentences the narrator may use. A fact names what happened and
/// never an id, a coordinate or a distance; an event type that is not worth telling (a step, a tick, a log
/// entry of a model decision) gives nothing.
/// </summary>
public static class NarrationFacts
{
    private static readonly Dictionary<string, string> MovedVerbs = new(StringComparer.Ordinal)
    {
        ["take"] = "picked up", ["drop"] = "dropped", ["put"] = "put down", ["throw"] = "threw",
        ["push"] = "pushed", ["pull"] = "pulled", ["drag"] = "dragged",
    };

    private static readonly Dictionary<string, string> ChangedVerbs = new(StringComparer.Ordinal)
    {
        ["open"] = "opened", ["close"] = "closed", ["wear"] = "put on", ["remove"] = "took off",
    };

    private static readonly Dictionary<string, string> FinishedVerbs = new(StringComparer.Ordinal)
    {
        ["wait"] = "Time passes.", ["climb"] = "Niko climbs.",
    };

    // `dig` and `build` already say what they did through terrain.dug and wall.built.
    private static readonly HashSet<string> CoveredByDetail = new(StringComparer.Ordinal) { "dig", "build", "take", "drop", "put", "push", "pull", "drag", "throw", "open", "close", "wear", "remove", "hit", "break" };

    private static readonly Regex Digits = new(@"\d", RegexOptions.Compiled);

    public static string? Summarize(string type, JsonObject data)
    {
        switch (type)
        {
            case "terrain.dug":
                return $"Niko dug through the {Words(data, "removed")}. Under it is {Words(data, "exposed")}.";
            case "wall.built":
                return data["kind"]?.GetValue<string>() == "floor"
                    ? $"Niko laid a {Words(data, "material")} floor."
                    : $"Niko built a {Words(data, "material")} wall.";
            case "object.moved":
            {
                var op = data["op"]?.GetValue<string>() ?? "";
                var verb = MovedVerbs.GetValueOrDefault(op, op);
                var amount = data["quantity"] is { } q && q.GetValue<int>() > 1 ? $"{q.GetValue<int>()} " : "the ";
                return $"Niko {verb} {amount}{Words(data, "kind")}.";
            }
            case "object.changed":
            {
                var op = data["op"]?.GetValue<string>() ?? "";
                return $"Niko {ChangedVerbs.GetValueOrDefault(op, op)} the {Words(data, "kind")}.";
            }
            case "physics.resolved":
            {
                var op = data["op"]?.GetValue<string>() ?? "";
                if (op is not ("hit" or "break")) return null;
                var broke = data["broken"] is JsonArray { Count: > 0 };
                return op == "hit" ? $"Niko hits something hard.{(broke ? " It breaks." : "")}" : "Niko smashes at it. It gives way.";
            }
            case "activity.finished":
            {
                var op = data["op"]?.GetValue<string>() ?? "";
                var outcome = data["outcome"]?.GetValue<string>();
                if (outcome == "completed") return FinishedVerbs.TryGetValue(op, out var text) ? text : CoveredByDetail.Contains(op) ? null : $"Niko finishes {op}.";
                if (outcome == "failed") return $"Niko cannot finish {op}: {data["reason"]?.GetValue<string>() ?? "it did not work"}.";
                return null;
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// What <c>inspect</c> says about a thing, with its numbers taken out ("Shovel · Wood · 1 m" becomes
    /// "Shovel, Wood"): the narrator is told never to state a measure, so it is not handed one.
    /// </summary>
    public static string? Inspection(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !Digits.IsMatch(p)).ToList();
        return parts.Count == 0 ? null : $"Niko looks closely: {string.Join(", ", parts)}.";
    }

    private static string Words(JsonObject data, string key) => (data[key]?.GetValue<string>() ?? "it").Replace('_', ' ').ToLowerInvariant();
}
