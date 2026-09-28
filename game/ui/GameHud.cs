using System;
using System.Collections.Generic;
using System.Linq;
using EtherBound.Host;
using EtherBound.Sim.Core;
using Godot;

namespace EtherBound.Game.Ui;

public partial class GameHud : CanvasLayer
{
    private static readonly Color TextColor = new("#e8ebf0");
    private static readonly Color DimColor = new("#a7b2c0");
    private static readonly Color CyanColor = new("#57c7ff");
    private static readonly Color GreenColor = new("#5af78e");
    private static readonly Color YellowColor = new("#f3f99d");
    private static readonly Color RedColor = new("#ff5c57");
    private static readonly Color InterfaceModulate = new(1, 1, 1, 1f);
    // The whole HUD is designed at 100% and scaled by this factor, so the layout keeps its numbers.
    private const float UiScale = 1.5f;
    private static int S(float value) => (int)MathF.Round(value * UiScale);
    private static Vector2 V(float x, float y) => new(x * UiScale, y * UiScale);

    private readonly Queue<Label> _feedRows = new();
    private readonly Dictionary<string, Button> _viewButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Button> _speedButtons = new();
    private Control _root = null!;
    private Control _gameView = null!, _debugView = null!, _llmView = null!;
    private PanelContainer _navigationPanel = null!;
    private PanelContainer _clockPanel = null!, _feedPanel = null!, _carryPanel = null!, _activityPanel = null!;
    private PanelContainer _debugShell = null!, _llmPanel = null!;
    private HBoxContainer _clockRow = null!, _viewTabs = null!;
    private Label _clockText = null!, _carryText = null!, _activityText = null!;
    private Label _brandLabel = null!;
    private ColorRect _brandDivider = null!;
    private ProgressBar _activityProgress = null!;
    private VBoxContainer _feedRowsContainer = null!;
    private LineEdit _input = null!;
    private Button _pauseButton = null!;
    private GeneratorPanel _newGame = null!, _mapForm = null!;
    private Label _fpsLabel = null!;
    private CompassOverlay _compass = null!;
    private WorldFrame? _frame;

    public GameHud()
    {
        Layer = 2;
        ActionMenus = new ActionMenuOverlay();
        ActionMenus.Modulate = InterfaceModulate;
    }

    public ActionMenuOverlay ActionMenus { get; }
    public string ActiveView { get; private set; } = "GAME";
    public bool InputHasFocus => _input is not null && _input.HasFocus();
    public bool NewGameVisible => _newGame is not null && _newGame.IsVisibleInTree();
    public bool CanMoveWorld => ActiveView == "GAME" && !InputHasFocus && !NewGameVisible && !ActionMenus.Visible;

    public event Action<int>? SpeedRequested;
    public event Action<bool>? PauseRequested;
    public event Action<NewGameRequest>? NewGameRequested;
    public event Action<string>? FreeTextSubmitted;
    public event Action<string>? ViewChanged;

