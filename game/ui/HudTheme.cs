using System;
using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The single source of colour, font and stylebox values for `game/ui/`. Every panel asks this class
/// for its colours, so no hex literal is left in `GameHud`, `BuildPanel`, `DevConsole`,
/// `GeneratorPanel` or `ActionMenuOverlay`. Colours are the tokens documented in
/// `docs/utils/STYLEGUIDE.md` [Sec. 3] (the `EtherBound/Colors` collection); sizes are in design
/// units of the 2560x1440 Figma frame and multiplied by <see cref="Scale"/>.
/// </summary>
public static class HudTheme
{
    // EtherBound/Colors (STYLEGUIDE.md [Sec. 3]).
    public static readonly Color Bg = new("#07080a");
    public static readonly Color Panel = new("#0c0d10");
    public static readonly Color Bar = new("#121318");
    public static readonly Color Line = new("#22242b");
    public static readonly Color LineHi = new("#3a3d47");
    public static readonly Color Text = new("#d6d8de");
    public static readonly Color Dim = new("#6a6d78");
    public static readonly Color Green = new("#5af78e");
    public static readonly Color Cyan = new("#57c7ff");
    public static readonly Color Magenta = new("#ff6ac1");
    public static readonly Color Yellow = new("#f3f99d");
    public static readonly Color Red = new("#ff5c57");

    // Derived surfaces: the same tokens at the alpha the HUD panels use over the world.
    public static readonly Color PanelOverWorld = new(Panel.R, Panel.G, Panel.B, 0.88f);
    public static readonly Color ModalOverWorld = new(Panel.R, Panel.G, Panel.B, 0.97f);
    public static readonly Color BarOverWorld = new(Bar.R, Bar.G, Bar.B, 0.94f);
    public static readonly Color Shadow = new(0, 0, 0, 0.6f);

    // Font sizes in design units. The body size is the one the plan calls 24 units (12 px at 720p).
    public const float BodyUnits = 24f;
    public const float SmallUnits = 20f;
    public const float TinyUnits = 18f;
    public const float TitleUnits = 28f;
    public const float ClockUnits = 30f;
    public const float BrandUnits = 32f;

    private static float _scale = 1f;

    /// <summary>
    /// Whether HUD motion (the feed's `glitch-in`) runs. Off for screenshots and for
    /// `--reduce-motion`, so a capture or a motion-sensitive player sees a still HUD.
    /// </summary>
    public static bool Motion { get; set; } = true;

    /// <summary>The interface face: JetBrains Mono, with Inter behind it for glyphs it lacks.</summary>
    public static Font LoadBodyFont()
    {
        var inter = GD.Load<FontFile>("res://assets/fonts/Inter-VariableFont_opsz_wght.ttf")
            ?? throw new InvalidOperationException("The HUD fallback font could not be loaded.");
        var mono = GD.Load<FontFile>("res://assets/fonts/JetBrainsMono-VariableFont_wght.ttf");
        if (mono is null) return inter;
        mono.Fallbacks = new Godot.Collections.Array<Font> { inter };
        return mono;
    }

    /// <summary>The same face at weight 700, for the clock and the brand.</summary>
    public static Font LoadBoldFont(Font body)
    {
        var tag = TextServerManager.GetPrimaryInterface().NameToTag("wght");
        return new FontVariation
        {
            BaseFont = body,
            VariationOpentype = new Godot.Collections.Dictionary { { tag, 700 } },
        };
    }

    /// <summary>Design units to screen pixels: <c>window height / 1440</c>.</summary>
    public static float Scale => _scale;

    public static void SetScale(float scale) => _scale = scale > 0 ? scale : 1f;

    /// <summary>A design-unit size in screen pixels, rounded so 1 px borders stay crisp.</summary>
    public static int S(float units) => (int)MathF.Round(units * _scale);

    /// <summary>A design-unit point or size in screen pixels.</summary>
    public static Vector2 V(float x, float y) => new(x * _scale, y * _scale);

    public static Vector2 V(Vector2 units) => units * _scale;

    public static Rect2 R(float x, float y, float w, float h) => new(x * _scale, y * _scale, w * _scale, h * _scale);

    public static Theme CreateTheme(Font font)
    {
        var theme = new Theme { DefaultFont = font, DefaultFontSize = S(BodyUnits) };
        ApplyScale(theme);
        return theme;
    }

    /// <summary>Re-applies the scale-dependent theme values after the HUD scale changed.</summary>
    public static void ApplyScale(Theme theme)
    {
        theme.DefaultFontSize = S(BodyUnits);
        // The tooltip that explains an unavailable action: the HUD's own panel, not the engine default.
        theme.SetStylebox("panel", "TooltipPanel", Box(Panel, LineHi, 1f, S(1), S(12), S(8)));
        theme.SetColor("font_color", "TooltipLabel", Text);
        theme.SetFontSize("font_size", "TooltipLabel", S(SmallUnits));
    }

    public static void Label(Label label, Color color, float units = BodyUnits)
    {
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", S(units));
    }

