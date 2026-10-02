using System.Text.Json.Nodes;
using EtherBound.Sim.Core;

namespace EtherBound.Sim.Tests;

/// <summary>
/// What a host test may compare after reloading its save and replaying its journal. The Extras wander on the host's real
/// clock, and <c>EnsureWorld</c> snaps or relocates one that was left mid-step, so their rows differ between a reload and a
/// replay by timing alone; Niko, the objects and the terrain do not. The event log is read before the reload, which is
/// where a relocation would add an event.
/// </summary>
internal static class ReplayCheck
{
    public static JsonObject Essential(JsonObject dump)
    {
        var kept = new JsonObject();
        foreach (var (key, value) in dump)
        {
            if (key == "actors")
                kept[key] = new JsonArray(value!.AsArray().Where(a => a!["id"]!.GetValue<string>() == Ids.Player)
                    .Select(a => a!.DeepClone()).ToArray());
            else
                kept[key] = value?.DeepClone();
        }
        return kept;
    }
}
