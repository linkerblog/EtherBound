using EtherBound.Game.Ui;
using EtherBound.Host;
using Xunit;

namespace EtherBound.Game.Tests;

/// <summary>The debug console's need cells (`NeedFormat`): whole percents, clamping and the dash for Niko.</summary>
public sealed class NeedFormatTests
{
    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(1.0, "100%")]
    [InlineData(0.496, "50%")]
    [InlineData(0.2049, "20%")]
    [InlineData(0.2051, "21%")]
    [InlineData(-0.3, "0%")]
    [InlineData(1.7, "100%")]
    public void A_level_is_a_whole_percent_clamped_to_the_scale(double level, string text) =>
        Assert.Equal(text, NeedFormat.Percent(level));

    [Fact]
    public void An_actor_with_needs_gets_three_cells_in_order_and_one_without_gets_dashes()
    {
        Assert.Equal(("90%", "45%", "10%"), NeedFormat.Cells(new HostNeeds(0.9, 0.45, 0.1)));
        Assert.Equal((NeedFormat.None, NeedFormat.None, NeedFormat.None), NeedFormat.Cells(null));
    }
}
