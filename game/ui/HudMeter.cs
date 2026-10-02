using System;

namespace EtherBound.Game.Ui;

/// <summary>
/// The arithmetic behind the ASCII meters (`STYLEGUIDE.md` [Sec. 12]) and the status column's
/// derived rows. Pure functions with no nodes, so the numbers are asserted in
/// `game.tests/HudMeterTests.cs` instead of read off a capture.
/// </summary>
public static class HudMeter
{
    /// <summary>Free carry threshold: the sim slows movement above it (`load_multiplier`).</summary>
    public const double FreeLoadKg = 10;

    /// <summary>The load meter's full scale: the single-object lift limit, not a sim cap.</summary>
    public const double LoadScaleKg = 40;

    public const int DefaultCells = 16;

    /// <summary>A bar split into normal cells, warning cells and empty cells; they sum to the total.</summary>
    public readonly record struct Bar(int Normal, int Warn, int Off)
    {
        public int Total => Normal + Warn + Off;
    }

    /// <summary>
    /// Fills <paramref name="value"/> of <paramref name="max"/> across <paramref name="cells"/>.
    /// Cells past <paramref name="warnAt"/> count as warning cells; pass <paramref name="max"/> to
    /// have none. A value above the maximum clamps to a full bar, a negative one to an empty bar.
    /// </summary>
    public static Bar Split(double value, double max, double warnAt, int cells = DefaultCells)
    {
        if (cells <= 0 || max <= 0 || double.IsNaN(value)) return new Bar(0, 0, Math.Max(0, cells));
        var on = (int)Math.Round(Math.Clamp(value / max, 0, 1) * cells, MidpointRounding.AwayFromZero);
        var normalCells = (int)Math.Round(Math.Clamp(warnAt / max, 0, 1) * cells, MidpointRounding.AwayFromZero);
        var normal = Math.Min(on, normalCells);
        return new Bar(normal, on - normal, cells - on);
    }

    /// <summary>The carried-weight meter: green up to the free 10 kg, yellow beyond, scaled to 40 kg.</summary>
    public static Bar Load(double loadKg, int cells = DefaultCells) => Split(loadKg, LoadScaleKg, FreeLoadKg, cells);

    /// <summary>How far an activity has run, 0 to 1. An activity with no length counts as complete.</summary>
    public static double Progress(int now, int startedMinute, int endsMinute)
    {
        var length = endsMinute - startedMinute;
        if (length <= 0) return 1;
        return Math.Clamp((double)(now - startedMinute) / length, 0, 1);
    }

    /// <summary>The activity meter: one colour, no warning zone.</summary>
    public static Bar Activity(int now, int startedMinute, int endsMinute, int cells = DefaultCells) =>
        Split(Progress(now, startedMinute, endsMinute), 1, 1, cells);
}
