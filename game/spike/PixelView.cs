using System;
using Godot;

namespace EtherBound.Game.Spike;

/// <summary>
/// The low-res world buffer: an orthographic camera at yaw 45 degrees and pitch 30 degrees renders
/// into a SubViewport where 1 px is one art pixel, shown at x1/x2/x4 with nearest filtering. The
/// camera snaps to the art-pixel grid and the sub-pixel remainder offsets the scaled image, so
/// scrolling is smooth while every texel stays locked to one art pixel.
/// </summary>
public sealed partial class PixelView : Node
{
    // A 1 m tile is 64 px wide: the x axis projects to cos(45) of a unit on screen, so 32 / cos(45).
    public static readonly float PixelsPerUnit = 32f / Mathf.Sqrt(0.5f);
    // Pitch 30 degrees gives an exact 2:1 diamond; scaling height by sqrt(2/3) makes 0.5 m = 16 px.
    public static readonly float VerticalScale = Mathf.Sqrt(2f / 3f);
    private const int Margin = 2;
    private const float Distance = 200f;

    public readonly SubViewport Viewport = new();
    public readonly Camera3D Camera = new();
    private readonly TextureRect _display = new();
    private readonly ColorRect _backdrop = new();
    private readonly CanvasLayer _layer = new();
    private readonly Vector3 _right, _up, _back;
    private Rect2 _target;
    private Vector2 _origin;
    public int Scale { get; private set; } = 2;
    public Vector2 Remainder { get; private set; }

    public PixelView()
    {
        var a = Mathf.Cos(Mathf.DegToRad(30f)) * Mathf.Sqrt(0.5f);
        var b = Mathf.Sin(Mathf.DegToRad(30f));
        var r = Mathf.Sqrt(0.5f);
        _right = new Vector3(r, 0, -r);
        _back = new Vector3(a, b, a);
        _up = _back.Cross(_right).Normalized();
    }

    public override void _Ready()
    {
        Viewport.Msaa3D = Godot.Viewport.Msaa.Disabled;
        Viewport.ScreenSpaceAA = Godot.Viewport.ScreenSpaceAAEnum.Disabled;
        Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        Viewport.CanvasItemDefaultTextureFilter = Godot.Viewport.DefaultCanvasItemTextureFilter.Nearest;
        AddChild(Viewport);
        Camera.Projection = Camera3D.ProjectionType.Orthogonal;
        Camera.Basis = new Basis(_right, _up, _back);
        Camera.Near = 1f;
        Camera.Far = Distance * 2;
        Viewport.AddChild(Camera);
        _display.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        _display.Texture = Viewport.GetTexture();
        _display.StretchMode = TextureRect.StretchModeEnum.Scale;
        // The page behind the world, drawn first in this canvas so it covers the window clear colour.
        _backdrop.MouseFilter = Control.MouseFilterEnum.Ignore;
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_backdrop);
        _layer.AddChild(_display);
        AddChild(_layer);
        GetViewport().SizeChanged += Resize;
        Resize();
    }

    public void SetScale(int scale)
    {
        Scale = scale;
        Resize();
    }

    /// <summary>Colour of the page behind the world, visible in the HUD's margins and gutters.</summary>
    public void SetBackdrop(Color color) => _backdrop.Color = color;

    /// <summary>
    /// Renders into a window-pixel rectangle (the HUD's `GameViewport`) instead of the whole window.
    /// An empty rectangle means the window, which is the behaviour without a HUD.
    /// </summary>
    public void SetTargetRect(Rect2 rect)
    {
        _target = rect;
        Resize();
    }

    private void Resize()
    {
        var window = GetViewport().GetVisibleRect().Size;
        var rect = _target.Size.X >= 1 && _target.Size.Y >= 1 ? _target : new Rect2(Vector2.Zero, window);
        _origin = rect.Position;
        // Even sizes keep the camera centre on a pixel corner, where tile corners project.
        var w = (int)Math.Ceiling(rect.Size.X / Scale / 2) * 2 + Margin * 2;
        var h = (int)Math.Ceiling(rect.Size.Y / Scale / 2) * 2 + Margin * 2;
        Viewport.Size = new Vector2I(w, h);
        Camera.Size = h / PixelsPerUnit;
        _display.Size = new Vector2(w, h) * Scale;
        _display.Position = _origin;
    }

    /// <summary>Centres the camera on a point in render space (already vertically scaled).</summary>
    public void Follow(Vector3 target)
    {
        var pr = target.Dot(_right) * PixelsPerUnit;
        var pu = target.Dot(_up) * PixelsPerUnit;
        var sr = Mathf.Round(pr);
        var su = Mathf.Round(pu);
        Remainder = new Vector2(pr - sr, pu - su);
        Camera.Position = _right * (sr / PixelsPerUnit) + _up * (su / PixelsPerUnit)
            + _back * (target.Dot(_back) + Distance);
        // Whole screen pixels only: at x1 the remainder rounds away, at x4 it moves in quarter art pixels.
        _display.Position = (_origin + new Vector2(-Margin - Remainder.X, -Margin + Remainder.Y) * Scale).Round();
    }

    public (Vector3 Origin, Vector3 Direction) RayFromScreen(Vector2 screenPosition)
    {
        var viewportPosition = (screenPosition - _display.GlobalPosition) / Scale;
        return (Camera.ProjectRayOrigin(viewportPosition), Camera.ProjectRayNormal(viewportPosition));
    }

    public Vector2 ScreenFromWorld(Vector3 worldPosition) =>
        _display.GlobalPosition + Camera.UnprojectPosition(worldPosition) * Scale;

    /// <summary>Art-pixel size of the low-res buffer, for screenshots at x1.</summary>
    public Image CaptureLowRes() => Viewport.GetTexture().GetImage();
}
