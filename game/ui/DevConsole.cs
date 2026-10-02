using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EtherBound.Host;
using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The dev console: a page-shaped surface that opens in the viewport area from `DEBUG`. It reads a
/// `WorldFrame` and never mutates state; `Regenerate world` reuses `GeneratorPanel` and raises the
/// same `NewGameRequest` the rest of the client uses. Sidebar entries with no backend (Events,
/// Clock, Save) are shown disabled rather than faked.
/// </summary>
public partial class DevConsole : PanelContainer
{
    private static readonly (string Key, string Label, bool Enabled)[] Sections =
    {
        ("world", "WORLD", true),
        ("npcs", "NPCS", true),
        ("events", "EVENTS", false),
        ("clock", "CLOCK", false),
        ("save", "SAVE", false),
    };

    private readonly Dictionary<string, Button> _sidebar = new(StringComparer.Ordinal);
    private readonly List<Control> _rows = new();
    private VBoxContainer _table = null!;
    private Control _worldSection = null!, _npcSection = null!;
    private GeneratorPanel _regenerate = null!;
    private Label _seed = null!, _tick = null!, _summary = null!, _empty = null!;
    private string _signature = "";
    private string _section = "world";

    public DevConsole()
    {
        Name = "DevConsole";
        Visible = false;
        MouseFilter = MouseFilterEnum.Pass;
        // The console is the page: as a container it sizes its own body, so the HUD only has to
        // place the console, with no anchor resolution to wait a frame for.
        AddThemeStyleboxOverride("panel", HudTheme.PanelFrame(modal: true));
    }

    /// <summary>`Regenerate world` asks for a new world through the existing request path.</summary>
    public event Action<NewGameRequest>? RegenerateRequested;

    public string Section => _section;

    /// <summary>Selects a sidebar section by key; for the screenshot driver.</summary>
    public void ShowSection(string key) => Select(key);

    public override void _Ready()
    {
        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", HudTheme.S(12));
        page.AddChild(TopBar());
        page.AddChild(Divider());
        var body = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", HudTheme.S(24));
        body.AddChild(Sidebar());
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", HudTheme.S(20));
        _worldSection = WorldSection();
        _npcSection = NpcSection();
        content.AddChild(_worldSection);
        content.AddChild(_npcSection);
        body.AddChild(content);
        page.AddChild(body);
        AddChild(page);
        Select("world");
    }

    public void UpdateFrame(WorldFrame frame)
    {
        if (_seed is null) return;
        _seed.Text = frame.Seed.ToString();
        _tick.Text = $"{frame.GameMinute:000000}  ·  {frame.Generator.ToUpperInvariant()} V{frame.GenVersion}";
        _regenerate.UpdateFrame(frame);
        var signature = Signature(frame);
        if (signature == _signature) return;
        _signature = signature;
        RebuildTable(frame);
    }

    private static string Signature(WorldFrame frame)
    {
        var builder = new StringBuilder();
        foreach (var actor in frame.Actors)
        {
            var (hunger, thirst, rest) = NeedFormat.Cells(actor.Needs);
            builder.Append(actor.Id).Append('|').Append(actor.Name).Append('|').Append(actor.X).Append(',')
                .Append(actor.Y).Append(',').Append(actor.H).Append('|').Append(actor.Activity?.Op).Append('|')
                .Append(hunger).Append(',').Append(thirst).Append(',').Append(rest).Append('\n');
        }
        return builder.ToString();
    }

    private Control TopBar()
    {
        var bar = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bar.AddThemeConstantOverride("separation", HudTheme.S(16));
        var title = new Label { Text = "DEBUG CONSOLE", VerticalAlignment = VerticalAlignment.Center };
        HudTheme.Label(title, HudTheme.Cyan, HudTheme.TitleUnits);
        bar.AddChild(title);
        bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        bar.AddChild(Field("SEED"));
        _seed = new Label { Text = "—", VerticalAlignment = VerticalAlignment.Center };
        HudTheme.Label(_seed, HudTheme.Text, HudTheme.SmallUnits);
        bar.AddChild(_seed);
        bar.AddChild(Field("TICK"));
        _tick = new Label { Text = "—", VerticalAlignment = VerticalAlignment.Center };
        HudTheme.Label(_tick, HudTheme.Dim, HudTheme.SmallUnits);
        bar.AddChild(_tick);
        return bar;
    }

    private Control Sidebar()
    {
        var column = new VBoxContainer { CustomMinimumSize = HudTheme.V(260, 0) };
        column.AddThemeConstantOverride("separation", HudTheme.S(10));
        foreach (var (key, label, enabled) in Sections)
        {
            var button = new Button { Text = label, Disabled = !enabled, Alignment = HorizontalAlignment.Left };
            HudTheme.Tab(button, false);
            if (enabled) button.Pressed += () => Select(key);
            _sidebar[key] = button;
            column.AddChild(button);
        }
        column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        return column;
    }

