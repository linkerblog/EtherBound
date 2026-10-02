using System;
using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The HUD's layout, solved in design units of the 2560x1440 frame and scaled to the window by
/// <see cref="HudTheme.Scale"/>: the largest scale at which the whole frame fits
/// (<c>min(width / 2560, height / 1440)</c>), centred in any spare width or height, so a maximized
/// window that is not exactly 16:9 still shows the entire HUD. Pure arithmetic, no nodes: the same
/// numbers feed the shell and the test in `game.tests/HudLayoutTests.cs`.
///
/// The plan fixes the frame, the 32-unit margins, the 40-unit gutters, the 200x50 menu buttons with
/// their 21-unit gap and the 1920x1152 viewport (30x18 tiles of 64). The header, footer and status
/// column widths below are derived so the frame adds up exactly to 2560x1440; confirm them against
/// the Figma frames when those are exported.
/// </summary>
public static class HudLayout
{
    public const float Width = 2560f;
    public const float Height = 1440f;
    public const float Margin = 32f;
    public const float Gutter = 40f;
    public const float HeaderHeight = 64f;
    public const float FooterHeight = 80f;
    public const float MenuWidth = 200f;
    public const float MenuButtonHeight = 50f;
    public const float MenuGap = 21f;
    public const float ViewportWidth = 1920f;
    public const float ViewportHeight = 1152f;
    public const float StatusWidth = Width - 2 * Margin - MenuWidth - Gutter - ViewportWidth - Gutter;
    public const float BuildButtonWidth = MenuWidth;
    public const float InputHeight = 40f;
    public const float InputWidth = 760f;
    public const float CompassSize = 160f;
    public const float PanelHeight = 360f;
    public const float PanelEmptyHeight = 240f;
    public const float TabHeight = 56f;
    public const float SlotHeight = 120f;
    public const int TilesAcross = 30;
    public const int TilesDown = 18;
    public const int TilePixels = 64;

    /// <summary>
    /// Every rectangle of the shell in design units, plus the scale to pixels. The header, menu,
    /// viewport, status and footer are frame-absolute; the compass and the build panel are local to
    /// the viewport, which is the control they live in.
    /// </summary>
    public sealed record Frame(
        Rect2 Header, Rect2 Menu, Rect2 Viewport, Rect2 Status, Rect2 Footer,
        Rect2 BuildButton, Rect2 Feed, Rect2 Input, Rect2 Compass, Rect2 Panel, float Scale, Vector2 Offset);

    /// <summary>The scale at which the whole design frame fits a window.</summary>
    public static float ScaleFor(Vector2 window) => Math.Min(window.X / Width, window.Y / Height);

    /// <summary>Where the scaled frame starts in window pixels: it is centred on both axes.</summary>
    public static Vector2 OffsetFor(Vector2 window)
    {
        var scale = ScaleFor(window);
        return new Vector2(Math.Max(0, (window.X - Width * scale) * 0.5f), Math.Max(0, (window.Y - Height * scale) * 0.5f));
    }

    public static Frame Solve(Vector2 window) => Solve(ScaleFor(window), OffsetFor(window));

    public static Frame Solve(float scale) => Solve(scale, Vector2.Zero);

    private static Frame Solve(float scale, Vector2 offset)
    {
        var header = new Rect2(Margin, Margin, Width - 2 * Margin, HeaderHeight);
        var band = Margin + HeaderHeight + Gutter;
        var menu = new Rect2(Margin, band, MenuWidth, ViewportHeight);
        var viewport = new Rect2(Margin + MenuWidth + Gutter, band, ViewportWidth, ViewportHeight);
        var status = new Rect2(viewport.End.X + Gutter, band, StatusWidth, ViewportHeight);
        var footer = new Rect2(Margin, band + ViewportHeight + Gutter, Width - 2 * Margin, FooterHeight);
        var build = new Rect2(footer.Position, new Vector2(BuildButtonWidth, FooterHeight));
        var right = footer.Position.X + BuildButtonWidth + Gutter;
        var input = new Rect2(footer.End.X - InputWidth, footer.Position.Y + (FooterHeight - InputHeight) * 0.5f,
            InputWidth, InputHeight);
        var feed = new Rect2(right, footer.Position.Y, input.Position.X - Gutter - right, FooterHeight);
        var compass = new Rect2(ViewportWidth - Gutter - CompassSize, ViewportHeight - Gutter - CompassSize,
            CompassSize, CompassSize);
        var panel = new Rect2(0, ViewportHeight - PanelHeight, ViewportWidth, PanelHeight);
        return new Frame(header, menu, viewport, status, footer, build, feed, input, compass, panel, scale, offset);
    }

    /// <summary>The i-th menu button, top to bottom in the left column, in the column's own space.</summary>
    public static Rect2 MenuButton(int index, float scale) =>
        new(0, index * (MenuButtonHeight + MenuGap), MenuWidth, MenuButtonHeight);

    /// <summary>The 3D view in screen pixels for a window, which is what `PixelView` renders into.</summary>
    public static Rect2 ViewportPixels(Vector2 window)
    {
        var frame = Solve(window);
        return new Rect2(frame.Viewport.Position * frame.Scale + frame.Offset, frame.Viewport.Size * frame.Scale);
    }
}
