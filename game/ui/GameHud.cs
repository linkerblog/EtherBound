using System;
using System.Collections.Generic;
using System.Linq;
using EtherBound.Host;
using EtherBound.Sim.Core;
using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The HUD shell: the 2560x1440 design frame from the Figma `HUD / Base` nodes, laid out in design
/// units and scaled to the window by <see cref="HudTheme.Scale"/>. Left `MainMenu`, header with brand
/// and clock, the `GameViewport` rectangle the 3D view renders into, the status column, and the
/// footer with `BuildButton`, feed and input line. The sim stays the source of truth for every
/// number: this class reads `WorldFrame` and raises events, it never writes state.
/// </summary>
public partial class GameHud : CanvasLayer
{
    private static readonly (string View, string Label, string Hint, bool Enabled)[] MenuEntries =
    {
        ("GAME", "GAME", "ALT+1", true),
        ("INVENTORY", "INVENTORY", "—", false),
        ("DEBUG", "DEBUG MENU", "ALT+2", true),
    };

    private readonly Dictionary<int, Button> _speedButtons = new();
    private readonly Queue<Label> _feedRows = new();
    private readonly Button[] _menuButtons = new Button[MenuEntries.Length];
    private Control _root = null!;
    private PanelContainer _header = null!, _clockPanel = null!, _feedPanel = null!, _inputPanel = null!;
    private PanelContainer _activityPanel = null!, _carryPanel = null!, _statusPanel = null!;
    private Control _menu = null!, _gameView = null!, _debugView = null!, _llmView = null!, _statusColumn = null!, _footer = null!;
    private HBoxContainer _clockRow = null!;
    private Label _clockText = null!, _carryText = null!, _activityText = null!;
    private ProgressBar _activityProgress = null!;
    private VBoxContainer _feedRowsContainer = null!;
    private LineEdit _input = null!;
    private Button _pauseButton = null!, _buildButton = null!;
    private GeneratorPanel _newGame = null!;
    private DevConsole _console = null!;
    private CompassOverlay _compass = null!;
    private WorldFrame? _frame;
    private int _buildRefresh;
    private Rect2 _viewportRect;

    public GameHud()
    {
        Layer = 2;
        ActionMenus = new ActionMenuOverlay();
        ActionMenus.Modulate = new Color(1, 1, 1, 1);
        Build = new BuildPanel();
    }

    public ActionMenuOverlay ActionMenus { get; }

    /// <summary>The build panel docked at the bottom of the viewport (Stage 2).</summary>
    public BuildPanel Build { get; }

    public string ActiveView { get; private set; } = "GAME";
    public bool InputHasFocus => _input is not null && _input.HasFocus();
    public bool NewGameVisible => _newGame is not null && _newGame.IsVisibleInTree();
    public bool BuildVisible => Build.Visible;

    /// <summary>The armed build slot, if any. Opening the panel does not block movement, so this is
    /// deliberately not part of <see cref="CanMoveWorld"/>; only the click that resolves the target
    /// is gated on it.</summary>
    public BuildPanel.Selection? ArmedBuild => Build.Armed;

    public bool CanMoveWorld => ActiveView == "GAME" && !InputHasFocus && !NewGameVisible && !ActionMenus.Visible;

    /// <summary>The `GameViewport` rectangle in window pixels: where `PixelView` renders and where
    /// mouse input reaches the world.</summary>
    public Rect2 ViewportRect => _viewportRect;

    public event Action<int>? SpeedRequested;
    public event Action<bool>? PauseRequested;
    public event Action<NewGameRequest>? NewGameRequested;
    public event Action<string>? FreeTextSubmitted;
    public event Action<string>? ViewChanged;

    /// <summary>The viewport rectangle changed: the client re-targets `PixelView`.</summary>
    public event Action<Rect2>? ViewportRectChanged;

    /// <summary>The panel needs the build slots `Menu.Build` offers; the client asks the host.</summary>
    public event Action? BuildSlotsRequested;

