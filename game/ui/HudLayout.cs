using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The HUD's layout, solved in design units of the 2560x1440 frame and scaled to the window by
/// <see cref="HudTheme.Scale"/> (<c>window height / 1440</c>). Pure arithmetic, no nodes: the same
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
        Rect2 BuildButton, Rect2 Feed, Rect2 Input, Rect2 Compass, Rect2 Panel, float Scale);

    public static Frame Solve(Vector2 window) => Solve(window.Y / Height);

    public static Frame Solve(float scale)
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
        return new Frame(header, menu, viewport, status, footer, build, feed, input, compass, panel, scale);
    }

    /// <summary>The i-th menu button, top to bottom in the left column, in the column's own space.</summary>
    public static Rect2 MenuButton(int index, float scale) =>
        new(0, index * (MenuButtonHeight + MenuGap), MenuWidth, MenuButtonHeight);

    /// <summary>The 3D view in screen pixels for a window, which is what `PixelView` renders into.</summary>
    public static Rect2 ViewportPixels(Vector2 window) => Cells(Solve(window).Viewport, window.Y / Height);

    private static Rect2 Cells(Rect2 rect, float scale) =>
        new(rect.Position * scale, rect.Size * scale);
}
