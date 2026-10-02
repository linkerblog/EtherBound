using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// Faint scanlines laid over a HUD panel. A child of a panel container fills the panel's content
/// area, so this needs no layout of its own. Never added over the 3D view.
/// </summary>
public partial class Scanlines : ColorRect
{
    private static Shader? _shader;

    public Scanlines()
    {
        Name = "Scanlines";
        MouseFilter = MouseFilterEnum.Ignore;
        Color = Colors.White;
        _shader ??= GD.Load<Shader>("res://ui/panel_scanlines.gdshader");
        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("period", Mathf.Max(2f, HudTheme.S(3)));
        Material = material;
    }
}

/// <summary>
/// An ASCII meter (`STYLEGUIDE.md` [Sec. 12]): filled cells in the normal colour, then the warning
/// colour, then dim empty cells. Cell counts come from <see cref="HudMeter"/>.
/// </summary>
public partial class MeterBar : HBoxContainer
{
    private readonly Label _normal = new(), _warn = new(), _off = new();

    public MeterBar(Color normalColor, Color? warnColor = null)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 0);
        foreach (var label in new[] { _normal, _warn, _off })
        {
            label.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(label);
        }
        HudTheme.Label(_normal, normalColor, HudTheme.SmallUnits);
        HudTheme.Label(_warn, warnColor ?? HudTheme.Yellow, HudTheme.SmallUnits);
        HudTheme.Label(_off, HudTheme.LineHi, HudTheme.SmallUnits);
    }

    public void Set(HudMeter.Bar bar)
    {
        _normal.Text = new string('█', bar.Normal);
        _warn.Text = new string('█', bar.Warn);
        _off.Text = new string('░', bar.Off);
        _normal.Visible = bar.Normal > 0;
        _warn.Visible = bar.Warn > 0;
        _off.Visible = bar.Off > 0;
    }

    /// <summary>Re-applies sizes after the HUD scale changed.</summary>
    public void Restyle()
    {
        foreach (var label in new[] { _normal, _warn, _off })
            label.AddThemeFontSizeOverride("font_size", HudTheme.S(HudTheme.SmallUnits));
    }
}

/// <summary>
/// The frame around the `GameViewport` rectangle: a hairline border and four cyan corner ticks,
/// drawn outside the rendered rectangle so picking and `PixelView` are not affected.
/// </summary>
public partial class ViewportFrame : Control
{
    public ViewportFrame()
    {
        Name = "ViewportFrame";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, HudTheme.LineHi, false, Mathf.Max(1f, HudTheme.S(2)));
        var tick = HudTheme.S(28);
        var thick = Mathf.Max(2f, HudTheme.S(4));
        var color = HudTheme.Cyan;
        var a = rect.Position;
        var b = rect.End;
        DrawLine(a, a + new Vector2(tick, 0), color, thick);
        DrawLine(a, a + new Vector2(0, tick), color, thick);
        DrawLine(new Vector2(b.X, a.Y), new Vector2(b.X - tick, a.Y), color, thick);
        DrawLine(new Vector2(b.X, a.Y), new Vector2(b.X, a.Y + tick), color, thick);
        DrawLine(new Vector2(a.X, b.Y), new Vector2(a.X + tick, b.Y), color, thick);
        DrawLine(new Vector2(a.X, b.Y), new Vector2(a.X, b.Y - tick), color, thick);
        DrawLine(b, b - new Vector2(tick, 0), color, thick);
        DrawLine(b, b - new Vector2(0, tick), color, thick);
    }
}
