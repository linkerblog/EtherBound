using EtherBound.Game.Ui;
using Xunit;

namespace EtherBound.Game.Tests;

/// <summary>The `NEARBY` rows: nearest first, stable ties, at most eight, whole-metre distances.</summary>
public sealed class NearbyListTests
{
    private static NearbyList.Entry Actor(string id, double distance, string? activity = null) =>
        new(id, $"Name {id}", "extra", distance, activity);

    [Fact]
    public void Rows_are_nearest_first_and_ties_break_by_id()
    {
        var rows = NearbyList.Rows(new[] { Actor("extra-003", 9), Actor("extra-002", 4), Actor("extra-001", 4) });

        Assert.Equal(new[] { "NAME EXTRA-001", "NAME EXTRA-002", "NAME EXTRA-003" }, rows.Select(row => row.Name));
    }

    [Fact]
    public void At_most_eight_rows_are_listed()
    {
        var rows = NearbyList.Rows(Enumerable.Range(0, 12).Select(i => Actor($"extra-{i:000}", i)));

        Assert.Equal(8, rows.Count);
        Assert.Equal("NAME EXTRA-007", rows[^1].Name);
    }

    [Theory]
    [InlineData(0.0, "<1 m")]
    [InlineData(0.99, "<1 m")]
    [InlineData(1.0, "1 m")]
    [InlineData(12.4, "12 m")]
    [InlineData(12.5, "13 m")]
    public void Distances_are_whole_metres_and_under_a_metre_is_marked(double distance, string expected) =>
        Assert.Equal(expected, NearbyList.Metres(distance));

    [Fact]
    public void The_activity_is_upper_cased_and_absent_when_idle()
    {
        var rows = NearbyList.Rows(new[] { Actor("a", 1, "dig"), Actor("b", 2) });

        Assert.Equal("DIG", rows[0].Activity);
        Assert.Null(rows[1].Activity);
    }

    [Fact]
    public void An_empty_input_gives_no_rows() => Assert.Empty(NearbyList.Rows(Array.Empty<NearbyList.Entry>()));
}
