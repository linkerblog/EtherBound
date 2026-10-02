using System;
using EtherBound.Host;
using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The HUD minimap: the colour cells the sim sent, drawn nearest-neighbour around Niko with his marker
/// and a north tag. It repaints its texture only when the frame carries a new <see cref="HostMinimap"/>
/// instance, and writes nothing back to the sim.
/// </summary>
public partial class MinimapPanel : Control
{
    private ImageTexture? _texture;
    private HostMinimap? _map;
    private Vector2 _player;

    public MinimapPanel()
    {
        Name = "Minimap";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        ClipContents = true;
    }

    public void SetMap(HostMinimap? map, double playerX, double playerY)
    {
        if (!ReferenceEquals(map, _map))
        {
            _map = map;
            if (map is null)
            {
                _texture = null;
            }
            else
            {
                var image = Image.CreateFromData(map.Cells, map.Cells, false, Image.Format.Rgb8, MinimapModel.Rgb(map));
                if (_texture is not null && _texture.GetSize() == new Vector2(map.Cells, map.Cells)) _texture.Update(image);
                else _texture = ImageTexture.CreateFromImage(image);
            }
        }
        _player = new Vector2((float)playerX, (float)playerY);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var side = Mathf.Min(Size.X, Size.Y);
        var square = new Rect2((Size.X - side) * 0.5f, (Size.Y - side) * 0.5f, side, side);
        DrawRect(square, HudTheme.Bg);
        if (_map is not null && _texture is not null)
        {
            DrawTextureRectRegion(_texture, square, MinimapModel.SourceRegion(_map, _player.X, _player.Y));
            var marker = square.Position + MinimapModel.MarkerFraction(_map, _player.X, _player.Y) * side;
            var radius = Mathf.Max(3f, side / 64f);
            DrawCircle(marker, radius + 1.5f, HudTheme.Bg);
            DrawCircle(marker, radius, HudTheme.Cyan);
        }
        DrawRect(square, HudTheme.LineHi, false, 1f);
        var font = GetThemeDefaultFont();
        var size = Mathf.Max(10, GetThemeDefaultFontSize() - 2);
        DrawString(font, square.Position + new Vector2(0, size + 4), "N", HorizontalAlignment.Center, side, size, HudTheme.Red);
    }
}