    public override void _Ready()
    {
        var font = GD.Load<FontFile>("res://assets/fonts/Inter-VariableFont_opsz_wght.ttf");
        if (font is null) throw new InvalidOperationException("The HUD font could not be loaded.");
        HudTheme.SetScale(GetViewport().GetVisibleRect().Size.Y / HudLayout.Height);
        var uiTheme = HudTheme.CreateTheme(font);
        ActionMenus.Theme = uiTheme;
        Build.Theme = uiTheme;
        _root = new Control { Name = "HudRoot", Modulate = new Color(1, 1, 1, 1), Theme = uiTheme };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_root);
        BuildHeader();
        BuildMenu();
        BuildViewportAndStatus();
        BuildFooter();
        _newGame = new GeneratorPanel(newGameMode: true) { Visible = false };
        _newGame.Confirmed += request => NewGameRequested?.Invoke(request);
        _root.AddChild(_newGame);
        AddChild(ActionMenus);
        GetViewport().SizeChanged += Layout;
        Layout();
        PushFeed("ALT+1 GAME · ALT+2 DEBUG · ALT+3 LLM", "seen");
    }

    public void UpdateFrame(WorldFrame frame)
    {
        _frame = frame;
        var player = frame.Actors.FirstOrDefault(actor => actor.Id == Ids.Player);
        if (player is null) return;
        var day = frame.GameMinute / 1440 + 1;
        var minute = frame.GameMinute % 1440;
        _clockText.Text = $"DAY {day:00}   {minute / 60:00}:{minute % 60:00}";
        _clockText.AddThemeColorOverride("font_color", frame.Paused ? HudTheme.Yellow : HudTheme.Green);
        _pauseButton.Text = frame.Paused ? "PLAY" : "PAUSE";
        _pauseButton.TooltipText = frame.Paused ? "Resume the world clock" : "Pause the world clock";
        HudTheme.MiniButton(_pauseButton, frame.Paused ? HudTheme.Yellow : HudTheme.Text, frame.Paused);
        foreach (var (speed, button) in _speedButtons)
        {
            var selected = frame.Speed == speed;
            HudTheme.MiniButton(button, selected ? HudTheme.Cyan : HudTheme.Dim, selected);
        }

        var carried = player.Carried.Length == 0
            ? "L  —\nR  —\nBACK  —"
            : string.Join("\n", player.Carried.Select(item => $"{item.Slot.ToUpperInvariant(),-5} {item.Name} ×{item.Quantity}"));
        _carryText.Text = $"{carried}\nLOAD  {player.LoadKg:0.0} kg";
        _carryText.AddThemeColorOverride("font_color", player.LoadKg > 10 ? HudTheme.Yellow : HudTheme.Text);

        var activity = player.Activity;
        _activityPanel.Visible = activity is not null;
        if (activity is not null)
        {
            var duration = Math.Max(1, activity.EndsMinute - activity.StartedMinute);
            var progress = Math.Clamp((double)(frame.GameMinute - activity.StartedMinute) / duration, 0, 1);
            _activityText.Text = $"{activity.Op.ToUpperInvariant()}  {Math.Max(0, activity.EndsMinute - frame.GameMinute)} MIN";
            _activityProgress.Value = progress * 100;
        }

        _newGame.UpdateFrame(frame);
        _console.UpdateFrame(frame);
        // The slots read the world through `Menu.Build`; refresh them while the panel is open so a
        // wall built a moment ago is not offered again.
        if (Build.Visible && ++_buildRefresh >= 20)
        {
            _buildRefresh = 0;
            BuildSlotsRequested?.Invoke();
        }
    }

    public void PushFeed(string text, string category = "info")
    {
        if (string.IsNullOrWhiteSpace(text) || _feedRowsContainer is null) return;
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", category switch
        {
            "warn" or "rumor" => HudTheme.Yellow,
            "fail" or "harm" => HudTheme.Red,
            "act" => HudTheme.LineHi,
            "seen" => HudTheme.Cyan,
            "ether" => HudTheme.Magenta,
            _ => HudTheme.Text,
        });
        label.AddThemeFontSizeOverride("font_size", HudTheme.S(HudTheme.SmallUnits));
        _feedRowsContainer.AddChild(label);
        _feedRows.Enqueue(label);
        // The footer strip is 80 design units tall; three rows plus the heading fit it.
        while (_feedRows.Count > 3)
        {
            var oldest = _feedRows.Dequeue();
            _feedRowsContainer.RemoveChild(oldest);
            oldest.QueueFree();
        }
    }

    public void ShowNewGame()
    {
        _newGame.OpenNew();
        Layout();
    }

    public void CloseNewGame() => _newGame.CloseNew();

    public void ShowGeneratorError(string message) => _newGame.ShowError(message);

    /// <summary>Fills the build panel from the sim's own `build` entries; nothing is authored here.</summary>
    public void ApplyBuildMenu(HostMenuPayload menu) => Build.SetEntries(menu.Entries);

    public bool CloseBuild() => Build.Close();

    /// <summary>Selects a console section; for the screenshot driver.</summary>
    public void ShowConsoleSection(string key) => _console.ShowSection(key);

    public void SwitchView(string view)
    {
        if (view is not ("GAME" or "DEBUG" or "LLM") || view == ActiveView) return;
        ActiveView = view;
        _gameView.Visible = view == "GAME";
        _debugView.Visible = view == "DEBUG";
        _llmView.Visible = view == "LLM";
        ActionMenus.Close();
        CloseNewGame();
        _input.ReleaseFocus();
        ViewChanged?.Invoke(view);
        UpdateMenuStyles();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape && !key.AltPressed)
        {
            // Esc closes what is open, innermost first; it never closes the world.
            if (!CloseBuild() && !NewGameVisible) return;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!key.AltPressed) return;
        switch (key.Keycode)
        {
            case Key.Key1: SwitchView("GAME"); break;
            case Key.Key2: SwitchView("DEBUG"); break;
            case Key.Key3: SwitchView("LLM"); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void BuildHeader()
    {
        _header = new PanelContainer { Name = "Header", MouseFilter = Control.MouseFilterEnum.Pass };
        _header.AddThemeStyleboxOverride("panel", HudTheme.HeaderFrame());
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", HudTheme.S(14));
        var brand = new Label { Text = "ETHERBOUND", VerticalAlignment = VerticalAlignment.Center };
        HudTheme.Label(brand, HudTheme.Text, HudTheme.BrandUnits);
        row.AddChild(brand);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _clockPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _clockPanel.AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Bg, HudTheme.LineHi, 0.9f, HudTheme.S(1), HudTheme.S(10), HudTheme.S(4)));
        _clockRow = new HBoxContainer();
        _clockRow.AddThemeConstantOverride("separation", HudTheme.S(8));
        _clockText = new Label { Text = "DAY 01   00:00", VerticalAlignment = VerticalAlignment.Center };
        HudTheme.Label(_clockText, HudTheme.Green, HudTheme.ClockUnits);
        _clockRow.AddChild(_clockText);
        _pauseButton = AddMiniButton(_clockRow, "PAUSE", () => PauseRequested?.Invoke(!(_frame?.Paused ?? false)));
        foreach (var speed in new[] { 1, 3, 10 })
            _speedButtons[speed] = AddMiniButton(_clockRow, $"x{speed}", () => SpeedRequested?.Invoke(speed));
        AddMiniButton(_clockRow, "NEW", ShowNewGame);
        _clockPanel.AddChild(_clockRow);
        row.AddChild(_clockPanel);
        _header.AddChild(row);
        _root.AddChild(_header);
    }

    private void BuildMenu()
    {
        _menu = new Control { Name = "MainMenu", MouseFilter = Control.MouseFilterEnum.Pass };
        for (var i = 0; i < MenuEntries.Length; i++)
        {
            var (view, label, hint, enabled) = MenuEntries[i];
            var button = new Button
            {
                Text = $"{label}   {hint}",
                Disabled = !enabled,
                Alignment = HorizontalAlignment.Left,
            };
            HudTheme.Button(button, HudTheme.Text);
            if (enabled) button.Pressed += () => SwitchView(view);
            _menuButtons[i] = button;
            _menu.AddChild(button);
        }
        _root.AddChild(_menu);
        UpdateMenuStyles();
    }

    private void BuildViewportAndStatus()
    {
        _gameView = new Control { Name = "GameViewport", MouseFilter = Control.MouseFilterEnum.Ignore };
        _compass = new CompassOverlay();
        _gameView.AddChild(_compass);
        _gameView.AddChild(Build);
        _root.AddChild(_gameView);
        // The console and the LLM placeholder cover the viewport area but not the world: the world
        // renders on its own canvas below the HUD, so these are siblings of the GAME overlay.
        _console = new DevConsole();
        _console.RegenerateRequested += request => NewGameRequested?.Invoke(request);
        _debugView = _console;
        _debugView.Visible = false;
        _root.AddChild(_debugView);
        _llmView = new PanelContainer { Name = "LlmView", Visible = false, MouseFilter = Control.MouseFilterEnum.Pass };
        _llmView.AddThemeStyleboxOverride("panel", HudTheme.PanelFrame(modal: true));
        var placeholder = new Label
        {
            Text = "NO MODEL CONNECTED",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        HudTheme.Label(placeholder, HudTheme.Dim);
        _llmView.AddChild(placeholder);
        _root.AddChild(_llmView);

        _statusColumn = new Control { Name = "StatusColumn", MouseFilter = Control.MouseFilterEnum.Pass };
        var stack = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", HudTheme.S(24));
        _activityPanel = CreatePanel(stack, "ACT");
        _activityText = new Label { Text = "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        HudTheme.Label(_activityText, HudTheme.Text, HudTheme.SmallUnits);
        Content(_activityPanel).AddChild(_activityText);
        _activityProgress = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, ShowPercentage = false };
        _activityProgress.AddThemeStyleboxOverride("background", HudTheme.Flat(HudTheme.Bar));
        _activityProgress.AddThemeStyleboxOverride("fill", HudTheme.Flat(HudTheme.Cyan));
        Content(_activityPanel).AddChild(_activityProgress);
        _activityPanel.Visible = false;

        _carryPanel = CreatePanel(stack, "CARRY");
        _carryText = new Label { Text = "L  —\nR  —\nBACK  —\nLOAD  0.0 kg" };
        HudTheme.Label(_carryText, HudTheme.Text, HudTheme.SmallUnits);
        Content(_carryPanel).AddChild(_carryText);

        // D4: the design's StatBar components (HP, EP) draw nothing until the sim exposes vitals of
        // its own; a placeholder would show invented numbers, so the panel stays hidden.
        _statusPanel = CreatePanel(stack, "STATUS");
        _statusPanel.Visible = false;
        _statusColumn.AddChild(stack);
        _root.AddChild(_statusColumn);
    }

    private void BuildFooter()
    {
        _footer = new Control { Name = "Footer", MouseFilter = Control.MouseFilterEnum.Pass };
        _buildButton = new Button { Text = "BUILD" };
        HudTheme.Button(_buildButton, HudTheme.Cyan);
        _buildButton.Pressed += ToggleBuild;
        _footer.AddChild(_buildButton);

        _feedPanel = CreatePanel(_footer, "FEED");
        _feedPanel.ClipContents = true;
        _feedRowsContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _feedRowsContainer.AddThemeConstantOverride("separation", HudTheme.S(4));
        Content(_feedPanel).AddChild(_feedRowsContainer);

        _inputPanel = CreatePanel(_footer, "");
        var inputRow = new HBoxContainer();
        var prompt = new Label { Text = ">", CustomMinimumSize = HudTheme.V(28, 0) };
        HudTheme.Label(prompt, HudTheme.Green, HudTheme.BodyUnits);
        inputRow.AddChild(prompt);
        _input = new LineEdit
        {
            PlaceholderText = "say or try anything",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClearButtonEnabled = true,
        };
        HudTheme.Input(_input);
        _input.TextSubmitted += text =>
        {
            FreeTextSubmitted?.Invoke(text);
            _input.Clear();
            _input.ReleaseFocus();
        };
        inputRow.AddChild(_input);
        Content(_inputPanel).AddChild(inputRow);

        Build.SlotSelected += selection => PushFeed($"BUILD ARMED · {selection.Subject.ToUpperInvariant()}", "seen");
        Build.SlotsClosed += () => BuildSlotsRequested?.Invoke();
        _root.AddChild(_footer);
    }

    private void ToggleBuild()
    {
        if (Build.Visible && Build.Close()) return;
        Build.Open();
        BuildSlotsRequested?.Invoke();
    }

    private void Layout()
    {
        if (_root is null || _footer is null) return;
        var window = GetViewport().GetVisibleRect().Size;
        HudTheme.SetScale(window.Y / HudLayout.Height);
        var frame = HudLayout.Solve(window);
        _root.Theme.DefaultFontSize = HudTheme.S(HudTheme.BodyUnits);

        Place(_header, frame.Header);
        _clockPanel.Size = _clockPanel.CustomMinimumSize = new Vector2(HudTheme.S(600), _header.Size.Y - HudTheme.S(12));

        Place(_menu, frame.Menu);
        for (var i = 0; i < _menuButtons.Length; i++)
        {
            var rect = HudLayout.MenuButton(i, frame.Scale);
            _menuButtons[i].Position = HudTheme.V(rect.Position);
            _menuButtons[i].Size = HudTheme.V(rect.Size);
            _menuButtons[i].AddThemeFontSizeOverride("font_size", HudTheme.S(HudTheme.BodyUnits));
        }

        Place(_gameView, frame.Viewport);
        Place(_compass, frame.Compass);
        Place(Build, frame.Panel);
        Place(_debugView, frame.Viewport);
        Place(_llmView, frame.Viewport);

        Place(_statusColumn, frame.Status);
        Place(_footer, frame.Footer);
        // Children are positioned in their parent's space, so frame rectangles are converted here.
        Place(_buildButton, frame.BuildButton, frame.Footer);
        _buildButton.AddThemeFontSizeOverride("font_size", HudTheme.S(HudTheme.BodyUnits));
        Place(_feedPanel, frame.Feed, frame.Footer);
        Place(_inputPanel, frame.Input, frame.Footer);

        if (_newGame.Visible) _newGame.Position = (_root.Size - _newGame.Size) * 0.5f;
        _viewportRect = HudTheme.R(frame.Viewport.Position.X, frame.Viewport.Position.Y,
            frame.Viewport.Size.X, frame.Viewport.Size.Y);
        ViewportRectChanged?.Invoke(_viewportRect);
    }

    /// <summary>Positions and sizes a control from a design-unit rectangle; the rectangle is in the
    /// space of the control's parent, so a control inside `GameViewport` gets a viewport-local one.</summary>
    private static void Place(Control control, Rect2 units)
    {
        control.Position = HudTheme.V(units.Position);
        control.Size = control.CustomMinimumSize = HudTheme.V(units.Size);
    }

    /// <summary>The same, converting a frame-absolute rectangle into the parent's space.</summary>
    private static void Place(Control control, Rect2 units, Rect2 parent) =>
        Place(control, new Rect2(units.Position - parent.Position, units.Size));

    private void UpdateMenuStyles()
    {
        for (var i = 0; i < _menuButtons.Length; i++)
        {
            var (view, _, _, enabled) = MenuEntries[i];
            var active = view == ActiveView;
            HudTheme.Button(_menuButtons[i], active ? HudTheme.Cyan : HudTheme.Text, active);
            if (!enabled) _menuButtons[i].Disabled = true;
        }
    }

    private static Button AddMiniButton(Container parent, string text, Action callback)
    {
        var button = new Button { Text = text };
        HudTheme.MiniButton(button, HudTheme.Text);
        button.Pressed += callback;
        parent.AddChild(button);
        return button;
    }

    private static PanelContainer CreatePanel(Control parent, string title)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        panel.AddThemeStyleboxOverride("panel", HudTheme.PanelFrame());
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", HudTheme.S(6));
        if (!string.IsNullOrEmpty(title))
        {
            body.AddChild(HudTheme.Heading(title));
            body.AddChild(new ColorRect
            {
                Color = HudTheme.Line,
                CustomMinimumSize = HudTheme.V(0, 1),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        panel.AddChild(body);
        parent.AddChild(panel);
        return panel;
    }

    private static VBoxContainer Content(Control panel) => panel.GetChild<VBoxContainer>(0);
}
