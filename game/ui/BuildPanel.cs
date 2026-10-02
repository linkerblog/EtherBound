using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using EtherBound.Host;
using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The build panel, docked at the bottom of the viewport. `WALLS` and `FLOORS` are the two target
/// kinds the `build` op accepts, and every slot is one subject `Menu.Build` returned, so the panel
/// never authors an option and never bypasses the action API: arming a slot only filters the next
/// click, which still submits the sim's own `build` action.
/// </summary>
public partial class BuildPanel : PanelContainer
{
    /// <summary>An armed slot: the target kind the click must land on and the sim's subject text.</summary>
    public readonly record struct Selection(string Kind, string Subject);

    public const string WallKind = "edge";
    public const string FloorKind = "tile";

    private readonly List<HostMenuEntry> _entries = new();
    private readonly List<Selection> _offered = new();
    private readonly Button _walls = new(), _floors = new();
    private readonly HBoxContainer _slots = new();
    private readonly Label _hint = new();
    private string _tab = WallKind;
    private (string Name, string Color)[] _materials = System.Array.Empty<(string, string)>();

    public BuildPanel()
    {
        Name = "BuildPanel";
        Visible = false;
        ClipContents = true;
        AddThemeStyleboxOverride("panel", HudTheme.PanelFrame(modal: true));
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", HudTheme.S(12));
        body.AddChild(HudTheme.Heading("BUILD"));
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", HudTheme.S(8));
        foreach (var (kind, label, button) in new[] { (WallKind, "WALLS", _walls), (FloorKind, "FLOORS", _floors) })
        {
            button.Text = label;
            button.CustomMinimumSize = HudTheme.V(220, 60);
            button.Pressed += () => SelectTab(kind);
            HudTheme.Tab(button, kind == _tab);
            tabs.AddChild(button);
        }
        tabs.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        HudTheme.Label(_hint, HudTheme.Dim, HudTheme.SmallUnits);
        _hint.Text = "PICK A SLOT · ESC CLOSES";
        tabs.AddChild(_hint);
        body.AddChild(tabs);
        body.AddChild(new ColorRect
        {
            Color = HudTheme.Line,
            CustomMinimumSize = HudTheme.V(0, 2),
            MouseFilter = MouseFilterEnum.Ignore,
        });
        _slots.AddThemeConstantOverride("separation", HudTheme.S(16));
        body.AddChild(_slots);
        AddChild(body);
        AddChild(new Scanlines());
    }

    /// <summary>
    /// Re-applies the scale-dependent styling after the HUD rebuilt its frame: the panel frame, the
    /// spacing and the tab sizes are baked at the scale they were created at.
    /// </summary>
    public void Restyle()
    {
        AddThemeStyleboxOverride("panel", HudTheme.PanelFrame(modal: true));
        var body = GetChild<VBoxContainer>(0);
        body.AddThemeConstantOverride("separation", HudTheme.S(12));
        _slots.AddThemeConstantOverride("separation", HudTheme.S(16));
        Rebuild();
    }

    /// <summary>The catalog's names and colours, so a slot can show the material it builds with.</summary>
    public void SetMaterials(IEnumerable<(string Name, string Color)> materials)
    {
        _materials = materials.ToArray();
        if (Visible) Rebuild();
    }

    /// <summary>
    /// Sizes the panel to its content, docked to the bottom of the viewport: the full height when
    /// there are slots to show, a shorter one for the empty state.
    /// </summary>
    public void ApplyHeight()
    {
        var units = _offered.Count > 0 ? HudLayout.PanelHeight : HudLayout.PanelEmptyHeight;
        CustomMinimumSize = HudTheme.V(HudLayout.ViewportWidth, units);
        Size = HudTheme.V(HudLayout.ViewportWidth, units);
        Position = HudTheme.V(0, HudLayout.ViewportHeight - units);
    }

    /// <summary>The armed slot, or null when the next click builds nothing.</summary>
    public Selection? Armed { get; private set; }

    /// <summary>How many slots the current tab offers; for the screenshot driver.</summary>
    public int SlotCount => _offered.Count;

    /// <summary>Selects a tab by target kind; for the screenshot driver.</summary>
    public void ShowTab(string kind) => SelectTab(kind);

    /// <summary>Arms the first offered slot; for the screenshot driver.</summary>
    public bool ArmFirst()
    {
        if (_offered.Count == 0) return false;
        Arm(_offered[0]);
        return true;
    }

    public event Action<Selection>? SlotSelected;

    /// <summary>The panel closed: the slots it shows are stale until they are asked for again.</summary>
    public event Action? SlotsClosed;

    public void Open()
    {
        Visible = true;
        _hint.Text = "PICK A SLOT · ESC CLOSES";
    }

