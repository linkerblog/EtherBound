using EtherBound.Game.Ui;
using Xunit;

namespace EtherBound.Game.Tests;

/// <summary>`BuildSwatch`: which catalog colour a build slot's subject takes.</summary>
public sealed class BuildSwatchTests
{
    private static readonly (string Name, string Color)[] Materials =
    {
        ("Soil", "#8b623d"),
        ("Dark soil", "#704b31"),
        ("Brick", "#a86f5a"),
        ("Stone", "#71777b"),
        ("Stonework", "#999999"),
    };

    [Theory]
    [InlineData("Brick wall", "#a86f5a")]
    [InlineData("brick floor", "#a86f5a")]
    [InlineData("Soil wall", "#8b623d")]
    public void A_subject_takes_the_colour_of_its_material(string subject, string expected) =>
        Assert.Equal(expected, BuildSwatch.ColorOf(subject, Materials));

    [Fact]
    public void The_longest_matching_name_wins()
    {
        Assert.Equal("#704b31", BuildSwatch.ColorOf("Dark soil wall", Materials));
        Assert.Equal("#999999", BuildSwatch.ColorOf("Stonework wall", Materials));
    }

    [Fact]
    public void A_name_only_matches_whole_words()
    {
        var materials = new[] { ("Stone", "#71777b") };

        Assert.Null(BuildSwatch.ColorOf("Stonework wall", materials));
    }

    [Fact]
    public void An_unknown_subject_has_no_swatch() => Assert.Null(BuildSwatch.ColorOf("Glass wall", Materials));
}
