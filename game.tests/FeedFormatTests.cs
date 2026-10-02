using EtherBound.Game.Ui;
using Xunit;

namespace EtherBound.Game.Tests;

/// <summary>The feed's stamp, tone mapping, row cap and age fading (`FeedFormat`).</summary>
public sealed class FeedFormatTests
{
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(54, "00:54")]
    [InlineData(1439, "23:59")]
    [InlineData(1440, "00:00")]
    [InlineData(1440 + 125, "02:05")]
    public void The_stamp_is_the_time_of_day_of_the_game_minute(int minute, string expected) =>
        Assert.Equal(expected, FeedFormat.Stamp(minute));

    [Theory]
    [InlineData("seen", FeedFormat.Tone.Seen)]
    [InlineData("act", FeedFormat.Tone.Act)]
    [InlineData("warn", FeedFormat.Tone.Warn)]
    [InlineData("rumor", FeedFormat.Tone.Warn)]
    [InlineData("fail", FeedFormat.Tone.Fail)]
    [InlineData("harm", FeedFormat.Tone.Fail)]
    [InlineData("ether", FeedFormat.Tone.Ether)]
    [InlineData("info", FeedFormat.Tone.Plain)]
    [InlineData("anything else", FeedFormat.Tone.Plain)]
    public void A_category_maps_to_the_tone_of_its_rule(string category, FeedFormat.Tone tone) =>
        Assert.Equal(tone, FeedFormat.ToneOf(category));

    [Fact]
    public void The_history_is_capped_at_one_hundred_rows_keeping_the_newest()
    {
        var rows = Enumerable.Range(0, 130).ToArray();

        var kept = FeedFormat.Cap(rows);

        Assert.Equal(100, kept.Count);
        Assert.Equal(30, kept[0]);
        Assert.Equal(129, kept[^1]);
        Assert.Equal(30, FeedFormat.Overflow(130));
        Assert.Equal(0, FeedFormat.Overflow(100));
    }

    [Fact]
    public void A_history_under_the_cap_is_returned_as_is()
    {
        var rows = Enumerable.Range(0, 5).ToArray();

        Assert.Same(rows, FeedFormat.Cap(rows));
    }

    [Fact]
    public void Rows_fade_with_age_and_anything_past_the_visible_three_sits_at_forty_five_percent()
    {
        Assert.Equal(1f, FeedFormat.Opacity(0));
        Assert.Equal(0.85f, FeedFormat.Opacity(1));
        Assert.Equal(0.7f, FeedFormat.Opacity(2));
        Assert.Equal(0.45f, FeedFormat.Opacity(3));
        Assert.Equal(0.45f, FeedFormat.Opacity(99));
        Assert.Equal(3, FeedFormat.VisibleRows);
    }
}
