using System;
using EtherBound.Host;

namespace EtherBound.Game.Ui;

/// <summary>
/// How the debug console writes needs: a whole percent per level, a dash for an actor with none (Niko).
/// Asserted in `game.tests/NeedFormatTests.cs`.
/// </summary>
public static class NeedFormat
{
    public const string None = "—";

    /// <summary>`0` to `100`, rounded, with the percent sign; levels outside 0..1 are clamped.</summary>
    public static string Percent(double level) => $"{(int)Math.Round(Math.Clamp(level, 0.0, 1.0) * 100.0, MidpointRounding.AwayFromZero)}%";

    /// <summary>The hunger, thirst and rest cells of the NPC table, in that order.</summary>
    public static (string Hunger, string Thirst, string Sleep) Cells(HostNeeds? needs) =>
        needs is null ? (None, None, None) : (Percent(needs.Hunger), Percent(needs.Thirst), Percent(needs.Rest));
}
