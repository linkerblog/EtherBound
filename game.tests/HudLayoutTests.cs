using EtherBound.Game.Ui;
using Godot;
using Xunit;

namespace EtherBound.Game.Tests;

/// <summary>
/// The HUD layout numbers from `docs/Dev-004.md` [Sec. 3.1]: a 2560x1440 design frame laid out in
/// design units and scaled by <c>window height / 1440</c>. This replaces the "compare a screenshot by
/// hand" check for the frame arithmetic; the Figma comparison is still a person's call.
/// </summary>
public sealed class HudLayoutTests
{
    private const float E = 0.001f;

    [Fact]
    public void The_frame_is_2560x1440_with_32_margins_and_40_gutters()
    {
        var frame = HudLayout.Solve(new Vector2(HudLayout.Width, HudLayout.Height));

        Assert.Equal(1f, frame.Scale, E);
        Assert.Equal(HudLayout.Margin, frame.Header.Position.X, E);
        Assert.Equal(HudLayout.Margin, frame.Header.Position.Y, E);
        Assert.Equal(HudLayout.Width - 2 * HudLayout.Margin, frame.Header.Size.X, E);
        Assert.Equal(HudLayout.Width - HudLayout.Margin, frame.Footer.End.X, E);
        Assert.Equal(HudLayout.Height - HudLayout.Margin, frame.Footer.End.Y, E);

        // Left menu, viewport and status column, separated by exactly one gutter each.
        Assert.Equal(HudLayout.Margin, frame.Menu.Position.X, E);
        Assert.Equal(HudLayout.MenuWidth, frame.Menu.Size.X, E);
        Assert.Equal(frame.Menu.End.X + HudLayout.Gutter, frame.Viewport.Position.X, E);
        Assert.Equal(frame.Viewport.End.X + HudLayout.Gutter, frame.Status.Position.X, E);
        Assert.Equal(HudLayout.StatusWidth, frame.Status.Size.X, E);

        // Header, viewport and footer, separated by exactly one gutter each.
        Assert.Equal(frame.Header.End.Y + HudLayout.Gutter, frame.Menu.Position.Y, E);
        Assert.Equal(frame.Menu.Position.Y, frame.Viewport.Position.Y, E);
        Assert.Equal(frame.Viewport.Position.Y, frame.Status.Position.Y, E);
        Assert.Equal(frame.Viewport.End.Y + HudLayout.Gutter, frame.Footer.Position.Y, E);
    }

    [Fact]
    public void The_viewport_is_1920x1152_thirty_by_eighteen_tiles_of_64()
    {
        var viewport = HudLayout.Solve(1f).Viewport;

        Assert.Equal(1920f, viewport.Size.X, E);
        Assert.Equal(1152f, viewport.Size.Y, E);
        Assert.Equal(HudLayout.TilesAcross * HudLayout.TilePixels, viewport.Size.X, E);
        Assert.Equal(HudLayout.TilesDown * HudLayout.TilePixels, viewport.Size.Y, E);
    }

    [Fact]
    public void The_menu_is_three_200x50_buttons_with_a_21_gap()
    {
        var first = HudLayout.MenuButton(0, 1f);
        var second = HudLayout.MenuButton(1, 1f);
        var third = HudLayout.MenuButton(2, 1f);

        Assert.Equal(Vector2.Zero, first.Position);
        Assert.Equal(new Vector2(200, 50), first.Size);
        Assert.Equal(first.Position.X, second.Position.X, E);
        Assert.Equal(first.Position.X, third.Position.X, E);
        Assert.Equal(HudLayout.MenuButtonHeight + HudLayout.MenuGap, second.Position.Y - first.Position.Y, E);
        Assert.Equal(HudLayout.MenuWidth, third.Size.X, E);
        Assert.True(third.End.Y <= HudLayout.Solve(1f).Menu.Size.Y);
    }

    [Fact]
    public void The_build_button_opens_the_panel_docked_at_the_bottom_of_the_viewport()
    {
        var frame = HudLayout.Solve(1f);

        Assert.Equal(HudLayout.BuildButtonWidth, frame.BuildButton.Size.X, E);
        Assert.Equal(frame.Footer.Size.Y, frame.BuildButton.Size.Y, E);
        Assert.Equal(frame.Viewport.Size.X, frame.Panel.Size.X, E);
        // The panel is local to the viewport, so both its x origin and its end are viewport-local.
        Assert.Equal(frame.Viewport.Size.Y, frame.Panel.End.Y, E);
        Assert.Equal(0f, frame.Panel.Position.X, E);
        Assert.Equal(HudLayout.ViewportHeight - HudLayout.PanelHeight, frame.Panel.Position.Y, E);
    }

