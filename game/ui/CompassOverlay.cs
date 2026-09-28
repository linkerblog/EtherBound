using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// A small iso compass in the GAME view: it shows where north, east, south and west land on screen,
/// plus which side the camera looks from, so a cutaway problem can be reported by face.
/// </summary>
public partial class CompassOverlay : Control
{
    private static readonly Color FrameColor = new("#3a4a58");
    private static readonly Color AxisColor = new("#929daa");
    private static readonly Color NorthColor = new("#ff5c57");
    private static readonly Color TextColor = new("#d6d8de");

    public CompassOverlay()
    {
        Name = "Compass";
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(116, 116);
        Size = new Vector2(116, 116);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.05f, 0.08f, 0.11f, 0.7f));
        var center = Size * 0.5f;
        var radius = Mathf.Min(Size.X, Size.Y) * 0.5f - 30f;
        var font = GetThemeDefaultFont();
        var size = Mathf.Max(9, GetThemeDefaultFontSize() - 2);
        // The camera looks from the south-east, so land +x is (2, 1) and land +y is (-2, 1) on screen.
        var east = new Vector2(2, 1).Normalized();
        var south = new Vector2(-2, 1).Normalized();
        var north = new Vector2(2, -1).Normalized();
        var west = new Vector2(-2, -1).Normalized();
        // The tile diamond: its corners project to the four screen axis points.
        DrawPolyline(new[]
        {
            center + new Vector2(radius, 0), center + new Vector2(0, radius * 0.5f),
            center + new Vector2(-radius, 0), center + new Vector2(0, -radius * 0.5f),
            center + new Vector2(radius, 0),
        }, FrameColor, 2f, true);
        Axis(center, north, radius, font, size, NorthColor, "N");
        Axis(center, east, radius, font, size, TextColor, "E");
        Axis(center, south, radius, font, size, TextColor, "S");
        Axis(center, west, radius, font, size, TextColor, "W");
        DrawString(font, center + new Vector2(-radius, radius * 0.5f + 12f), "CAM",
            HorizontalAlignment.Center, radius * 2f, size, AxisColor);
    }

    private void Axis(Vector2 center, Vector2 dir, float radius, Font font, int size, Color color, string text)
    {
        DrawLine(center, center + dir * radius * 0.5f, AxisColor, 2f);
        var label = center + dir * radius * 0.68f;
        DrawString(font, label - new Vector2(12f, 0f), text, HorizontalAlignment.Center, 24f, size, color);
    }
}
