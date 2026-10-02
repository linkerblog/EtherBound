using System;
using System.Collections.Generic;
using System.Linq;
using EtherBound.Host;
using EtherBound.Llm;
using EtherBound.Sim.Core;
using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The HUD shell on the 2560x1440 design frame (`HudLayout`), laid out in design units, scaled to
/// the window and centred in it. Left `MainMenu`, header with brand, clock and speed control, the
/// `GameViewport` rectangle the 3D view renders into, the status column (`ACT`, `CARRY`, `NEARBY`),
/// and the footer with `BUILD`, the feed and the input line. The sim stays the source of truth for
/// every number: this class reads `WorldFrame` and raises events, it never writes state.
///
/// Sizes and styleboxes are baked at the scale they are created at, so the frame is rebuilt when
/// the window changes the scale (the maximize at start-up, a manual resize). The feed history lives
/// here and is replayed into the new log, so a rebuild loses nothing the player saw.
/// </summary>
public partial class GameHud : CanvasLayer
{
    private sealed record MenuItem(string View, string Label, string Hint, bool Enabled);

    private sealed record MenuCell(Button Button, ColorRect Rule, PanelContainer Chip);

    private static readonly MenuItem[] MenuItems =
    {
        new("GAME", "GAME", "ALT+1", true),
        new("INVENTORY", "INVENTORY", "—", false),
        new("DEBUG", "DEBUG", "ALT+2", true),
        new("LLM", "LLM", "ALT+3", true),
    };

    private readonly Dictionary<int, Button> _speedButtons = new();
    private readonly List<MenuCell> _menuButtons = new();
    private sealed class FeedEntry
    {
        public FeedEntry(int minute, string text, string category) => (Minute, Text, Category) = (minute, text, category);

        public int Minute { get; }
        public string Text { get; set; }
        public string Category { get; }
    }

