using System;
using System.Collections.Generic;
using System.Linq;

namespace EtherBound.Game.Ui;

/// <summary>
/// The feed's rules without nodes: the game-minute stamp, the category behind each row's rule, the
/// row cap and how much a row fades with age. Asserted in `game.tests/FeedFormatTests.cs`.
/// </summary>
public static class FeedFormat
{
    /// <summary>History kept per session; the footer shows the newest three and scrolls through these.</summary>
    public const int MaxRows = 100;

    /// <summary>Rows visible in the footer slot without scrolling.</summary>
    public const int VisibleRows = 3;

    public enum Tone { Plain, Seen, Act, Warn, Fail, Ether }

    /// <summary>`HH:MM` of the day the game minute falls on.</summary>
    public static string Stamp(int gameMinute)
    {
        var minute = ((gameMinute % 1440) + 1440) % 1440;
        return $"{minute / 60:00}:{minute % 60:00}";
    }

    /// <summary>Maps a feed category to its tone. `rumor` and `harm` reuse the nearest existing tone.</summary>
    public static Tone ToneOf(string category) => category switch
    {
        "warn" or "rumor" => Tone.Warn,
        "fail" or "harm" => Tone.Fail,
        "act" => Tone.Act,
        "seen" => Tone.Seen,
        "ether" => Tone.Ether,
        _ => Tone.Plain,
    };

    /// <summary>
    /// Opacity of a row by how many rows are newer than it (0 is the newest): the visible three fade
    /// gently, anything older, reached only by scrolling, sits at 45 %.
    /// </summary>
    public static float Opacity(int newerRows) => newerRows switch
    {
        <= 0 => 1f,
        1 => 0.85f,
        2 => 0.7f,
        _ => 0.45f,
    };

    /// <summary>The rows to keep after adding one: the newest <see cref="MaxRows"/>, oldest first.</summary>
    public static IReadOnlyList<T> Cap<T>(IReadOnlyList<T> rows) =>
        rows.Count <= MaxRows ? rows : rows.Skip(rows.Count - MaxRows).ToArray();

    /// <summary>How many rows must be removed from the front to respect the cap.</summary>
    public static int Overflow(int count) => Math.Max(0, count - MaxRows);
}