    /// <summary>A real decision: bracket button with an inverted hover (STYLEGUIDE.md [Sec. 7]).</summary>
    public static void Button(Button button, Color color, bool selected = false, Color? accent = null)
    {
        var edge = accent ?? (selected ? Cyan : LineHi);
        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_hover_color", Bg);
        button.AddThemeColorOverride("font_pressed_color", Bg);
        button.AddThemeColorOverride("font_disabled_color", LineHi);
        button.AddThemeStyleboxOverride("normal", Box(PanelOverWorld, edge, 0.94f, S(2), S(12), S(8)));
        button.AddThemeStyleboxOverride("hover", Box(Text, Text, 1f, S(2), S(12), S(8)));
        button.AddThemeStyleboxOverride("pressed", Box(color, color, 1f, S(2), S(12), S(8)));
        button.AddThemeStyleboxOverride("focus", Box(PanelOverWorld, Cyan, 1f, S(2), S(12), S(8)));
        button.AddThemeStyleboxOverride("disabled", Box(Panel, Line, 0.55f, S(2), S(12), S(8)));
        button.AddThemeFontSizeOverride("font_size", S(BodyUnits));
    }

    /// <summary>A toolbar control: quiet border, no bracket, subtle hover (STYLEGUIDE.md [Sec. 7]).</summary>
    public static void MiniButton(Button button, Color color, bool selected = false)
    {
        var edge = selected ? color : LineHi;
        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_hover_color", Text);
        button.AddThemeColorOverride("font_pressed_color", Bg);
        button.AddThemeColorOverride("font_disabled_color", LineHi);
        button.AddThemeStyleboxOverride("normal", Box(Panel, edge, selected ? 1f : 0.92f, S(1), S(7), S(2)));
        button.AddThemeStyleboxOverride("hover", Box(Bar, LineHi, 1f, S(1), S(7), S(2)));
        button.AddThemeStyleboxOverride("pressed", Box(color, color, 1f, S(1), S(7), S(2)));
        button.AddThemeStyleboxOverride("focus", Box(Panel, Cyan, 1f, S(1), S(7), S(2)));
        button.AddThemeStyleboxOverride("disabled", Box(Panel, Line, 0.72f, S(1), S(7), S(2)));
        button.AddThemeFontSizeOverride("font_size", S(TinyUnits));
    }

    /// <summary>One cell of the segmented speed control: equal-width, the selected one lit.</summary>
    public static void Segment(Button button, Color color, bool selected)
    {
        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_hover_color", Text);
        button.AddThemeColorOverride("font_pressed_color", Bg);
        button.AddThemeColorOverride("font_disabled_color", LineHi);
        button.AddThemeStyleboxOverride("normal", Box(selected ? Bar : Panel, selected ? color : LineHi, 1f, S(selected ? 2 : 1), S(8), S(4)));
        button.AddThemeStyleboxOverride("hover", Box(Bar, selected ? color : Dim, 1f, S(selected ? 2 : 1), S(8), S(4)));
        button.AddThemeStyleboxOverride("pressed", Box(color, color, 1f, S(1), S(8), S(4)));
        button.AddThemeStyleboxOverride("focus", Box(Panel, Cyan, 1f, S(2), S(8), S(4)));
        button.AddThemeStyleboxOverride("disabled", Box(Panel, Line, 0.72f, S(1), S(8), S(4)));
        button.AddThemeFontSizeOverride("font_size", S(SmallUnits));
    }