    public override void _Ready()
    {
        var font = GD.Load<FontFile>("res://assets/fonts/Inter-VariableFont_opsz_wght.ttf");
        if (font is null) throw new InvalidOperationException("The HUD font could not be loaded.");
        var uiTheme = new Theme { DefaultFont = font, DefaultFontSize = S(12) };
        ActionMenus.Theme = uiTheme;
        _root = new Control { Name = "HudRoot", Modulate = InterfaceModulate, Theme = uiTheme };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_root);
        BuildViews();
        BuildGamePanels();
        BuildDebugView();
        BuildLlmView();
        BuildFpsCounter();
        BuildCompass();
        AddChild(ActionMenus);
        GetViewport().SizeChanged += Layout;
        Layout();
        PushFeed("ALT+1 GAME · ALT+2 DEBUG · ALT+3 LLM", "seen");
    }

    public override void _Process(double delta)
    {
        _fpsLabel.Text = $"{Engine.GetFramesPerSecond():0} FPS";
    }

    private void BuildFpsCounter()
    {
        _fpsLabel = new Label
        {
            Text = "0 FPS",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = V(70, 18),
            Size = V(70, 18),
        };
        StyleLabel(_fpsLabel, DimColor, true);
        _root.AddChild(_fpsLabel);
    }

    private void BuildCompass()
    {
        _compass = new CompassOverlay { CustomMinimumSize = V(80, 80), Size = V(80, 80) };
        _gameView.AddChild(_compass);
    }

    public void UpdateFrame(WorldFrame frame)
    {
        _frame = frame;
        var player = frame.Actors.FirstOrDefault(actor => actor.Id == Ids.Player);
        if (player is null) return;
        var day = frame.GameMinute / 1440 + 1;
        var minute = frame.GameMinute % 1440;
        _clockText.Text = $"DAY {day:00}   {minute / 60:00}:{minute % 60:00}";
        _clockText.AddThemeColorOverride("font_color", frame.Paused ? YellowColor : GreenColor);
        _pauseButton.Text = frame.Paused ? "PLAY" : "PAUSE";
        _pauseButton.TooltipText = frame.Paused ? "Resume the world clock" : "Pause the world clock";
        ButtonStyle(_pauseButton, frame.Paused ? YellowColor : TextColor, compact: true, selected: frame.Paused);
        foreach (var (speed, button) in _speedButtons)
        {
            var selected = frame.Speed == speed;
            ButtonStyle(button, selected ? CyanColor : DimColor, compact: true, selected: selected);
        }

        var carried = player.Carried.Length == 0
            ? "L  —\nR  —\nBACK  —"
            : string.Join("\n", player.Carried.Select(item => $"{item.Slot.ToUpperInvariant(),-5} {item.Name} ×{item.Quantity}"));
        _carryText.Text = $"{carried}\nLOAD  {player.LoadKg:0.0} kg";
        if (player.LoadKg > 10) _carryText.AddThemeColorOverride("font_color", YellowColor);
        else _carryText.AddThemeColorOverride("font_color", TextColor);

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
        _mapForm.UpdateFrame(frame);
        Layout();
    }

    public void PushFeed(string text, string category = "info")
    {
        if (string.IsNullOrWhiteSpace(text) || _feedRowsContainer is null) return;
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        var color = category switch
        {
            "warn" => YellowColor,
            "fail" or "harm" => RedColor,
            "act" or "seen" => CyanColor,
            "ether" => new Color("#ff6ac1"),
            _ => TextColor,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 12);
        _feedRowsContainer.AddChild(label);
        _feedRows.Enqueue(label);
        while (_feedRows.Count > 8)
        {
            var oldest = _feedRows.Dequeue();
            _feedRowsContainer.RemoveChild(oldest);
            oldest.QueueFree();
        }
        Layout();
    }

    public void ShowNewGame()
    {
        _newGame.OpenNew();
        PositionNewGame();
    }

    public void CloseNewGame() => _newGame.CloseNew();

    public void ShowGeneratorError(string message)
    {
        _newGame.ShowError(message);
        _mapForm.ShowError(message);
    }

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
        UpdateTabStyles();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key || !key.AltPressed) return;
        switch (key.Keycode)
        {
            case Key.Key1: SwitchView("GAME"); break;
            case Key.Key2: SwitchView("DEBUG"); break;
            case Key.Key3: SwitchView("LLM"); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void BuildViews()
    {
        _navigationPanel = new PanelContainer
        {
            Name = "NavigationPanel",
            Position = V(20, 12),
            Size = V(430, 76),
            CustomMinimumSize = V(430, 76),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _navigationPanel.AddThemeStyleboxOverride("panel", PanelFrame());
        var navigation = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        navigation.AddThemeConstantOverride("separation", S(7));
        navigation.AddChild(new ColorRect
        {
            Color = CyanColor,
            CustomMinimumSize = V(3, 26),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _brandLabel = new Label
        {
            Text = "ETHERBOUND",
            CustomMinimumSize = V(116, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        StyleLabel(_brandLabel, TextColor, true);
        _brandLabel.AddThemeFontSizeOverride("font_size", S(13));
        navigation.AddChild(_brandLabel);
        _brandDivider = new ColorRect
        {
            Color = new Color("#303a44"),
            CustomMinimumSize = V(1, 28),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        navigation.AddChild(_brandDivider);
        _viewTabs = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _viewTabs.AddThemeConstantOverride("separation", S(4));
        foreach (var view in new[] { "GAME", "DEBUG", "LLM" })
        {
            var button = new Button { Text = view, CustomMinimumSize = V(68, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            ButtonStyle(button, CyanColor, compact: true);
            button.Pressed += () => SwitchView(view);
            _viewButtons.Add(view, button);
            _viewTabs.AddChild(button);
        }
        navigation.AddChild(_viewTabs);
        _navigationPanel.AddChild(navigation);
        _root.AddChild(_navigationPanel);

        _gameView = new Control { Name = "GameView" };
        _gameView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _gameView.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(_gameView);
        _debugView = new Control { Name = "DebugView", Visible = false };
        _debugView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _debugView.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(_debugView);
        _llmView = new Control { Name = "LlmView", Visible = false };
        _llmView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _llmView.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(_llmView);
        UpdateTabStyles();
    }

    private void BuildGamePanels()
    {
        _clockPanel = CreatePanel(_root, Vector2.Zero, V(540, 76), "WORLD CLOCK");
        _clockRow = new HBoxContainer();
        _clockRow.AddThemeConstantOverride("separation", S(4));
        var clockContent = Content(_clockPanel);
        _clockText = new Label { Text = "DAY 01   00:00", CustomMinimumSize = V(154, 0), VerticalAlignment = VerticalAlignment.Center };
        StyleLabel(_clockText, GreenColor, true);
        _clockText.AddThemeFontSizeOverride("font_size", S(16));
        _clockRow.AddChild(_clockText);
        _pauseButton = AddMiniButton("PAUSE", () => PauseRequested?.Invoke(!(_frame?.Paused ?? false)));
        _pauseButton.CustomMinimumSize = V(56, 30);
        _clockRow.AddChild(_pauseButton);
        foreach (var speed in new[] { 1, 3, 10 })
        {
            var button = AddMiniButton($"x{speed}", () => SpeedRequested?.Invoke(speed));
            button.CustomMinimumSize = V(40, 30);
            _speedButtons.Add(speed, button);
            _clockRow.AddChild(button);
        }
        var newWorldButton = AddMiniButton("NEW", ShowNewGame);
        newWorldButton.CustomMinimumSize = V(48, 30);
        _clockRow.AddChild(newWorldButton);
        clockContent.AddChild(_clockRow);

        _feedPanel = CreatePanel(_gameView, V(20, 0), V(360, 166), "FEED");
        _feedRowsContainer = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _feedRowsContainer.AddThemeConstantOverride("separation", S(2));
        Content(_feedPanel).AddChild(_feedRowsContainer);

        _activityPanel = CreatePanel(_gameView, V(400, 0), V(280, 68), "ACT");
        var activityColumn = Content(_activityPanel);
        _activityText = new Label { Text = "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        StyleLabel(_activityText, TextColor);
        activityColumn.AddChild(_activityText);
        _activityProgress = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, CustomMinimumSize = V(250, 8), ShowPercentage = false };
        _activityProgress.AddThemeStyleboxOverride("background", FlatBox("#202a33"));
        _activityProgress.AddThemeStyleboxOverride("fill", FlatBox("#57c7ff"));
        activityColumn.AddChild(_activityProgress);
        _activityPanel.Visible = false;

        _carryPanel = CreatePanel(_gameView, Vector2.Zero, V(260, 140), "CARRY");
        _carryText = new Label { Text = "L  —\nR  —\nBACK  —\nLOAD  0.0 kg" };
        StyleLabel(_carryText, TextColor);
        Content(_carryPanel).AddChild(_carryText);

        var inputPanel = CreatePanel(_gameView, V(20, 0), V(540, 46), "");
        var inputRow = new HBoxContainer();
        var prompt = new Label { Text = ">", CustomMinimumSize = V(16, 0) };
        StyleLabel(prompt, GreenColor, true);
        inputRow.AddChild(prompt);
        _input = new LineEdit { PlaceholderText = "say or try anything", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
        StyleInput(_input);
        _input.TextSubmitted += text =>
        {
            FreeTextSubmitted?.Invoke(text);
            _input.Clear();
            _input.ReleaseFocus();
        };
        inputRow.AddChild(_input);
        Content(inputPanel).AddChild(inputRow);
        _inputPanel = inputPanel;

        _newGame = new GeneratorPanel(newGameMode: true) { Visible = false };
        _newGame.Confirmed += request => NewGameRequested?.Invoke(request);
        _root.AddChild(_newGame);
    }

    private PanelContainer _inputPanel = null!;

    private void BuildDebugView()
    {
        _debugShell = CreatePanel(_debugView, V(20, 96), V(520, 220), "DEBUG");
        _mapForm = new GeneratorPanel(newGameMode: false) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _mapForm.Confirmed += request => NewGameRequested?.Invoke(request);
        Content(_debugShell).AddChild(_mapForm);
    }

    private void BuildLlmView()
    {
        _llmPanel = CreatePanel(_llmView, V(20, 96), V(520, 120), "LLM");
        var placeholder = new Label { Text = "NO MODEL CONNECTED", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        StyleLabel(placeholder, DimColor);
        Content(_llmPanel).AddChild(placeholder);
    }

    private void Layout()
    {
        if (_root is null || _feedPanel is null) return;
        var size = _root.Size;
        var compact = size.X < S(700);
        var stacked = size.X < S(1030);
        var navigationWidth = stacked ? Math.Max(S(280), size.X - S(40)) : S(430);
        var clockWidth = stacked ? Math.Max(S(320), size.X - S(40)) : S(540);
        _navigationPanel.CustomMinimumSize = _navigationPanel.Size = new Vector2(navigationWidth, S(76));
        _clockPanel.CustomMinimumSize = _clockPanel.Size = new Vector2(clockWidth, S(76));
        _navigationPanel.Position = V(20, 12);
        _clockPanel.Position = stacked ? V(20, 96) : new Vector2(size.X - clockWidth - S(20), S(12));
        _brandLabel.Visible = !compact;
        _brandDivider.Visible = !compact;
        _clockText.CustomMinimumSize = new Vector2(compact ? S(92) : S(154), 0);
        _pauseButton.CustomMinimumSize = V(compact ? 46 : 56, 30);
        foreach (var button in _speedButtons.Values) button.CustomMinimumSize = V(compact ? 34 : 40, 30);
        var narrow = size.X < S(700);
        var bottomPanelWidth = narrow ? Math.Max(S(100), (size.X - S(60)) * 0.5f) : S(360);
        var carryWidth = narrow ? bottomPanelWidth : S(260);
        var inputWidth = Math.Min(S(540), Math.Max(S(260), size.X - S(40)));
        var feedHeight = Math.Clamp(S(48) + _feedRows.Count * S(15), S(64), S(166));
        _feedPanel.CustomMinimumSize = _feedPanel.Size = new Vector2(bottomPanelWidth, feedHeight);
        _carryPanel.CustomMinimumSize = _carryPanel.Size = new Vector2(carryWidth, S(140));
        _inputPanel.CustomMinimumSize = _inputPanel.Size = new Vector2(inputWidth, S(46));
        _inputPanel.Position = new Vector2(S(20), Math.Max(S(30), size.Y - _inputPanel.Size.Y - S(12)));
        _feedPanel.Position = new Vector2(S(20), Math.Max(S(90), _inputPanel.Position.Y - feedHeight - S(8)));
        _carryPanel.Position = new Vector2(Math.Max(S(20), size.X - carryWidth - S(20)), Math.Max(S(90), size.Y - S(216)));
        var activityY = stacked
            ? Math.Max(_clockPanel.Position.Y + _clockPanel.Size.Y + S(12), size.Y - S(320))
            : Math.Max(S(92), size.Y - S(134));
        _activityPanel.Position = new Vector2(Mathf.Clamp(size.X * 0.5f - S(140), S(20), Math.Max(S(20), size.X - S(300))), activityY);
        var contentY = stacked ? _clockPanel.Position.Y + _clockPanel.Size.Y + S(12) : S(96);
        var contentWidth = Math.Min(S(520), Math.Max(S(280), size.X - S(40)));
        _debugShell.Position = new Vector2(S(20), contentY);
        _debugShell.CustomMinimumSize = _debugShell.Size = new Vector2(contentWidth, Math.Clamp(_mapForm.Size.Y + S(46), S(220), Math.Max(S(220), size.Y - contentY - S(20))));
        _llmPanel.Position = new Vector2(S(20), contentY);
        _llmPanel.CustomMinimumSize = _llmPanel.Size = new Vector2(contentWidth, S(120));
        _fpsLabel.Position = new Vector2(size.X - _fpsLabel.CustomMinimumSize.X - S(12),
            size.Y - _fpsLabel.CustomMinimumSize.Y - S(10));
        _compass.Position = new Vector2(size.X - _compass.Size.X - S(28),
            Math.Max(_clockPanel.Position.Y + _clockPanel.Size.Y + S(12),
                _carryPanel.Position.Y - _compass.Size.Y - S(12)));
        PositionNewGame();
    }

    private void PositionNewGame()
    {
        if (_newGame is null || _root is null) return;
        _newGame.Position = (_root.Size - _newGame.Size) * 0.5f;
    }

    private void UpdateTabStyles()
    {
        if (_viewButtons.Count == 0) return;
        foreach (var (view, button) in _viewButtons)
        {
            var active = view == ActiveView;
            ButtonStyle(button, active ? CyanColor : DimColor, compact: true, selected: active);
        }
    }

    private Button AddMiniButton(string text, Action callback)
    {
        var button = new Button { Text = text, CustomMinimumSize = V(32, 28) };
        ButtonStyle(button, TextColor, compact: true);
        button.Pressed += callback;
        return button;
    }

    private static PanelContainer CreatePanel(Control parent, Vector2 position, Vector2 size, string title)
    {
        var panel = new PanelContainer { Position = position, Size = size, CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Pass };
        panel.AddThemeStyleboxOverride("panel", PanelFrame());
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", S(5));
        if (!string.IsNullOrEmpty(title))
        {
            var heading = new HBoxContainer { CustomMinimumSize = V(0, 15) };
            heading.AddThemeConstantOverride("separation", S(7));
            heading.AddChild(new ColorRect
            {
                Color = CyanColor,
                CustomMinimumSize = V(2, 10),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            var label = new Label { Text = title, VerticalAlignment = VerticalAlignment.Center };
            StyleLabel(label, CyanColor, true);
            label.AddThemeFontSizeOverride("font_size", S(11));
            heading.AddChild(label);
            body.AddChild(heading);
            body.AddChild(new ColorRect
            {
                Color = new Color("#25313b"),
                CustomMinimumSize = new Vector2(0, 1),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        panel.AddChild(body);
        parent.AddChild(panel);
        return panel;
    }

    private static VBoxContainer Content(PanelContainer panel) => panel.GetChild<VBoxContainer>(0);

    private static Label FieldLabel(string text) => new() { Text = text };

    private static void StyleLabel(Label label, Color color, bool bold = false)
    {
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", S(bold ? 13 : 12));
    }

    private static void ButtonStyle(Button button, Color color, bool compact, bool selected = false)
    {
        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_disabled_color", DimColor);
        button.AddThemeColorOverride("font_hover_color", TextColor);
        button.AddThemeColorOverride("font_pressed_color", new Color("#071117"));
        button.AddThemeStyleboxOverride("normal", selected
            ? Box("#12303c", "#57c7ff", 1)
            : Box("#10151b", "#303a44", 0.96f));
        button.AddThemeStyleboxOverride("hover", Box("#1a2b35", "#70d5ff", 1));
        button.AddThemeStyleboxOverride("pressed", Box("#57c7ff", "#a5e6ff", 1));
        button.AddThemeStyleboxOverride("focus", Box("#101820", "#57c7ff", 1));
        button.AddThemeStyleboxOverride("disabled", Box("#0b1015", "#29313a", 0.72f));
        button.AddThemeFontSizeOverride("font_size", S(compact ? 11 : 12));
    }

    private static void StyleInput(LineEdit input)
    {
        input.AddThemeColorOverride("font_color", TextColor);
        input.AddThemeColorOverride("font_placeholder_color", DimColor);
        input.AddThemeColorOverride("caret_color", CyanColor);
        input.AddThemeColorOverride("selection_color", new Color("#24495c"));
        input.AddThemeStyleboxOverride("normal", Box("#10151b", "#303a44", 1));
        input.AddThemeStyleboxOverride("focus", Box("#101820", "#57c7ff", 1));
        input.AddThemeFontSizeOverride("font_size", S(12));
    }

    private static StyleBoxFlat PanelFrame()
    {
        var frame = Box("#090e13", "#3f4d5a", 0.97f);
        frame.BorderWidthLeft = S(2);
        frame.ContentMarginLeft = S(10);
        frame.ContentMarginTop = S(7);
        frame.ContentMarginRight = S(10);
        frame.ContentMarginBottom = S(7);
        return frame;
    }

    private static StyleBoxFlat FlatBox(string color) => new()
    {
        BgColor = new Color(color),
        CornerRadiusBottomLeft = 0,
        CornerRadiusBottomRight = 0,
        CornerRadiusTopLeft = 0,
        CornerRadiusTopRight = 0,
    };

    private static StyleBoxFlat Box(string background, string border, float alpha)
    {
        var color = new Color(background);
        color.A = alpha;
        return new StyleBoxFlat
        {
            BgColor = color,
            BorderColor = new Color(border),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = S(8),
            ContentMarginTop = S(4),
            ContentMarginRight = S(8),
            ContentMarginBottom = S(4),
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 0,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
        };
    }
}
