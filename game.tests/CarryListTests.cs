using EtherBound.Game.Ui;
using Xunit;

namespace EtherBound.Game.Tests;

/// <summary>The `CARRY` rows: one per hand, a single `HANDS` row for a two-handed object, `BACK`.</summary>
public sealed class CarryListTests
{
    [Fact]
    public void Nothing_carried_gives_three_empty_rows()
    {
        var rows = CarryList.Rows(Array.Empty<CarryList.Item>());

        Assert.Equal(new[] { "L", "R", "BACK" }, rows.Select(row => row.Slot));
        Assert.All(rows, row => Assert.Equal(CarryList.Empty, row.Text));
        Assert.All(rows, row => Assert.False(row.Filled));
    }

    [Fact]
    public void Each_hand_and_the_back_show_their_object_and_a_stack_count()
    {
        var rows = CarryList.Rows(new[]
        {
            new CarryList.Item("left", "Apple", 3),
            new CarryList.Item("right", "Shovel", 1),
            new CarryList.Item("back", "Backpack", 1),
        });

        Assert.Equal("Apple ×3", rows[0].Text);
        Assert.Equal("Shovel", rows[1].Text);
        Assert.Equal("Backpack", rows[2].Text);
    }

    [Fact]
    public void A_two_handed_object_replaces_both_hand_rows_with_one_hands_row()
    {
        var rows = CarryList.Rows(new[] { new CarryList.Item("both", "Sledgehammer", 1) });

        Assert.Equal(new[] { "HANDS", "BACK" }, rows.Select(row => row.Slot));
        Assert.Equal("Sledgehammer", rows[0].Text);
        Assert.True(rows[0].Filled);
    }

    [Fact]
    public void A_slot_the_panel_does_not_know_still_shows()
    {
        var rows = CarryList.Rows(new[] { new CarryList.Item("belt", "Pouch", 1) });

        Assert.Equal("BELT", rows[^1].Slot);
        Assert.Equal("Pouch", rows[^1].Text);
    }
}
