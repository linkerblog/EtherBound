using System;
using System.Collections.Generic;
using System.Linq;
using EtherBound.Host;
using EtherBound.Sim.Core;
using Godot;

namespace EtherBound.Game.Ui;

public partial class ActionMenuOverlay : Control
{
    private sealed record VerbGroup(string Op, string Label, List<HostMenuEntry> Entries);

    private readonly List<Button> _buttons = new();
    private HostMenuPayload? _menu;
    private List<VerbGroup> _groups = new();
    private Vector2 _center;
    private bool _radial;
    private int _level;
    private int _groupIndex;
    private int _focusIndex;

    public ActionMenuOverlay()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;
    }

    public event Action<GameAction>? ActionSelected;
    public event Action? Closed;

    public void ShowContext(HostMenuPayload menu, Vector2 position)
    {
        _menu = menu;
        _radial = false;
        _level = 0;
        Visible = true;
        ClearContent();
        var width = 280f;
        var height = Math.Min(520f, 42f + menu.Entries.Length * 32f);
        var clamped = new Vector2(Mathf.Clamp(position.X, 8, Math.Max(8, Size.X - width - 8)),
            Mathf.Clamp(position.Y, 8, Math.Max(8, Size.Y - height - 8)));
        var panel = PanelAt(clamped, new Vector2(width, height));
        var target = new Label { Text = menu.Target.ToUpperInvariant(), CustomMinimumSize = new Vector2(0, 28) };
        LabelStyle(target, new Color("#57c7ff"));
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        content.AddChild(target);
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, height - 60),
        };
        var entries = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(entries);
        content.AddChild(scroll);
        if (menu.Entries.Length == 0)
        {
            var empty = new Label { Text = "NO ACTIONS" };
            LabelStyle(empty, new Color("#6a6d78"));
            entries.AddChild(empty);
        }
        foreach (var entry in menu.Entries) AddEntryButton(entries, entry, ContextText(entry));
        panel.AddChild(content);
    }

    public void ShowRadial(HostMenuPayload menu, Vector2 center)
    {
        _menu = menu;
        _radial = true;
        _center = center;
        _level = 1;
        _groups = GroupEntries(menu.Entries);
        _groupIndex = 0;
        Visible = true;
        ShowVerbs();
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        ClearContent();
        _menu = null;
        Closed?.Invoke();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape)
        {
            if (_radial && _level == 2) ShowVerbs();
            else Close();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!_radial || _buttons.Count == 0) return;
        if (key.Keycode is Key.Left or Key.Up)
        {
            Focus((_focusIndex + _buttons.Count - 1) % _buttons.Count);
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode is Key.Right or Key.Down)
        {
            Focus((_focusIndex + 1) % _buttons.Count);
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode is Key.Enter or Key.Space)
        {
            _buttons[_focusIndex].EmitSignal(BaseButton.SignalName.Pressed);
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode is >= Key.Key1 and <= Key.Key9)
        {
            var index = (int)key.Keycode - (int)Key.Key1;
            if (index < _buttons.Count) Focus(index);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        if (!_radial || _level != 2 || _buttons.Count == 0) return;
        foreach (var button in _buttons)
            DrawLine(_center, button.Position + button.Size * 0.5f, new Color("#3a3d47"), 1f, true);
    }

    private void ShowVerbs()
    {
        _level = 1;
        ClearContent();
        var hub = PanelAt(_center - new Vector2(86, 36), new Vector2(172, 72));
        var title = new Label { Text = _menu?.Target.ToUpperInvariant() ?? "NIKO", HorizontalAlignment = HorizontalAlignment.Center };
        LabelStyle(title, new Color("#57c7ff"));
        hub.AddChild(title);

        _buttons.Clear();
        if (_groups.Count == 0)
        {
            AddRadialButton("NO ACTIONS", _center + new Vector2(-56, 60), false, "");
            return;
        }
        var radius = Math.Max(150f, _groups.Count * 13f);
        for (var i = 0; i < _groups.Count; i++)
        {
            var group = _groups[i];
            var angle = -MathF.PI * 0.5f + MathF.Tau * i / _groups.Count;
            var point = _center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var available = group.Entries.Any(entry => entry.Available);
            var text = group.Entries.Count == 1 ? VerbText(group.Entries[0]) : group.Label.ToUpperInvariant();
            var button = AddRadialButton(text, point, available, group.Entries.FirstOrDefault(e => !e.Available)?.Reason ?? "");
            var captured = i;
            button.Pressed += () => SelectVerb(captured);
        }
        Focus(Math.Clamp(_groupIndex, 0, _buttons.Count - 1));
    }

    private void SelectVerb(int index)
    {
        if (index < 0 || index >= _groups.Count) return;
        _groupIndex = index;
        var group = _groups[index];
        if (group.Entries.Count == 1)
        {
            SelectAction(group.Entries[0]);
            return;
        }
        _level = 2;
        ClearContent();
        var hub = PanelAt(_center - new Vector2(86, 38), new Vector2(172, 76));
        var back = new Button { Text = $"< {group.Label.ToUpperInvariant()}" };
        ButtonStyle(back, new Color("#57c7ff"), false);
        back.Pressed += ShowVerbs;
        hub.AddChild(back);
        var focus = new Label { Text = _menu?.Target.ToUpperInvariant() ?? "NIKO", HorizontalAlignment = HorizontalAlignment.Center };
        LabelStyle(focus, new Color("#6a6d78"));
        hub.AddChild(focus);

        _buttons.Clear();
        var positions = new Dictionary<(int Dx, int Dy), int>();
        foreach (var entry in group.Entries)
        {
            var offset = (entry.TileDx, entry.TileDy);
            positions.TryGetValue(offset, out var sameTile);
            positions[offset] = sameTile + 1;
            var direction = new Vector2(entry.TileDx + entry.TileDy, entry.TileDy - entry.TileDx).Normalized();
            if (direction == Vector2.Zero)
            {
                var angle = -MathF.PI * 0.5f + MathF.Tau * sameTile / Math.Max(1, group.Entries.Count);
                direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }
            var point = _center + direction * (145 + sameTile * 38);
            var subject = entry.Subject ?? (entry.TileDx == 0 && entry.TileDy == 0 ? _menu?.Target : "tile");
            var label = subject is null ? entry.Label : $"{entry.Label.ToUpperInvariant()} {subject}";
            var button = AddRadialButton(label, point, entry.Available, entry.Reason ?? "");
            button.Pressed += () => SelectAction(entry);
        }
        Focus(0);
        QueueRedraw();
    }

    private void SelectAction(HostMenuEntry entry)
    {
        if (!entry.Available) return;
        var action = entry.Action;
        Close();
        ActionSelected?.Invoke(action);
    }

    private Button AddRadialButton(string text, Vector2 center, bool available, string reason)
    {
        var button = new Button
        {
            Text = text,
            Position = center - new Vector2(68, 17),
            Size = new Vector2(136, 34),
            CustomMinimumSize = new Vector2(136, 34),
            TooltipText = available ? "" : reason,
            Disabled = !available,
            MouseFilter = MouseFilterEnum.Stop,
        };
        ButtonStyle(button, available ? new Color("#d6d8de") : new Color("#3a3d47"), true);
        AddChild(button);
        _buttons.Add(button);
        return button;
    }

    private void AddEntryButton(VBoxContainer column, HostMenuEntry entry, string text)
    {
        var button = new Button
        {
            Text = entry.Available || string.IsNullOrEmpty(entry.Reason) ? text : $"{text}  ·  {entry.Reason}",
            Disabled = !entry.Available,
            CustomMinimumSize = new Vector2(0, 30),
            TooltipText = entry.Reason ?? "",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var tone = entry.Tags.Contains("illegal", StringComparer.Ordinal) ? new Color("#ff5c57")
            : entry.Tags.Contains("ether", StringComparer.Ordinal) ? new Color("#ff6ac1") : new Color("#d6d8de");
        ButtonStyle(button, tone, false);
        button.Pressed += () => SelectAction(entry);
        column.AddChild(button);
        _buttons.Add(button);
    }

    private static string ContextText(HostMenuEntry entry) => entry.Subject is null
        ? entry.Label
        : $"{entry.Label}  {entry.Subject}";

    private static string VerbText(HostMenuEntry entry) => entry.Subject is null
        ? entry.Label.ToUpperInvariant()
        : $"{entry.Label.ToUpperInvariant()} / {entry.Subject.ToUpperInvariant()}";

    private static List<VerbGroup> GroupEntries(IEnumerable<HostMenuEntry> entries)
    {
        var groups = new List<VerbGroup>();
        foreach (var entry in entries)
        {
            var group = groups.FirstOrDefault(g => g.Op == entry.Op);
            if (group is null) groups.Add(new VerbGroup(entry.Op, entry.Label, new List<HostMenuEntry> { entry }));
            else group.Entries.Add(entry);
        }
        return groups;
    }

    private PanelContainer PanelAt(Vector2 position, Vector2 size)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color("#0c0d10"),
            BorderColor = new Color("#3a3d47"),
            BorderWidthLeft = 2,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 10,
            ContentMarginTop = 8,
            ContentMarginRight = 10,
            ContentMarginBottom = 8,
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 0,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
        };
        var panel = new PanelContainer
        {
            Position = position,
            Size = size,
            CustomMinimumSize = size,
            MouseFilter = MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);
        return panel;
    }

    private static void ButtonStyle(Button button, Color color, bool compact)
    {
        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_disabled_color", new Color("#6a6d78"));
        button.AddThemeColorOverride("font_hover_color", new Color("#07080a"));
        button.AddThemeStyleboxOverride("normal", ButtonBox("#0c0d10", "#3a3d47"));
        button.AddThemeStyleboxOverride("hover", ButtonBox("#57c7ff", "#57c7ff"));
        button.AddThemeStyleboxOverride("disabled", ButtonBox("#0c0d10", "#22242b"));
        button.AddThemeFontSizeOverride("font_size", compact ? 10 : 11);
    }

    private static StyleBoxFlat ButtonBox(string background, string border) => new()
    {
        BgColor = new Color(background),
        BorderColor = new Color(border),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusBottomLeft = 0,
        CornerRadiusBottomRight = 0,
        CornerRadiusTopLeft = 0,
        CornerRadiusTopRight = 0,
        ContentMarginLeft = 7,
        ContentMarginRight = 7,
        ContentMarginTop = 4,
        ContentMarginBottom = 4,
    };

    private static void LabelStyle(Label label, Color color)
    {
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 11);
    }

    private void Focus(int index)
    {
        if (_buttons.Count == 0) return;
        _focusIndex = Math.Clamp(index, 0, _buttons.Count - 1);
        _buttons[_focusIndex].GrabFocus();
    }

    private void ClearContent()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _buttons.Clear();
    }
}
