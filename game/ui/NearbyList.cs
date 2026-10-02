using System;
using System.Collections.Generic;
using System.Linq;

namespace EtherBound.Game.Ui;

/// <summary>
/// The `NEARBY` panel's rows. The client decides who is visible (an actor whose position projects
/// inside `GameViewport`) and passes only those here; this class orders and formats them, so the
/// list can never contain an actor the player cannot see. Asserted in
/// `game.tests/NearbyListTests.cs`.
/// </summary>
public static class NearbyList
{
    public const int MaxRows = 8;

    /// <summary>An actor Niko can currently see, with its distance from Niko in metres.</summary>
    public readonly record struct Entry(string Id, string Name, string Kind, double Distance, string? Activity);

    public readonly record struct Row(string Name, string Distance, string? Activity);

    /// <summary>Nearest first, ties broken by id so the order never flickers; at most eight rows.</summary>
    public static IReadOnlyList<Row> Rows(IEnumerable<Entry> visible) => visible
        .OrderBy(entry => entry.Distance)
        .ThenBy(entry => entry.Id, StringComparer.Ordinal)
        .Take(MaxRows)
        .Select(entry => new Row(entry.Name.ToUpperInvariant(), Metres(entry.Distance), entry.Activity?.ToUpperInvariant()))
        .ToArray();

    /// <summary>Whole metres, `&lt;1 m` under a metre.</summary>
    public static string Metres(double distance) =>
        distance < 1 ? "<1 m" : $"{(int)Math.Round(distance, MidpointRounding.AwayFromZero)} m";
}
