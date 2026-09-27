using System;
using System.Collections.Generic;
using System.Linq;
using EtherBound.Host;
using EtherBound.Sim.Core;
using Godot;

namespace EtherBound.Game.Ui;

public partial class GameHud : CanvasLayer
{
    private static readonly Color TextColor = new("#d6d8de");
    private static readonly Color DimColor = new("#929daa");
    private static readonly Color CyanColor = new("#57c7ff");
    private static readonly Color GreenColor = new("#5af78e");
    private static readonly Color YellowColor = new("#f3f99d");
    private static readonly Color RedColor = new("#ff5c57");
    private static readonly Color InterfaceModulate = new(1, 1, 1, 0.8f);
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
        var font = GD.Load<FontFile>("res://assets/fonts/Montserrat-VariableFont_wght.ttf");
        if (font is null) throw new InvalidOperationException("The HUD font could not be loaded.");
        var uiTheme = new Theme { DefaultFont = font, DefaultFontSize = 12 };
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
            CustomMinimumSize = new Vector2(70, 18),
            Size = new Vector2(70, 18),
        };
        StyleLabel(_fpsLabel, DimColor, true);
        _root.AddChild(_fpsLabel);
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
            Position = new Vector2(20, 12),
            Size = new Vector2(430, 76),
            CustomMinimumSize = new Vector2(430, 76),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _navigationPanel.AddThemeStyleboxOverride("panel", PanelFrame());
        var navigation = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        navigation.AddThemeConstantOverride("separation", 7);
        navigation.AddChild(new ColorRect
        {
            Color = CyanColor,
            CustomMinimumSize = new Vector2(3, 26),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _brandLabel = new Label
        {
            Text = "ETHERBOUND",
            CustomMinimumSize = new Vector2(116, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        StyleLabel(_brandLabel, TextColor, true);
        _brandLabel.AddThemeFontSizeOverride("font_size", 13);
        navigation.AddChild(_brandLabel);
        _brandDivider = new ColorRect
        {
            Color = new Color("#303a44"),
            CustomMinimumSize = new Vector2(1, 28),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        navigation.AddChild(_brandDivider);
        _viewTabs = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _viewTabs.AddThemeConstantOverride("separation", 4);
        foreach (var view in new[] { "GAME", "DEBUG", "LLM" })
        {
            var button = new Button { Text = view, CustomMinimumSize = new Vector2(68, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
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
        _clockPanel = CreatePanel(_root, Vector2.Zero, new Vector2(540, 76), "WORLD CLOCK");
        _clockRow = new HBoxContainer();
        _clockRow.AddThemeConstantOverride("separation", 4);
        var clockContent = Content(_clockPanel);
        _clockText = new Label { Text = "DAY 01   00:00", CustomMinimumSize = new Vector2(154, 0), VerticalAlignment = VerticalAlignment.Center };
        StyleLabel(_clockText, GreenColor, true);
        _clockText.AddThemeFontSizeOverride("font_size", 16);
        _clockRow.AddChild(_clockText);
        _pauseButton = AddMiniButton("PAUSE", () => PauseRequested?.Invoke(!(_frame?.Paused ?? false)));
        _pauseButton.CustomMinimumSize = new Vector2(56, 30);
        _clockRow.AddChild(_pauseButton);
        foreach (var speed in new[] { 1, 3, 10 })
        {
            var button = AddMiniButton($"x{speed}", () => SpeedRequested?.Invoke(speed));
            button.CustomMinimumSize = new Vector2(40, 30);
            _speedButtons.Add(speed, button);
            _clockRow.AddChild(button);
        }
        var newWorldButton = AddMiniButton("NEW", ShowNewGame);
        newWorldButton.CustomMinimumSize = new Vector2(48, 30);
        _clockRow.AddChild(newWorldButton);
        clockContent.AddChild(_clockRow);

        _feedPanel = CreatePanel(_gameView, new Vector2(20, 0), new Vector2(360, 166), "FEED");
        _feedRowsContainer = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _feedRowsContainer.AddThemeConstantOverride("separation", 2);
        Content(_feedPanel).AddChild(_feedRowsContainer);

        _activityPanel = CreatePanel(_gameView, new Vector2(400, 0), new Vector2(280, 68), "ACT");
        var activityColumn = Content(_activityPanel);
        _activityText = new Label { Text = "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        StyleLabel(_activityText, TextColor);
        activityColumn.AddChild(_activityText);
        _activityProgress = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, CustomMinimumSize = new Vector2(250, 8), ShowPercentage = false };
        _activityProgress.AddThemeStyleboxOverride("background", FlatBox("#202a33"));
        _activityProgress.AddThemeStyleboxOverride("fill", FlatBox("#57c7ff"));
        activityColumn.AddChild(_activityProgress);
        _activityPanel.Visible = false;

        _carryPanel = CreatePanel(_gameView, new Vector2(0, 0), new Vector2(260, 140), "CARRY");
        _carryText = new Label { Text = "L  —\nR  —\nBACK  —\nLOAD  0.0 kg" };
        StyleLabel(_carryText, TextColor);
        Content(_carryPanel).AddChild(_carryText);

        var inputPanel = CreatePanel(_gameView, new Vector2(20, 0), new Vector2(540, 46), "");
        var inputRow = new HBoxContainer();
        var prompt = new Label { Text = ">", CustomMinimumSize = new Vector2(16, 0) };
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
        _debugShell = CreatePanel(_debugView, new Vector2(20, 96), new Vector2(520, 220), "DEBUG");
        _mapForm = new GeneratorPanel(newGameMode: false) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _mapForm.Confirmed += request => NewGameRequested?.Invoke(request);
        Content(_debugShell).AddChild(_mapForm);
    }

    private void BuildLlmView()
    {
        _llmPanel = CreatePanel(_llmView, new Vector2(20, 96), new Vector2(520, 120), "LLM");
        var placeholder = new Label { Text = "NO MODEL CONNECTED", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        StyleLabel(placeholder, DimColor);
        Content(_llmPanel).AddChild(placeholder);
    }

    private void Layout()
    {
        if (_root is null || _feedPanel is null) return;
        var size = _root.Size;
        var compact = size.X < 700;
        var stacked = size.X < 1030;
        var navigationWidth = stacked ? Math.Max(280, size.X - 40) : 430;
        var clockWidth = stacked ? Math.Max(320, size.X - 40) : 540;
        _navigationPanel.CustomMinimumSize = _navigationPanel.Size = new Vector2(navigationWidth, 76);
        _clockPanel.CustomMinimumSize = _clockPanel.Size = new Vector2(clockWidth, 76);
        _navigationPanel.Position = new Vector2(20, 12);
        _clockPanel.Position = stacked ? new Vector2(20, 96) : new Vector2(size.X - clockWidth - 20, 12);
        _brandLabel.Visible = !compact;
        _brandDivider.Visible = !compact;
        _clockText.CustomMinimumSize = new Vector2(compact ? 92 : 154, 0);
        _pauseButton.CustomMinimumSize = new Vector2(compact ? 46 : 56, 30);
        foreach (var button in _speedButtons.Values) button.CustomMinimumSize = new Vector2(compact ? 34 : 40, 30);
        var narrow = size.X < 700;
        var bottomPanelWidth = narrow ? Math.Max(100, (size.X - 60) * 0.5f) : 360;
        var carryWidth = narrow ? bottomPanelWidth : 260;
        var inputWidth = Math.Min(540, Math.Max(260, size.X - 40));
        var feedHeight = Math.Clamp(48 + _feedRows.Count * 15, 64, 166);
        _feedPanel.CustomMinimumSize = _feedPanel.Size = new Vector2(bottomPanelWidth, feedHeight);
        _carryPanel.CustomMinimumSize = _carryPanel.Size = new Vector2(carryWidth, 140);
        _inputPanel.CustomMinimumSize = _inputPanel.Size = new Vector2(inputWidth, 46);
        _inputPanel.Position = new Vector2(20, Math.Max(30, size.Y - _inputPanel.Size.Y - 12));
        _feedPanel.Position = new Vector2(20, Math.Max(90, _inputPanel.Position.Y - feedHeight - 8));
        _carryPanel.Position = new Vector2(Math.Max(20, size.X - carryWidth - 20), Math.Max(90, size.Y - 216));
        var activityY = stacked
            ? Math.Max(_clockPanel.Position.Y + _clockPanel.Size.Y + 12, size.Y - 320)
            : Math.Max(92, size.Y - 134);
        _activityPanel.Position = new Vector2(Mathf.Clamp(size.X * 0.5f - 140, 20, Math.Max(20, size.X - 300)), activityY);
        var contentY = stacked ? _clockPanel.Position.Y + _clockPanel.Size.Y + 12 : 96;
        var contentWidth = Math.Min(520, Math.Max(280, size.X - 40));
        _debugShell.Position = new Vector2(20, contentY);
        _debugShell.CustomMinimumSize = _debugShell.Size = new Vector2(contentWidth, Math.Clamp(_mapForm.Size.Y + 46, 220, Math.Max(220, size.Y - contentY - 20)));
        _llmPanel.Position = new Vector2(20, contentY);
        _llmPanel.CustomMinimumSize = _llmPanel.Size = new Vector2(contentWidth, 120);
        _fpsLabel.Position = new Vector2(size.X - _fpsLabel.CustomMinimumSize.X - 12, _clockPanel.Position.Y + _clockPanel.Size.Y + 7);
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
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(32, 28) };
        ButtonStyle(button, TextColor, compact: true);
        button.Pressed += callback;
        return button;
    }

    private static PanelContainer CreatePanel(Control parent, Vector2 position, Vector2 size, string title)
    {
        var panel = new PanelContainer { Position = position, Size = size, CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Pass };
        panel.AddThemeStyleboxOverride("panel", PanelFrame());
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 5);
        if (!string.IsNullOrEmpty(title))
        {
            var heading = new HBoxContainer { CustomMinimumSize = new Vector2(0, 15) };
            heading.AddThemeConstantOverride("separation", 7);
            heading.AddChild(new ColorRect
            {
                Color = CyanColor,
                CustomMinimumSize = new Vector2(2, 10),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            var label = new Label { Text = title, VerticalAlignment = VerticalAlignment.Center };
            StyleLabel(label, CyanColor, true);
            label.AddThemeFontSizeOverride("font_size", 11);
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
        label.AddThemeFontSizeOverride("font_size", bold ? 13 : 12);
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
        button.AddThemeFontSizeOverride("font_size", compact ? 11 : 12);
    }

    private static void StyleInput(LineEdit input)
    {
        input.AddThemeColorOverride("font_color", TextColor);
        input.AddThemeColorOverride("font_placeholder_color", DimColor);
        input.AddThemeColorOverride("caret_color", CyanColor);
        input.AddThemeColorOverride("selection_color", new Color("#24495c"));
        input.AddThemeStyleboxOverride("normal", Box("#10151b", "#303a44", 1));
        input.AddThemeStyleboxOverride("focus", Box("#101820", "#57c7ff", 1));
        input.AddThemeFontSizeOverride("font_size", 12);
    }

    private static StyleBoxFlat PanelFrame()
    {
        var frame = Box("#0b1016", "#35414c", 0.96f);
        frame.BorderWidthLeft = 2;
        frame.ContentMarginLeft = 10;
        frame.ContentMarginTop = 7;
        frame.ContentMarginRight = 10;
        frame.ContentMarginBottom = 7;
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
            ContentMarginLeft = 8,
            ContentMarginTop = 4,
            ContentMarginRight = 8,
            ContentMarginBottom = 4,
            CornerRadiusBottomLeft = 0,
            CornerRadiusBottomRight = 0,
            CornerRadiusTopLeft = 0,
            CornerRadiusTopRight = 0,
        };
    }
}
