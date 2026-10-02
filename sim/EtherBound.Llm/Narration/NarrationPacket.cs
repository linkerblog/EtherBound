using System.Text;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Llm.Narration;

/// <summary>
/// Everything the narrator is told, as text. It holds only what Niko perceived: the facts of his own actions, the
/// surface under him and the things he can see beside him, a word for the hour and the last few narrations. No
/// ids, no coordinates and nothing inside a closed container ever gets in.
/// </summary>
public sealed record NarrationPacket(string TimeWord, string Standing, IReadOnlyList<string> Nearby, IReadOnlyList<string> Facts,
    IReadOnlyList<string> Recent, int FromSeq, int ToSeq)
{
    public string ToUserMessage()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Time of day: {TimeWord}.");
        builder.AppendLine($"Niko stands on {Standing}.{(Nearby.Count > 0 ? $" Near him: {string.Join(", ", Nearby)}." : "")}");
        builder.AppendLine("What just happened:");
        foreach (var fact in Facts) builder.AppendLine($"- {fact}");
        if (Recent.Count > 0)
        {
            builder.AppendLine("Earlier lines, never to be repeated:");
            foreach (var line in Recent) builder.AppendLine($"- {line}");
        }
        builder.Append("Write the narration.");
        return builder.ToString();
    }

    /// <summary>The clock has no date: a word for the hour is all the narrator needs and all it gets.</summary>
    public static string TimeWordOf(int gameMinute) => ((gameMinute / 60) % 24) switch
    {
        >= 5 and < 8 => "dawn",
        >= 8 and < 12 => "morning",
        >= 12 and < 14 => "midday",
        >= 14 and < 18 => "afternoon",
        >= 18 and < 21 => "evening",
        _ => "night",
    };

    /// <summary>
    /// Built on the sim thread from the same read the radial menu makes, so it sees exactly what Niko can reach and no
    /// more: closed containers hide their contents there already.
    /// </summary>
    public static NarrationPacket Build(WorldState state, MenuPayload menu, IReadOnlyList<string> facts, IReadOnlyList<string> recent,
        int fromSeq, int toSeq, int maxNearby)
    {
        var standing = menu.Places.FirstOrDefault(p => p.Dx == 0 && p.Dy == 0)?.Label ?? "open ground";
        var nearby = menu.Entries
            .Where(e => e.Op == "inspect" && e.Action.Target is ObjectTarget or ActorTarget && !string.IsNullOrWhiteSpace(e.Subject))
            .Select(e => e.Subject!.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(maxNearby)
            .ToList();
        return new NarrationPacket(TimeWordOf(state.GameMinute), standing.ToLowerInvariant(), nearby, facts, recent, fromSeq, toSeq);
    }
}
