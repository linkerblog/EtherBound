using EtherBound.Game.Ui;
using Xunit;

namespace EtherBound.Game.Tests;

/// <summary>
/// The ASCII meters and the activity progress of the status column (`HudMeter`): cell counts, the
/// 10 kg warning boundary, clamping and the empty-length activity.
/// </summary>
public sealed class HudMeterTests
{
    [Theory]
    [InlineData(0, 0, 0, 16)]
    [InlineData(10, 4, 0, 12)]
    [InlineData(25, 4, 6, 6)]
    [InlineData(40, 4, 12, 0)]
    [InlineData(55, 4, 12, 0)]
    public void Load_is_green_up_to_ten_kilograms_and_yellow_beyond_on_a_forty_kilogram_scale(
        double kg, int normal, int warn, int off)
    {
        var bar = HudMeter.Load(kg);

        Assert.Equal(new HudMeter.Bar(normal, warn, off), bar);
        Assert.Equal(HudMeter.DefaultCells, bar.Total);
    }

    [Fact]
    public void A_negative_or_not_a_number_load_is_an_empty_bar()
    {
        Assert.Equal(new HudMeter.Bar(0, 0, 16), HudMeter.Load(-3));
        Assert.Equal(new HudMeter.Bar(0, 0, 16), HudMeter.Load(double.NaN));
    }

    [Fact]
    public void Cells_follow_the_requested_total()
    {
        var bar = HudMeter.Load(20, cells: 8);

        Assert.Equal(8, bar.Total);
        Assert.Equal(new HudMeter.Bar(2, 2, 4), bar);
    }

    [Theory]
    [InlineData(100, 100, 130, 0.0)]
    [InlineData(100, 115, 130, 0.5)]
    [InlineData(100, 130, 130, 1.0)]
    [InlineData(100, 250, 130, 1.0)]
    public void Activity_progress_runs_from_zero_to_one_and_clamps(int started, int now, int ends, double expected) =>
        Assert.Equal(expected, HudMeter.Progress(now, started, ends), 3);

    [Fact]
    public void An_activity_that_has_not_started_yet_is_zero_and_one_with_no_length_is_complete()
    {
        Assert.Equal(0.0, HudMeter.Progress(90, 100, 130));
        Assert.Equal(1.0, HudMeter.Progress(100, 100, 100));
        Assert.Equal(1.0, HudMeter.Progress(100, 130, 100));
    }

    [Fact]
    public void The_activity_meter_has_no_warning_zone()
    {
        var bar = HudMeter.Activity(now: 115, startedMinute: 100, endsMinute: 130);

        Assert.Equal(0, bar.Warn);
        Assert.Equal(8, bar.Normal);
        Assert.Equal(8, bar.Off);
    }
}
