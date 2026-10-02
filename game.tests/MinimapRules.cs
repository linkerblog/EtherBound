using System.Collections.Immutable;
using EtherBound.Game.Ui;
using EtherBound.Host;
using Godot;
using Xunit;

namespace EtherBound.Game.Tests;

/// <summary>The minimap panel's arithmetic (Dev-010): where Niko is on the sim's image and which part of it is drawn.</summary>
public sealed class MinimapRules
{
    private const float E = 0.001f;

    // A 9x9-chunk window with its origin at chunk (-4, -4): Niko's own chunk (0, 0) is the middle one.
    private static HostMinimap Window(int originCx = -4, int originCy = -4) =>
        new(originCx, originCy, 9, 16, 2, ImmutableArray.CreateRange(new byte[144 * 144 * 3]));

    [Fact]
    public void A_tile_is_two_cells_per_side_of_the_chunk_origin()
    {
        var map = Window();

        Assert.Equal(new Vector2(64, 64), MinimapModel.CellOf(map, 0.0, 0.0));
        Assert.Equal(new Vector2(72, 80), MinimapModel.CellOf(map, 16.0, 32.0));
        Assert.Equal(new Vector2(63.5f, 63.75f), MinimapModel.CellOf(map, -1.0, -0.5));
    }

    [Fact]
    public void The_view_is_centred_on_niko_and_the_marker_sits_in_the_middle()
    {
        var map = Window();
        var region = MinimapModel.SourceRegion(map, 10.0, 20.0);

        Assert.Equal(MinimapModel.ViewCells, region.Size.X, E);
        Assert.Equal(69f - MinimapModel.ViewCells * 0.5f, region.Position.X, E);
        Assert.Equal(74f - MinimapModel.ViewCells * 0.5f, region.Position.Y, E);
        var marker = MinimapModel.MarkerFraction(map, 10.0, 20.0);
        Assert.Equal(0.5f, marker.X, E);
        Assert.Equal(0.5f, marker.Y, E);
    }

    [Fact]
    public void The_view_never_reads_past_the_image_and_the_marker_moves_off_centre_instead()
    {
        var map = Window();
        // Niko at the far edge of his chunk's window: the centred view would leave the image on the left.
        var region = MinimapModel.SourceRegion(map, -4 * 32.0 - 100.0, 0.0);

        Assert.Equal(0f, region.Position.X, E);
        Assert.True(region.End.X <= map.Cells + E);
        Assert.True(MinimapModel.MarkerFraction(map, -4 * 32.0 - 100.0, 0.0).X < 0.5f);

        var far = MinimapModel.SourceRegion(map, 4 * 32.0 + 100.0, 4 * 32.0 + 100.0);
        Assert.Equal(map.Cells, far.End.X, E);
        Assert.Equal(map.Cells, far.End.Y, E);
    }

    [Fact]
    public void Anywhere_in_his_chunk_niko_keeps_the_view_inside_the_image_and_near_the_centre()
    {
        var map = Window();
        foreach (var offset in new[] { 0.0, 0.5, 15.9, 31.9 })
        {
            var region = MinimapModel.SourceRegion(map, offset, offset);
            Assert.True(region.Position.X >= 0f && region.End.X <= map.Cells);
            Assert.Equal(0.5f, MinimapModel.MarkerFraction(map, offset, offset).X, E);
        }
    }

    [Fact]
    public void The_image_bytes_are_the_sims_rgb_cells()
    {
        var map = Window();

        Assert.Equal(map.Cells * map.Cells * 3, MinimapModel.Rgb(map).Length);
        Assert.Equal(144, map.Cells);
    }
}