    /// <summary>Closes and disarms; true when it was open.</summary>
    public bool Close()
    {
        if (!Visible) return false;
        Visible = false;
        Armed = null;
        SlotsClosed?.Invoke();
        return true;
    }

    /// <summary>Rebuilds the slots from `Menu.Build`'s own entries, one per subject and kind.</summary>
    public void SetEntries(ImmutableArray<HostMenuEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries.Where(entry => entry.Op == "build" && Kind(entry) is not null));
        if (Armed is { } armed && !_entries.Any(entry => Kind(entry) == armed.Kind && entry.Subject == armed.Subject))
        {
            Armed = null;
            _hint.Text = "SLOT GONE FROM THE MENU";
        }
        Rebuild();
    }

    private static string? Kind(HostMenuEntry entry) => entry.Action.Target?.Kind switch
    {
        WallKind => WallKind,
        FloorKind => FloorKind,
        _ => null,
    };

    private void SelectTab(string kind)
    {
        if (_tab == kind) return;
        _tab = kind;
        Armed = null;
        HudTheme.Tab(_walls, _tab == WallKind);
        HudTheme.Tab(_floors, _tab == FloorKind);
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var child in _slots.GetChildren())
        {
            _slots.RemoveChild(child);
            child.QueueFree();
        }
        // Sizes and fonts are re-applied here, not only in the constructor, because the HUD scale
        // changes with the window and this panel is created before the first layout.
        _walls.CustomMinimumSize = _floors.CustomMinimumSize = HudTheme.V(220, 60);
        HudTheme.Tab(_walls, _tab == WallKind);
        HudTheme.Tab(_floors, _tab == FloorKind);
        HudTheme.Label(_hint, HudTheme.Dim, HudTheme.SmallUnits);
        var slots = _entries
            .Where(entry => Kind(entry) == _tab)
            .GroupBy(entry => entry.Subject ?? entry.Label, System.StringComparer.Ordinal)
            .Select(group => (
                Subject: group.Key,
                Available: group.Any(entry => entry.Available),
                Reason: group.FirstOrDefault(entry => !entry.Available)?.Reason))
            .ToArray();
        _offered.Clear();
        _offered.AddRange(slots.Select(slot => new Selection(_tab, slot.Subject)));
        if (slots.Length == 0)
        {
            var empty = new Label { Text = "NOTHING BUILDABLE HERE" };
            HudTheme.Label(empty, HudTheme.Dim, HudTheme.SmallUnits);
            _slots.AddChild(empty);
            ApplyHeight();
            return;
        }
        foreach (var (subject, available, reason) in slots)
        {
            var title = subject.ToUpperInvariant();
            var button = new Button
            {
                Text = available || string.IsNullOrWhiteSpace(reason) ? title : $"{title}\n{reason.ToUpperInvariant()}",
                Disabled = !available,
                Alignment = HorizontalAlignment.Left,
                ClipText = true,
            };
            button.CustomMinimumSize = HudTheme.V(440, HudLayout.SlotHeight);
            button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            button.TooltipText = available ? $"Arm {subject}" : reason ?? "unavailable";
            HudTheme.Slot(button, Armed is { } armed && armed.Subject == subject);
            if (BuildSwatch.ColorOf(subject, _materials) is { } swatch)
            {
                button.Icon = Swatch(new Color(swatch), available);
                button.IconAlignment = HorizontalAlignment.Left;
                button.AddThemeConstantOverride("h_separation", HudTheme.S(14));
            }
            button.Pressed += () => Arm(new Selection(_tab, subject));
            _slots.AddChild(button);
        }
        ApplyHeight();
    }

    /// <summary>A solid colour square with a 2-unit edge, drawn at the HUD scale.</summary>
    private static ImageTexture Swatch(Color color, bool available)
    {
        var size = Math.Max(8, HudTheme.S(56));
        var edge = Math.Max(1, HudTheme.S(2));
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var fill = available ? color : new Color(color.R, color.G, color.B, 0.35f);
        image.Fill(HudTheme.LineHi);
        image.FillRect(new Rect2I(edge, edge, size - 2 * edge, size - 2 * edge), fill);
        return ImageTexture.CreateFromImage(image);
    }

    private void Arm(Selection selection)
    {
        Armed = Armed?.Subject == selection.Subject && Armed?.Kind == selection.Kind ? null : selection;
        _hint.Text = Armed is null ? "PICK A SLOT · ESC CLOSES"
            : $"{selection.Subject.ToUpperInvariant()} ARMED · CLICK A TILE TO BUILD";
        Rebuild();
        if (Armed is { } armed) SlotSelected?.Invoke(armed);
    }
}
