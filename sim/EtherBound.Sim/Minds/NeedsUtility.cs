using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Rng;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Minds;

/// <summary>
/// One way an Extra could meet a need now: an action to submit where it stands, or a goal to walk to.
/// Exactly one of <see cref="Action"/> and <see cref="Goal"/> is set.
/// </summary>
public sealed record NeedOption(string Need, double Score, GameAction? Action, GoalSpot? Goal);

/// <summary>
/// The Extras' utility step (<c>docs/Dev-011.md</c> [Sec. 3], D7): pure scoring, no state and no engine
/// calls. It ranks what the brain found (the actor's own <c>Menu</c>, the perception proxy, its anchor);
/// the brain submits the winner through the one action door. Utility is urgency minus distance over
/// <see cref="DistanceCost"/>; the usual gain term is 1 here, because no meal is larger than the room left
/// below a threshold of 0.5.
/// </summary>
public static class NeedsUtility
{
    /// <summary>Metres that cost one unit of urgency: a fully urgent need is worth a 30 m walk.</summary>
    public const double DistanceCost = 30.0;

    /// <summary>Options this close to the best are a toss-up, chosen with the actor's seeded stream.</summary>
    public const double TieBand = 0.05;

    /// <summary>How far an Extra notices food and water (D8): the radius the brain asks <c>Percepts</c> for.</summary>
    public const int PerceptionRadiusM = 12;

    /// <summary>A perception scan runs at most this often per Extra, on a phase derived from its id.</summary>
    public const int ScanEveryMinutes = 5;

    /// <summary>The need each op satisfies, for the ops a menu offers.</summary>
    private static string? NeedOf(string op) => op switch
    {
        "eat" => NeedCatalog.Hunger,
        "drink" => NeedCatalog.Thirst,
        _ => null,
    };

    private static readonly IReadOnlyDictionary<string, double> Content = new Dictionary<string, double>();

    /// <summary>Needs that want a source now, with how badly. Empty when the actor is content (the usual case, so nothing is allocated).</summary>
    public static IReadOnlyDictionary<string, double> Urgencies(ActorNeeds needs, int minute)
    {
        Dictionary<string, double>? result = null;
        foreach (var spec in NeedCatalog.Default.Specs)
            if (NeedModel.Urgency(spec, needs.Level(spec.Key, minute), minute) is > 0 and var urgency)
                (result ??= new Dictionary<string, double>(StringComparer.Ordinal))[spec.Key] = urgency;
        return result ?? Content;
    }

    /// <summary>Whether this Extra's perception scan falls on this minute. Not <c>string.GetHashCode</c>: it differs per process.</summary>
    public static bool ScanDue(string actorId, int minute)
    {
        var phase = 0;
        foreach (var c in actorId) phase = (phase * 31 + c) % ScanEveryMinutes;
        return ((minute % ScanEveryMinutes) + ScanEveryMinutes) % ScanEveryMinutes == phase;
    }

    /// <summary>Eating and drinking available where the actor stands: its own menu, carried food included.</summary>
    public static IEnumerable<NeedOption> FromMenu(MenuPayload menu, IReadOnlyDictionary<string, double> urgencies)
    {
        foreach (var entry in menu.Entries)
            if (entry.Available && NeedOf(entry.Op) is { } need && urgencies.TryGetValue(need, out var urgency))
                yield return new NeedOption(need, urgency, entry.Action, null);
    }

    /// <summary>Walks to a standing tile from which food or water is in reach.</summary>
    public static IEnumerable<NeedOption> FromPercepts(ActorState actor, IReadOnlyList<Percept> percepts,
        IReadOnlyDictionary<string, double> urgencies)
    {
        foreach (var percept in percepts)
        {
            if (!urgencies.TryGetValue(percept.Need, out var urgency)) continue;
            // Already there: the menu would have offered it, so walking here again would only loop.
            if ((PyMath.Floor(actor.X), PyMath.Floor(actor.Y), actor.H) == (percept.StandX, percept.StandY, percept.StandH)) continue;
            var distance = PyMath.Hypot(percept.StandX + 0.5 - actor.X, percept.StandY + 0.5 - actor.Y);
            yield return new NeedOption(percept.Need, urgency - distance / DistanceCost, null,
                new GoalSpot(percept.StandX, percept.StandY, percept.StandH, SeekKind));
        }
    }

    /// <summary>Sleep at the anchor, which is the Extra's home: here if it stands on it, else a walk there.</summary>
    public static IEnumerable<NeedOption> FromRest(ActorState actor, Mind mind, IReadOnlyDictionary<string, double> urgencies)
    {
        if (!urgencies.TryGetValue(NeedCatalog.Rest, out var urgency)) yield break;
        var anchor = mind.Anchor;
        if ((PyMath.Floor(actor.X), PyMath.Floor(actor.Y), actor.H) == (anchor.X, anchor.Y, anchor.H))
        {
            yield return new NeedOption(NeedCatalog.Rest, urgency, GameAction.On("sleep", new SelfTarget()), null);
            yield break;
        }
        var distance = PyMath.Hypot(anchor.X + 0.5 - actor.X, anchor.Y + 0.5 - actor.Y);
        yield return new NeedOption(NeedCatalog.Rest, urgency - distance / DistanceCost, null, new GoalSpot(anchor.X, anchor.Y, anchor.H, SeekKind));
    }

    public const string SeekKind = "seek";

    /// <summary>Best first. A toss-up among the leaders is settled by <paramref name="rng"/>, then the rest follow by score.</summary>
    public static IReadOnlyList<NeedOption> Rank(IEnumerable<NeedOption> options, PyRandom rng)
    {
        var ordered = options.OrderByDescending(o => o.Score).ToList();
        if (ordered.Count < 2) return ordered;
        var tied = ordered.Count(o => o.Score >= ordered[0].Score - TieBand);
        if (tied > 1)
        {
            var pick = rng.RandInt(0, tied - 1);
            (ordered[0], ordered[pick]) = (ordered[pick], ordered[0]);
        }
        return ordered;
    }
}