    private readonly List<FeedEntry> _feedHistory = new();
    private FeedEntry? _liveEntry;
    private FeedLog.Handle? _liveRow;
    private int _liveId;
    private LlmRuntime? _llm;
    private readonly List<Control> _carryRows = new();
    private readonly List<Control> _nearbyRows = new();
    private Theme _theme = null!;
    private Font _bold = null!;
    private Control _root = null!, _frameRoot = null!;
    private PanelContainer _header = null!, _clockPanel = null!, _feedPanel = null!, _inputPanel = null!;
    private PanelContainer _mapPanel = null!, _activityPanel = null!, _carryPanel = null!, _nearbyPanel = null!, _statusPanel = null!;
    private MinimapPanel _minimap = null!;
    private Control _menu = null!, _gameView = null!, _debugView = null!, _llmView = null!, _statusColumn = null!, _footer = null!;
    private ViewportFrame _viewportFrame = null!;
    private Label _clockText = null!, _subtitle = null!, _activityText = null!, _loadText = null!;
    private MeterBar _activityMeter = null!, _loadMeter = null!;
    private FeedLog _feed = null!;
    private LineEdit _input = null!;
    private Control _inputChip = null!;
    private Button _pauseButton = null!, _buildButton = null!;
    private GeneratorPanel _newGame = null!;
    private DevConsole _console = null!;
    private CompassOverlay _compass = null!;
    private WorldFrame? _frame;
    private int _buildRefresh;
    private string _nearbySignature = "\0";
    private IReadOnlyList<NearbyList.Entry> _nearby = Array.Empty<NearbyList.Entry>();
    private float _builtScale = -1f;
    private HudLayout.Frame _layoutFrame = HudLayout.Solve(1f);
    private int _materialCount = -1;
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
        var body = HudTheme.LoadBodyFont();
        _bold = HudTheme.LoadBoldFont(body);
        HudTheme.SetScale(HudLayout.ScaleFor(GetViewport().GetVisibleRect().Size));
        _theme = HudTheme.CreateTheme(body);
        ActionMenus.Theme = _theme;
        Build.Theme = _theme;
        _root = new Control { Name = "HudRoot", Modulate = new Color(1, 1, 1, 1), Theme = _theme };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_root);
        AddChild(ActionMenus);
        PushFeed("ALT+1 GAME · ALT+2 DEBUG · ALT+3 LLM", "seen");
        GetViewport().SizeChanged += Layout;
        Layout();
    }

    public void UpdateFrame(WorldFrame frame)
    {
        _frame = frame;
        var player = frame.Actors.FirstOrDefault(actor => actor.Id == Ids.Player);
        if (player is null) return;
        var day = frame.GameMinute / 1440 + 1;
        var minute = frame.GameMinute % 1440;
        _clockText.Text = $"DAY {day:00} · {minute / 60:00}:{minute % 60:00}";
        _clockText.AddThemeColorOverride("font_color", frame.Paused ? HudTheme.Yellow : HudTheme.Green);
        _subtitle.Text = frame.Paused ? "// PAUSED" : "// LIVE SIMULATION";
        _subtitle.AddThemeColorOverride("font_color", frame.Paused ? HudTheme.Yellow : HudTheme.Dim);
        _pauseButton.Text = frame.Paused ? "▶" : "II";
        _pauseButton.TooltipText = frame.Paused ? "Resume the world clock" : "Pause the world clock";
        HudTheme.Segment(_pauseButton, frame.Paused ? HudTheme.Yellow : HudTheme.Text, frame.Paused);
        foreach (var (speed, button) in _speedButtons)
        {
            var selected = frame.Speed == speed;
            HudTheme.Segment(button, selected ? HudTheme.Cyan : HudTheme.Dim, selected);
        }

        if (frame.Materials.Length != _materialCount)
        {
            _materialCount = frame.Materials.Length;
            Build.SetMaterials(frame.Materials.Select(material => (material.Name, material.Color)));
        }
        UpdateCarry(player);
        UpdateActivity(frame, player);
        _minimap.SetMap(frame.Minimap, player.X, player.Y);

        _newGame.UpdateFrame(frame);
        _console.UpdateFrame(frame);
        // A container keeps the size it grew to while its content was taller, and this page is
        // rebuilt from every frame; putting it back on the viewport rectangle keeps it off the footer.
        if (_debugView.Visible) Place(_debugView, _layoutFrame.Viewport);
        // The slots read the world through `Menu.Build`; refresh them while the panel is open so a
        // wall built a moment ago is not offered again.
        if (Build.Visible && ++_buildRefresh >= 20)
        {
            _buildRefresh = 0;
            BuildSlotsRequested?.Invoke();
        }
    }

    /// <summary>
    /// The actors Niko can currently see, already filtered by the client to those inside
    /// `GameViewport`, so this panel can only ever list what is on screen. Read-only.
    /// </summary>
    public void SetNearby(IReadOnlyList<NearbyList.Entry> visible)
    {
        _nearby = visible;
        if (_nearbyPanel is null) return;
        var rows = NearbyList.Rows(visible);
        var signature = string.Join("\n", rows.Select(row => $"{row.Name}|{row.Distance}|{row.Activity}"));
        if (signature == _nearbySignature) return;
        _nearbySignature = signature;
        RebuildNearby(rows);
    }

    public void PushFeed(string text, string category = "info")
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var minute = _frame?.GameMinute ?? 0;
        _feedHistory.Add(new FeedEntry(minute, text, category));
        TrimHistory();
        _feed?.Push(minute, text, category);
    }

    private void TrimHistory()
    {
        if (FeedFormat.Overflow(_feedHistory.Count) > 0) _feedHistory.RemoveRange(0, FeedFormat.Overflow(_feedHistory.Count));
    }

    /// <summary>Narration the log already holds, shown again after a restart; each line keeps the minute it was written at.</summary>
    public void RestoreNarration(IEnumerable<(int Minute, string Text)> lines)
    {
        foreach (var (minute, text) in lines)
        {
            _feedHistory.Add(new FeedEntry(minute, text, "narr"));
            _feed?.Push(minute, text, "narr", animate: false, wrap: true);
        }
        TrimHistory();
    }

    /// <summary>
    /// Narration streams into one row that grows as the model writes (Dev-008). It is the engine's line that stays
    /// if the narration later fails, so a row that did not end well is removed rather than left half written.
    /// </summary>
    public void StreamNarration(int id, string piece)
    {
        if (_liveEntry is null || _liveId != id)
        {
            FinishNarration(_liveId, null);
            var minute = _frame?.GameMinute ?? 0;
            _liveEntry = new FeedEntry(minute, "", "narr");
            _liveId = id;
            _feedHistory.Add(_liveEntry);
            TrimHistory();
            _liveRow = _feed?.Push(minute, "", "narr", wrap: true);
        }
        _liveEntry.Text += piece;
        _liveRow?.SetText(_liveEntry.Text.TrimStart());
    }

    /// <summary>The model is retrying after a failed check: what was shown so far is void.</summary>
    public void RestartNarration(int id)
    {
        if (_liveEntry is null || _liveId != id) return;
        _liveEntry.Text = "";
        _liveRow?.SetText("");
    }

    /// <summary>Ends the live narration: <paramref name="text"/> replaces what streamed, null removes the row.</summary>
    public void FinishNarration(int id, string? text)
    {
        if (_liveEntry is null || _liveId != id) return;
        if (text is null)
        {
            _feedHistory.Remove(_liveEntry);
            _liveRow?.Remove();
        }
        else
        {
            _liveEntry.Text = text;
            _liveRow?.SetText(text);
        }
        _liveEntry = null;
        _liveRow = null;
        _liveId = 0;
    }

    public void ShowNewGame()
    {
        _newGame.OpenNew();
        Layout();
    }

    public void CloseNewGame() => _newGame?.CloseNew();

    /// <summary>Gives the LLM tab the runtime it reads and edits; the tab is rebuilt with the rest of the HUD.</summary>
    public void SetLlm(LlmRuntime? runtime) => _llm = runtime;

    public void ShowGeneratorError(string message) => _newGame.ShowError(message);

    /// <summary>Fills the build panel from the sim's own `build` entries; nothing is authored here.</summary>
    public void ApplyBuildMenu(HostMenuPayload menu) => Build.SetEntries(menu.Entries);

    public bool CloseBuild()
    {
        var closed = Build.Close();
        if (closed) UpdateBuildButton();
        return closed;
    }

    /// <summary>The centre of a menu button in window pixels, to hover it; for the screenshot driver.</summary>
    public Vector2 MenuButtonCenter(string view)
    {
        var index = Array.FindIndex(MenuItems, item => item.View == view);
        return index < 0 ? Vector2.Zero : _menuButtons[index].Button.GetGlobalRect().GetCenter();
    }

    /// <summary>Scrolls the feed to its oldest row; for the screenshot driver.</summary>
    public void ScrollFeedToTop() => _feed.ScrollToTop();

    /// <summary>Scrolls the feed to its newest row; for the screenshot driver.</summary>
    public void ScrollFeedToEnd() => _feed.ScrollToBottom();

    /// <summary>Selects a console section; for the screenshot driver.</summary>
    public void ShowConsoleSection(string key) => _console.ShowSection(key);

    public void SwitchView(string view)
    {
        if (view is not ("GAME" or "DEBUG" or "LLM") || view == ActiveView) return;
        ActiveView = view;
        ApplyView();
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

    // ------------------------------------------------------------------ construction

    /// <summary>Creates every scaled control from scratch. Called again when the scale changes.</summary>
    private void BuildFrame()
    {
        if (Build.GetParent() is { } parent) parent.RemoveChild(Build);
        if (_frameRoot is not null)
        {
            _root.RemoveChild(_frameRoot);
            _frameRoot.QueueFree();
        }
        if (_newGame is not null)
        {
            _root.RemoveChild(_newGame);
            _newGame.QueueFree();
        }
        _speedButtons.Clear();
        _menuButtons.Clear();
        _carryRows.Clear();
        _nearbyRows.Clear();
        _nearbySignature = "\0";

        HudTheme.ApplyScale(_theme);
        _frameRoot = new Control { Name = "HudFrame", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_frameRoot);
        BuildHeader();
        BuildMenu();
        BuildViewportAndStatus();
        BuildFooter();
        _newGame = new GeneratorPanel(newGameMode: true) { Visible = false };
        _newGame.Confirmed += request => NewGameRequested?.Invoke(request);
        _root.AddChild(_newGame);
        Build.Restyle();

        _liveRow = null;
        foreach (var entry in _feedHistory)
        {
            var handle = _feed.Push(entry.Minute, entry.Text, entry.Category, animate: false, wrap: entry.Category == "narr");
            if (entry == _liveEntry) _liveRow = handle;
        }
        ApplyView();
        UpdateMenuStyles();
        UpdateBuildButton();
        if (_frame is not null) UpdateFrame(_frame);
        SetNearby(_nearby);
    }

    private void BuildHeader()
    {
        _header = new PanelContainer { Name = "Header", MouseFilter = Control.MouseFilterEnum.Pass };
        _header.AddThemeStyleboxOverride("panel", HudTheme.HeaderFrame());
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", HudTheme.S(14));

        row.AddChild(new ColorRect
        {
            Color = HudTheme.Cyan,
            CustomMinimumSize = HudTheme.V(6, 34),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var brand = new Label { Text = "ETHERBOUND", VerticalAlignment = VerticalAlignment.Center };
        HudTheme.Label(brand, HudTheme.Text, HudTheme.BrandUnits);
        brand.AddThemeFontOverride("font", _bold);
        row.AddChild(brand);
        _subtitle = new Label { Text = "// LIVE SIMULATION", VerticalAlignment = VerticalAlignment.Center };
        HudTheme.Label(_subtitle, HudTheme.Dim, HudTheme.SmallUnits);
        row.AddChild(_subtitle);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        _clockPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _clockPanel.AddThemeStyleboxOverride("panel", HudTheme.Box(HudTheme.Bg, HudTheme.LineHi, 0.9f, HudTheme.S(1), HudTheme.S(12), HudTheme.S(3)));
        var clockRow = new HBoxContainer();
        clockRow.AddThemeConstantOverride("separation", HudTheme.S(16));
        _clockText = new Label { Text = "DAY 01 · 00:00", VerticalAlignment = VerticalAlignment.Center };
        HudTheme.Label(_clockText, HudTheme.Green, HudTheme.ClockUnits);
        _clockText.AddThemeFontOverride("font", _bold);
        clockRow.AddChild(_clockText);

        var segments = new HBoxContainer();
        segments.AddThemeConstantOverride("separation", HudTheme.S(4));
        _pauseButton = AddSegment(segments, "II", () => PauseRequested?.Invoke(!(_frame?.Paused ?? false)));
        foreach (var speed in new[] { 1, 3, 10 })
            _speedButtons[speed] = AddSegment(segments, $"x{speed}", () => SpeedRequested?.Invoke(speed));
        clockRow.AddChild(segments);

        var newButton = new Button { Text = "NEW", CustomMinimumSize = HudTheme.V(78, 0) };
        HudTheme.Segment(newButton, HudTheme.Green, false);
        newButton.Pressed += ShowNewGame;
        clockRow.AddChild(newButton);
        _clockPanel.AddChild(clockRow);
        row.AddChild(_clockPanel);

        _header.AddChild(row);
        _header.AddChild(new Scanlines());
        _frameRoot.AddChild(_header);
    }

    private void BuildMenu()
    {
        _menu = new Control { Name = "MainMenu", MouseFilter = Control.MouseFilterEnum.Pass };
        foreach (var item in MenuItems)
        {
            // ClipText keeps a long label inside its 200-unit column instead of growing the button
            // under the viewport; the hotkey chip is a child anchored to the right edge.
            var button = new Button { Text = item.Label, Disabled = !item.Enabled, Alignment = HorizontalAlignment.Left, ClipText = true };
            var rule = new ColorRect { Color = HudTheme.Cyan, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            rule.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
            rule.OffsetRight = HudTheme.S(6);
            button.AddChild(rule);
            var chip = HudTheme.Chip(item.Hint, item.Enabled ? HudTheme.Dim : HudTheme.LineHi);
            chip.AnchorLeft = chip.AnchorRight = 1;
            chip.AnchorTop = chip.AnchorBottom = 0.5f;
            chip.GrowHorizontal = Control.GrowDirection.Begin;
            chip.GrowVertical = Control.GrowDirection.Both;
            chip.OffsetLeft = chip.OffsetRight = -HudTheme.S(10);
            button.AddChild(chip);
            var view = item.View;
            if (item.Enabled) button.Pressed += () => SwitchView(view);
            _menuButtons.Add(new MenuCell(button, rule, chip));
            _menu.AddChild(button);
        }
        _frameRoot.AddChild(_menu);
    }

    private void BuildViewportAndStatus()
    {
        _viewportFrame = new ViewportFrame();
        _frameRoot.AddChild(_viewportFrame);
        _gameView = new Control { Name = "GameViewport", MouseFilter = Control.MouseFilterEnum.Ignore };
        _compass = new CompassOverlay();
        _gameView.AddChild(_compass);
        _gameView.AddChild(Build);
        _frameRoot.AddChild(_gameView);
        // The console and the LLM placeholder cover the viewport area but not the world: the world
        // renders on its own canvas below the HUD, so these are siblings of the GAME overlay.
        _console = new DevConsole { ClipContents = true };
        _console.RegenerateRequested += request => NewGameRequested?.Invoke(request);
        _debugView = _console;
        _frameRoot.AddChild(_debugView);
        _llmView = new LlmPanel(_llm) { Visible = false };
        _frameRoot.AddChild(_llmView);

        _statusColumn = new Control { Name = "StatusColumn", MouseFilter = Control.MouseFilterEnum.Pass };
        var stack = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        // The column is a plain Control, so the stack needs anchors to take its width and height.
        stack.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        stack.AddThemeConstantOverride("separation", HudTheme.S(24));

        // The map is first, so it stays at the top of the column whatever the other panels show.
        _mapPanel = CreatePanel(stack, "MAP");
        _minimap = new MinimapPanel { CustomMinimumSize = HudTheme.V(HudLayout.MinimapSize, HudLayout.MinimapSize) };
        Content(_mapPanel).AddChild(_minimap);

        _activityPanel = CreatePanel(stack, "ACT");
        _activityText = new Label { Text = "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true };
        HudTheme.Label(_activityText, HudTheme.Text, HudTheme.SmallUnits);
        Content(_activityPanel).AddChild(_activityText);
        _activityMeter = new MeterBar(HudTheme.Cyan);
        Content(_activityPanel).AddChild(_activityMeter);
        _activityPanel.Visible = false;

        _carryPanel = CreatePanel(stack, "CARRY");
        _loadText = new Label { Text = "LOAD  0.0 kg", ClipText = true };
        HudTheme.Label(_loadText, HudTheme.Dim, HudTheme.SmallUnits);
        _loadMeter = new MeterBar(HudTheme.Green);
        Content(_carryPanel).AddChild(_loadText);
        Content(_carryPanel).AddChild(_loadMeter);

        _nearbyPanel = CreatePanel(stack, "NEARBY");

        // D4: the design's StatBar components (HP, EP) draw nothing until the sim exposes vitals of
        // its own; a placeholder would show invented numbers, so the panel stays hidden.
        _statusPanel = CreatePanel(stack, "STATUS");
        _statusPanel.Visible = false;
        _statusColumn.AddChild(stack);
        _frameRoot.AddChild(_statusColumn);
    }

    private void BuildFooter()
    {
        _footer = new Control { Name = "Footer", MouseFilter = Control.MouseFilterEnum.Pass };
        _buildButton = new Button { Text = "BUILD" };
        _buildButton.Pressed += ToggleBuild;
        _footer.AddChild(_buildButton);

        // No title row: the footer slot is 80 units tall and three feed rows need all of it.
        _feedPanel = new PanelContainer { Name = "FeedPanel", MouseFilter = Control.MouseFilterEnum.Pass, ClipContents = true };
        _feedPanel.AddThemeStyleboxOverride("panel", HudTheme.PanelFrame(padY: 2));
        _feed = new FeedLog { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _feedPanel.AddChild(_feed);
        _feedPanel.AddChild(new Scanlines());
        _footer.AddChild(_feedPanel);

        _inputPanel = new PanelContainer { Name = "InputPanel", MouseFilter = Control.MouseFilterEnum.Pass };
        _inputPanel.AddThemeStyleboxOverride("panel", HudTheme.PanelFrame(padY: 2));
        var inputRow = new HBoxContainer();
        var prompt = new Label { Text = ">", CustomMinimumSize = HudTheme.V(28, 0), VerticalAlignment = VerticalAlignment.Center };
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
        _input.FocusEntered += () => SetInputFocused(true);
        _input.FocusExited += () => SetInputFocused(false);
        inputRow.AddChild(_input);
        _inputChip = HudTheme.Chip("ENTER");
        _inputChip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        inputRow.AddChild(_inputChip);
        _inputPanel.AddChild(inputRow);
        _footer.AddChild(_inputPanel);

        Build.SlotSelected -= OnSlotSelected;
        Build.SlotSelected += OnSlotSelected;
        Build.SlotsClosed -= OnSlotsClosed;
        Build.SlotsClosed += OnSlotsClosed;
        _frameRoot.AddChild(_footer);
    }

    private void OnSlotSelected(BuildPanel.Selection selection) =>
        PushFeed($"BUILD ARMED · {selection.Subject.ToUpperInvariant()}", "seen");

    private void OnSlotsClosed()
    {
        UpdateBuildButton();
        BuildSlotsRequested?.Invoke();
    }

    // ------------------------------------------------------------------ updates

    private void UpdateCarry(HostActor player)
    {
        foreach (var row in _carryRows)
        {
            row.GetParent().RemoveChild(row);
            row.QueueFree();
        }
        _carryRows.Clear();
        var rows = CarryList.Rows(player.Carried.Select(item => new CarryList.Item(item.Slot, item.Name, item.Quantity)));
        var body = Content(_carryPanel);
        var insertAt = body.GetChildCount() - 2;
        foreach (var row in rows)
        {
            var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", HudTheme.S(10));
            var slot = new Label { Text = row.Slot, CustomMinimumSize = HudTheme.V(78, 0) };
            HudTheme.Label(slot, HudTheme.Dim, HudTheme.SmallUnits);
            var item = new Label
            {
                Text = row.Text,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            HudTheme.Label(item, row.Filled ? HudTheme.Text : HudTheme.LineHi, HudTheme.SmallUnits);
            line.AddChild(slot);
            line.AddChild(item);
            body.AddChild(line);
            body.MoveChild(line, insertAt++);
            _carryRows.Add(line);
        }
        var heavy = player.LoadKg > HudMeter.FreeLoadKg;
        _loadText.Text = $"LOAD  {player.LoadKg:0.0} kg";
        _loadText.AddThemeColorOverride("font_color", heavy ? HudTheme.Yellow : HudTheme.Dim);
        _loadMeter.Set(HudMeter.Load(player.LoadKg));
    }

    private void UpdateActivity(WorldFrame frame, HostActor player)
    {
        var activity = player.Activity;
        _activityPanel.Visible = activity is not null;
        if (activity is null) return;
        _activityText.Text = $"{activity.Op.ToUpperInvariant()}  {Math.Max(0, activity.EndsMinute - frame.GameMinute)} MIN";
        _activityMeter.Set(HudMeter.Activity(frame.GameMinute, activity.StartedMinute, activity.EndsMinute));
    }

    private void RebuildNearby(IReadOnlyList<NearbyList.Row> rows)
    {
        foreach (var row in _nearbyRows)
        {
            row.GetParent().RemoveChild(row);
            row.QueueFree();
        }
        _nearbyRows.Clear();
        var body = Content(_nearbyPanel);
        if (rows.Count == 0)
        {
            var empty = new Label { Text = "NO ONE IN VIEW" };
            HudTheme.Label(empty, HudTheme.LineHi, HudTheme.SmallUnits);
            body.AddChild(empty);
            _nearbyRows.Add(empty);
            return;
        }
        foreach (var row in rows)
        {
            var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", HudTheme.S(8));
            var name = new Label
            {
                Text = row.Name,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            HudTheme.Label(name, HudTheme.Text, HudTheme.SmallUnits);
            line.AddChild(name);
            if (row.Activity is { } activity)
            {
                var doing = new Label { Text = activity };
                HudTheme.Label(doing, HudTheme.Cyan, HudTheme.TinyUnits);
                line.AddChild(doing);
            }
            var distance = new Label { Text = row.Distance, HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = HudTheme.V(70, 0) };
            HudTheme.Label(distance, HudTheme.Dim, HudTheme.SmallUnits);
            line.AddChild(distance);
            body.AddChild(line);
            _nearbyRows.Add(line);
        }
    }

    private void ApplyView()
    {
        _gameView.Visible = ActiveView == "GAME";
        _debugView.Visible = ActiveView == "DEBUG";
        _llmView.Visible = ActiveView == "LLM";
        // A container keeps the size it grew to even after its content shrinks, so views that
        // switch sections are put back on their rectangle every time they are shown.
        if (_builtScale > 0) Layout();
    }

    private void ToggleBuild()
    {
        if (Build.Visible && Build.Close()) return;
        Build.Open();
        UpdateBuildButton();
        BuildSlotsRequested?.Invoke();
    }

    private void UpdateBuildButton()
    {
        if (_buildButton is null) return;
        HudTheme.Button(_buildButton, HudTheme.Cyan, Build.Visible);
    }

    private void SetInputFocused(bool focused)
    {
        _inputPanel.AddThemeStyleboxOverride("panel", HudTheme.PanelFrame(padY: 2, accent: focused ? HudTheme.Green : null));
        _inputChip.Visible = !focused;
    }

    private void UpdateMenuStyles()
    {
        for (var i = 0; i < _menuButtons.Count; i++)
        {
            var (button, rule, chip) = _menuButtons[i];
            var item = MenuItems[i];
            var active = item.View == ActiveView;
            HudTheme.Button(button, active ? HudTheme.Cyan : HudTheme.Text, active);
            button.AddThemeFontSizeOverride("font_size", HudTheme.S(22));
            rule.Visible = active;
            if (!item.Enabled) button.Disabled = true;
        }
    }

    // ------------------------------------------------------------------ layout

    private void Layout()
    {
        if (_root is null) return;
        var window = GetViewport().GetVisibleRect().Size;
        var frame = _layoutFrame = HudLayout.Solve(window);
        if (_builtScale < 0f || Math.Abs(frame.Scale - _builtScale) > 0.004f)
        {
            HudTheme.SetScale(frame.Scale);
            _builtScale = frame.Scale;
            BuildFrame();
        }

        _frameRoot.Position = frame.Offset;
        _frameRoot.Size = HudTheme.V(HudLayout.Width, HudLayout.Height);
        Place(_header, frame.Header);
        for (var i = 0; i < _menuButtons.Count; i++)
        {
            var rect = HudLayout.MenuButton(i, frame.Scale);
            Place(_menuButtons[i].Button, rect);
        }
        Place(_menu, frame.Menu);

        var framed = new Rect2(frame.Viewport.Position - new Vector2(4, 4), frame.Viewport.Size + new Vector2(8, 8));
        Place(_viewportFrame, framed);
        Place(_gameView, frame.Viewport);
        Place(_compass, frame.Compass);
        Place(Build, frame.Panel);
        Build.ApplyHeight();
        Place(_debugView, frame.Viewport);
        Place(_llmView, frame.Viewport);

        Place(_statusColumn, frame.Status);
        Place(_footer, frame.Footer);
        // Children are positioned in their parent's space, so frame rectangles are converted here.
        Place(_buildButton, frame.BuildButton, frame.Footer);
        Place(_feedPanel, frame.Feed, frame.Footer);
        Place(_inputPanel, frame.Input, frame.Footer);

        if (_newGame.Visible) _newGame.Position = (_root.Size - _newGame.Size) * 0.5f;
        _viewportRect = new Rect2(frame.Offset + HudTheme.V(frame.Viewport.Position), HudTheme.V(frame.Viewport.Size));
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

    private static Button AddSegment(Container parent, string text, Action callback)
    {
        var button = new Button { Text = text, CustomMinimumSize = HudTheme.V(62, 0) };
        HudTheme.Segment(button, HudTheme.Text, false);
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
        panel.AddChild(new Scanlines());
        parent.AddChild(panel);
        return panel;
    }

    private static VBoxContainer Content(Control panel) => panel.GetChild<VBoxContainer>(0);
}
