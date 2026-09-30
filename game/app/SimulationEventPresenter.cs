using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using EtherBound.Host;
using EtherBound.Sim.Core;

namespace EtherBound.Game.App;

public static class SimulationEventPresenter
{
    public static bool TryFormat(IReadOnlyList<HostSimulationEvent> events, out string text, out string category)
    {
        var detail = events.LastOrDefault(item => item.Type is "terrain.dug" or "wall.built" or "object.moved" or
            "object.changed" or "physics.resolved");
        if (detail is not null)
        {
            var data = JsonNode.Parse(detail.DataJson)!.AsObject();
            (text, category) = detail.Type switch
            {
                "terrain.dug" => ($"DUG {data.Str("removed").ToUpperInvariant()}", "act"),
                "wall.built" => ($"BUILT {data.Str("material").ToUpperInvariant()}", "act"),
                "object.moved" => ($"{data.Str("op").ToUpperInvariant()} {data.Str("kind").ToUpperInvariant()}", "act"),
                "object.changed" => ($"{data.Str("op").ToUpperInvariant()} {data.Str("kind").ToUpperInvariant()}", "act"),
                "physics.resolved" => (data.Str("op").ToUpperInvariant(), "act"),
                _ => ("", "info"),
            };
            return true;
        }

        var finished = events.LastOrDefault(item => item.Type == "activity.finished");
        if (finished is null)
        {
            text = "";
            category = "info";
            return false;
        }
        var outcome = JsonNode.Parse(finished.DataJson)!.AsObject();
        var op = outcome.Str("op").ToUpperInvariant();
        var result = outcome.Str("outcome");
        text = result == "completed" ? $"{op} DONE" : $"{op} {result.ToUpperInvariant()} {outcome["reason"]?.GetValue<string>()}";
        category = result == "completed" ? "act" : "fail";
        return true;
    }
}