    private Control WorldSection()
    {
        var (card, body) = Card("REGENERATE WORLD");
        var note = new Label
        {
            Text = "RAISES THE SAME NEW-GAME REQUEST AS THE GAME VIEW · NOTHING HERE WRITES STATE",
            ClipText = true,
        };
        HudTheme.Label(note, HudTheme.Dim, HudTheme.SmallUnits);
        body.AddChild(note);
        _regenerate = new GeneratorPanel(newGameMode: false);
        _regenerate.Confirmed += request => RegenerateRequested?.Invoke(request);
        // A long generator form scrolls inside the card; as a plain child its minimum height would
        // push the console past the viewport and over the footer.
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _regenerate.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_regenerate);
        body.AddChild(scroll);
        return card;
    }

    private Control NpcSection()
    {
        var (card, body) = Card("NPCS");
        _summary = new Label { Text = "—" };
        HudTheme.Label(_summary, HudTheme.Dim, HudTheme.SmallUnits);
        body.AddChild(_summary);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", HudTheme.S(12));
        foreach (var (text, width) in new[] { ("NAME", 380f), ("KIND", 160f), ("TILE", 260f), ("H", 90f), ("HUN", 110f), ("THI", 110f), ("RST", 110f), ("ACTIVITY", 240f) })
        {
            var cell = new Label { Text = text, CustomMinimumSize = HudTheme.V(width, 0) };
            HudTheme.Label(cell, HudTheme.LineHi, HudTheme.SmallUnits);
            head.AddChild(cell);
        }
        body.AddChild(head);
        body.AddChild(Divider());
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _table = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _table.AddThemeConstantOverride("separation", HudTheme.S(4));
        scroll.AddChild(_table);
        body.AddChild(scroll);
        _empty = new Label { Text = "" };
        HudTheme.Label(_empty, HudTheme.Dim, HudTheme.SmallUnits);
        body.AddChild(_empty);
        return card;
    }

    private void RebuildTable(WorldFrame frame)
    {
        foreach (var row in _rows)
        {
            _table.RemoveChild(row);
            row.QueueFree();
        }
        _rows.Clear();
        foreach (var actor in frame.Actors.OrderBy(actor => actor.Kind == "player" ? 0 : 1).ThenBy(actor => actor.Id, StringComparer.Ordinal))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", HudTheme.S(12));
            var (hunger, thirst, rest) = NeedFormat.Cells(actor.Needs);
            foreach (var (text, width) in new[]
            {
                (actor.Name ?? actor.Id, 380f),
                (actor.Kind.ToUpperInvariant(), 160f),
                ($"{actor.X:0.0}, {actor.Y:0.0}", 260f),
                (actor.H.ToString(), 90f),
                (hunger, 110f),
                (thirst, 110f),
                (rest, 110f),
                (actor.Activity is { } activity ? $"{activity.Op.ToUpperInvariant()} {activity.EndsMinute - activity.StartedMinute} MIN" : "—", 240f),
            })
            {
                var cell = new Label { Text = text, CustomMinimumSize = HudTheme.V(width, 0) };
                HudTheme.Label(cell, actor.Kind == "player" ? HudTheme.Cyan : HudTheme.Text, HudTheme.SmallUnits);
                row.AddChild(cell);
            }
            _table.AddChild(row);
            _rows.Add(row);
        }
        var extras = frame.Actors.Count(actor => actor.Kind != "player");
        _summary.Text = $"TICK {frame.GameMinute}  ·  {extras} EXTRAS  ·  SEED {frame.Seed}";
        _empty.Text = extras == 0 ? "NO EXTRAS IN THIS WORLD" : "";
    }

    private void Select(string key)
    {
        if (Sections.All(section => section.Key != key || !section.Enabled)) return;
        _section = key;
        _worldSection.Visible = key == "world";
        _npcSection.Visible = key == "npcs";
        foreach (var (section, button) in _sidebar) HudTheme.Tab(button, section == key);
    }

    /// <summary>A page card: the panel frame plus the body container its content belongs in. The body
    /// is returned separately because a `PanelContainer` lays out a single child.</summary>
    private static (PanelContainer Card, VBoxContainer Body) Card(string title)
    {
        var card = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Pass,
        };
        card.AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Panel, HudTheme.LineHi, 0.9f, HudTheme.S(2), HudTheme.S(16), HudTheme.S(14)));
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", HudTheme.S(12));
        body.AddChild(HudTheme.Heading(title));
        body.AddChild(new ColorRect
        {
            Color = HudTheme.Line,
            CustomMinimumSize = HudTheme.V(0, 2),
            MouseFilter = MouseFilterEnum.Ignore,
        });
        card.AddChild(body);
        return (card, body);
    }

    private static Label Field(string text)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        HudTheme.Label(label, HudTheme.LineHi, HudTheme.SmallUnits);
        return label;
    }

    private static ColorRect Divider() => new()
    {
        Color = HudTheme.Line,
        CustomMinimumSize = HudTheme.V(0, 2),
        MouseFilter = MouseFilterEnum.Ignore,
    };
}