    [Fact]
    public void The_footer_has_the_feed_and_the_input_line_right_of_build()
    {
        var frame = HudLayout.Solve(1f);

        Assert.True(frame.Feed.Position.X > frame.BuildButton.End.X);
        Assert.Equal(frame.Footer.End.X, frame.Input.End.X, E);
        Assert.Equal(frame.Feed.End.X + HudLayout.Gutter, frame.Input.Position.X, E);
        Assert.Equal(frame.Footer.Size.Y, frame.Feed.Size.Y, E);
        Assert.Equal(HudLayout.InputHeight, frame.Input.Size.Y, E);
    }

    [Fact]
    public void The_compass_sits_inside_the_viewport_bottom_right()
    {
        var frame = HudLayout.Solve(1f);

        Assert.Equal(frame.Viewport.Size.X - HudLayout.Gutter, frame.Compass.End.X, E);
        Assert.Equal(frame.Viewport.Size.Y - HudLayout.Gutter, frame.Compass.End.Y, E);
        Assert.Equal(HudLayout.CompassSize, frame.Compass.Size.X, E);
    }

    [Fact]
    public void The_scale_follows_the_window_height_and_the_window_rect_is_the_viewport_in_pixels()
    {
        Assert.Equal(1f, HudLayout.Solve(new Vector2(2560, 1440)).Scale, E);
        Assert.Equal(0.5f, HudLayout.Solve(new Vector2(1280, 720)).Scale, E);

        var pixels = HudLayout.ViewportPixels(new Vector2(1280, 720));
        Assert.Equal(new Rect2(136, 68, 960, 576), pixels);
    }

    [Fact]
    public void A_wider_than_16_9_window_scales_by_height_and_centres_the_frame()
    {
        // A maximized 2560x1440 monitor loses height to the title bar and the taskbar.
        var window = new Vector2(2560, 1369);
        var frame = HudLayout.Solve(window);

        Assert.Equal(1369f / 1440f, frame.Scale, E);
        Assert.Equal(0.951f, frame.Scale, 0.001f);
        Assert.Equal(0f, frame.Offset.Y, E);
        Assert.Equal((2560f - 2560f * frame.Scale) * 0.5f, frame.Offset.X, E);
        Assert.True(frame.Offset.X > 0);

        // The scaled frame sits wholly inside the window: no rectangle goes past either edge.
        var right = frame.Offset.X + frame.Footer.End.X * frame.Scale;
        var bottom = frame.Offset.Y + frame.Footer.End.Y * frame.Scale;
        Assert.True(right <= window.X + E);
        Assert.True(bottom <= window.Y + E);
        Assert.Equal(frame.Offset.X, window.X - frame.Offset.X - 2560f * frame.Scale, E);
    }

    [Fact]
    public void A_taller_than_16_9_window_scales_by_width_and_centres_vertically()
    {
        var frame = HudLayout.Solve(new Vector2(1280, 800));

        Assert.Equal(0.5f, frame.Scale, E);
        Assert.Equal(0f, frame.Offset.X, E);
        Assert.Equal(40f, frame.Offset.Y, E);
    }

    [Fact]
    public void The_design_size_window_has_scale_one_and_no_offset()
    {
        var frame = HudLayout.Solve(new Vector2(2560, 1440));

        Assert.Equal(1f, frame.Scale, E);
        Assert.Equal(Vector2.Zero, frame.Offset);
        Assert.Equal(new Rect2(32 + 200 + 40, 32 + 64 + 40, 1920, 1152), HudLayout.ViewportPixels(new Vector2(2560, 1440)));
    }

    [Fact]
    public void The_viewport_in_pixels_includes_the_centring_offset()
    {
        var window = new Vector2(2560, 1369);
        var frame = HudLayout.Solve(window);
        var pixels = HudLayout.ViewportPixels(window);

        Assert.Equal(frame.Viewport.Position.X * frame.Scale + frame.Offset.X, pixels.Position.X, E);
        Assert.Equal(1920f * frame.Scale, pixels.Size.X, E);
        Assert.Equal(1152f * frame.Scale, pixels.Size.Y, E);
    }
}