    /// <summary>A hotkey chip: a dim, bordered label such as `ALT+2` or `ENTER`.</summary>
    public static PanelContainer Chip(string text, Color? color = null)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", Box(Bar, Line, 1f, S(1), S(7), S(1)));
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        Label(label, color ?? Dim, TinyUnits);
        chip.AddChild(label);
        return chip;
    }

    /// <summary>A build slot: the same states as a mini button, but tall and centred.</summary>
    public static void Slot(Button button, bool selected)
    {
        button.AddThemeColorOverride("font_color", selected ? Bg : Text);
        button.AddThemeColorOverride("font_hover_color", Bg);
        button.AddThemeColorOverride("font_pressed_color", Bg);
        button.AddThemeColorOverride("font_disabled_color", LineHi);
        button.AddThemeStyleboxOverride("normal", Box(selected ? Cyan : PanelOverWorld, selected ? Cyan : LineHi,
            1f, S(2), S(12), S(6)));
        button.AddThemeStyleboxOverride("hover", Box(Text, Text, 1f, S(2), S(12), S(6)));
        button.AddThemeStyleboxOverride("pressed", Box(Cyan, Cyan, 1f, S(2), S(12), S(6)));
        button.AddThemeStyleboxOverride("focus", Box(PanelOverWorld, Cyan, 1f, S(2), S(12), S(6)));
        button.AddThemeStyleboxOverride("disabled", Box(Panel, Line, 0.55f, S(2), S(12), S(6)));
        button.AddThemeFontSizeOverride("font_size", S(SmallUnits));
    }

    /// <summary>A folder tab (STYLEGUIDE.md [Sec. 14]): the active one is cyan on the panel colour.</summary>
    public static void Tab(Button button, bool active)
    {
        button.AddThemeColorOverride("font_color", active ? Cyan : Dim);
        button.AddThemeColorOverride("font_hover_color", Text);
        button.AddThemeColorOverride("font_pressed_color", Bg);
        button.AddThemeColorOverride("font_disabled_color", LineHi);
        button.AddThemeStyleboxOverride("normal", Box(active ? PanelOverWorld : Bar, active ? LineHi : Line, 1f, S(2), S(10), S(6)));
        button.AddThemeStyleboxOverride("hover", Box(Bar, LineHi, 1f, S(2), S(10), S(6)));
        button.AddThemeStyleboxOverride("pressed", Box(Cyan, Cyan, 1f, S(2), S(10), S(6)));
        button.AddThemeStyleboxOverride("focus", Box(Panel, Cyan, 1f, S(2), S(10), S(6)));
        button.AddThemeStyleboxOverride("disabled", Box(Panel, Line, 0.55f, S(2), S(10), S(6)));
        button.AddThemeFontSizeOverride("font_size", S(SmallUnits));
    }

    public static void Input(LineEdit input)
    {
        input.AddThemeColorOverride("font_color", Text);
        input.AddThemeColorOverride("font_placeholder_color", Dim);
        input.AddThemeColorOverride("caret_color", Green);
        input.AddThemeColorOverride("selection_color", new Color(Cyan.R, Cyan.G, Cyan.B, 0.25f));
        input.AddThemeStyleboxOverride("normal", Box(Panel, Line, 1f, S(1), S(10), S(4)));
        input.AddThemeStyleboxOverride("focus", Box(Panel, Green, 1f, S(1), S(10), S(4)));
        input.AddThemeFontSizeOverride("font_size", S(BodyUnits));
    }

    public static void Field(Control control)
    {
        control.AddThemeColorOverride("font_color", Text);
        control.AddThemeColorOverride("font_hover_color", Text);
        control.AddThemeColorOverride("font_disabled_color", Dim);
        control.AddThemeStyleboxOverride("normal", Box(Panel, Line, 1f, S(1), S(10), S(4)));
        control.AddThemeStyleboxOverride("hover", Box(Bar, LineHi, 1f, S(1), S(10), S(4)));
        control.AddThemeStyleboxOverride("focus", Box(Panel, Cyan, 1f, S(1), S(10), S(4)));
        control.AddThemeStyleboxOverride("disabled", Box(Panel, Line, 0.72f, S(1), S(10), S(4)));
        control.AddThemeFontSizeOverride("font_size", S(BodyUnits));
    }

    /// <summary>A translucent HUD panel: square corners, a left accent, a title strip.</summary>
    public static StyleBoxFlat PanelFrame(bool modal = false, float padY = 10, Color? accent = null)
    {
        var frame = Box(modal ? ModalOverWorld : PanelOverWorld, Line, 1f, S(3), S(14), S(padY));
        if (accent is { } rule) frame.BorderColor = rule;
        frame.BorderWidthTop = S(1);
        frame.BorderWidthRight = S(1);
        frame.BorderWidthBottom = S(1);
        return frame;
    }

    /// <summary>The title strip behind a panel title, from <c>--bar</c>.</summary>
    public static StyleBoxFlat TitleStrip() => Box(BarOverWorld, Line, 1f, 0, S(14), S(6));

    /// <summary>The header strip: the panel frame with the tight padding its 64-unit slot needs.</summary>
    public static StyleBoxFlat HeaderFrame() => Box(PanelOverWorld, Line, 0.92f, S(1), S(14), S(4));

    public static StyleBoxFlat Box(string color) => new()
    {
        BgColor = new Color(color),
        CornerRadiusBottomLeft = 0,
        CornerRadiusBottomRight = 0,
        CornerRadiusTopLeft = 0,
        CornerRadiusTopRight = 0,
    };

    public static StyleBoxFlat Box(Color background, Color border, float alpha, int borderWidth, int padX, int padY)
    {
        var fill = new Color(background.R, background.G, background.B, alpha);
        return new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            ContentMarginLeft = padX,
            ContentMarginTop = padY,
            ContentMarginRight = padX,
            ContentMarginBottom = padY,
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 0,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
        };
    }

    /// <summary>A squared-off top/bottom strip: the radius is zero everywhere but the fill differs.</summary>
    public static StyleBoxFlat Flat(Color color) => new()
    {
        BgColor = color,
        CornerRadiusBottomLeft = 0,
        CornerRadiusBottomRight = 0,
        CornerRadiusTopLeft = 0,
        CornerRadiusTopRight = 0,
    };

    /// <summary>The four HUD title heading, a 3-unit cyan rule plus the label.</summary>
    public static Control Heading(string title)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", S(10));
        row.AddChild(new ColorRect
        {
            Color = Cyan,
            CustomMinimumSize = V(4, BodyUnits),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var label = new Label { Text = title, VerticalAlignment = VerticalAlignment.Center };
        Label(label, Dim, TinyUnits);
        row.AddChild(label);
        return row;
    }
}
