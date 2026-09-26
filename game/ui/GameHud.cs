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
    private static readonly Color DimColor = new("#6a6d78");
    private static readonly Color CyanColor = new("#57c7ff");
    private static readonly Color GreenColor = new("#5af78e");
    private static readonly Color YellowColor = new("#f3f99d");
    private static readonly Color RedColor = new("#ff5c57");
    private readonly Queue<Label> _feedRows = new();
    private Control _root = null!;
    private Control _gameView = null!, _debugView = null!, _llmView = null!;
    private PanelContainer _clockPanel = null!, _feedPanel = null!, _carryPanel = null!, _activityPanel = null!;
    private PanelContainer _debugShell = null!, _llmPanel = null!;
    private HBoxContainer _clockRow = null!;
    private Label _clockText = null!, _carryText = null!, _activityText = null!;
    private ProgressBar _activityProgress = null!;
    private VBoxContainer _feedRowsContainer = null!;
    private LineEdit _input = null!;
    private Button _pauseButton = null!;
    private GeneratorPanel _newGame = null!, _mapForm = null!;
    private WorldFrame? _frame;

    public GameHud()
    {
        Layer = 2;
        ActionMenus = new ActionMenuOverlay();
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
        _root = new Control { Name = "HudRoot" };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_root);
        BuildViews();
        BuildGamePanels();
        BuildDebugView();
        BuildLlmView();
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
        _clockText.Text = $"DAY {day} · {minute / 60:00}:{minute % 60:00}";
        _clockText.AddThemeColorOverride("font_color", frame.Paused ? YellowColor : GreenColor);
        _pauseButton.Text = frame.Paused ? "RESUME" : "II";

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
        label.AddThemeFontSizeOverride("font_size", 11);
        _feedRowsContainer.AddChild(label);
        _feedRows.Enqueue(label);
        while (_feedRows.Count > 8)
        {
            var oldest = _feedRows.Dequeue();
            _feedRowsContainer.RemoveChild(oldest);
            oldest.QueueFree();
        }
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
        var tabs = new HBoxContainer { Position = new Vector2(20, 0), Size = new Vector2(300, 30) };
        foreach (var view in new[] { "GAME", "DEBUG", "LLM" })
        {
            var button = new Button { Text = view, CustomMinimumSize = new Vector2(82, 29) };
            ButtonStyle(button, CyanColor, compact: true);
            button.Pressed += () => SwitchView(view);
            tabs.AddChild(button);
        }
        _root.AddChild(tabs);

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
        _clockPanel = CreatePanel(_root, new Vector2(20, 36), new Vector2(470, 48), "CLOCK");
        _clockRow = new HBoxContainer();
        var clockContent = Content(_clockPanel);
        _clockText = new Label { Text = "DAY 1 · 00:00", CustomMinimumSize = new Vector2(142, 0) };
        StyleLabel(_clockText, GreenColor, true);
        _clockRow.AddChild(_clockText);
        _pauseButton = AddMiniButton("II", () => PauseRequested?.Invoke(!(_frame?.Paused ?? false)));
        _clockRow.AddChild(_pauseButton);
        foreach (var speed in new[] { 1, 3, 10 }) _clockRow.AddChild(AddMiniButton($"x{speed}", () => SpeedRequested?.Invoke(speed)));
        _clockRow.AddChild(AddMiniButton("NEW", ShowNewGame));
        clockContent.AddChild(_clockRow);

        _feedPanel = CreatePanel(_gameView, new Vector2(20, 0), new Vector2(360, 166), "FEED");
        _feedRowsContainer = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Content(_feedPanel).AddChild(_feedRowsContainer);

        _activityPanel = CreatePanel(_gameView, new Vector2(400, 0), new Vector2(280, 68), "ACT");
        var activityColumn = Content(_activityPanel);
        _activityText = new Label { Text = "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        StyleLabel(_activityText, TextColor);
        activityColumn.AddChild(_activityText);
        _activityProgress = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, CustomMinimumSize = new Vector2(250, 7), ShowPercentage = false };
        activityColumn.AddChild(_activityProgress);
        _activityPanel.Visible = false;

        _carryPanel = CreatePanel(_gameView, new Vector2(0, 0), new Vector2(260, 140), "CARRY");
        _carryText = new Label { Text = "L  —\nR  —\nBACK  —\nLOAD  0.0 kg" };
        StyleLabel(_carryText, TextColor);
        Content(_carryPanel).AddChild(_carryText);

        var inputPanel = CreatePanel(_gameView, new Vector2(20, 0), new Vector2(540, 42), "");
        var inputRow = new HBoxContainer();
        var prompt = new Label { Text = ">", CustomMinimumSize = new Vector2(16, 0) };
        StyleLabel(prompt, GreenColor, true);
        inputRow.AddChild(prompt);
        _input = new LineEdit { PlaceholderText = "say or try anything", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
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
        _feedPanel.Position = new Vector2(20, Math.Max(70, size.Y - 216));
        _carryPanel.Position = new Vector2(Math.Max(20, size.X - 280), Math.Max(70, size.Y - 216));
        _inputPanel.Position = new Vector2(20, Math.Max(30, size.Y - 42));
        _activityPanel.Position = new Vector2(Mathf.Clamp(size.X * 0.5f - 140, 20, size.X - 300), Math.Max(92, size.Y - 118));
        _clockPanel.Position = new Vector2(20, 36);
        _debugShell.Position = new Vector2(20, 96);
        _debugShell.Size = new Vector2(520, Math.Clamp(_mapForm.Size.Y + 46, 220, Math.Max(220, size.Y - 116)));
        _llmPanel.Position = new Vector2(20, 96);
        PositionNewGame();
    }

    private void PositionNewGame()
    {
        if (_newGame is null || _root is null) return;
        _newGame.Position = (_root.Size - _newGame.Size) * 0.5f;
    }

    private void UpdateTabStyles()
    {
        var tabs = _root?.GetChildren().OfType<HBoxContainer>().FirstOrDefault();
        if (tabs is null) return;
        foreach (var button in tabs.GetChildren().OfType<Button>())
        {
            var active = button.Text == ActiveView;
            button.AddThemeColorOverride("font_color", active ? CyanColor : DimColor);
            button.AddThemeStyleboxOverride("normal", Box("#121318", active ? "#3a3d47" : "#22242b", 0.9f));
        }
    }

    private Button AddMiniButton(string text, Action callback)
    {
        var button = new Button { Text = text };
        ButtonStyle(button, TextColor, compact: true);
        button.Pressed += callback;
        return button;
    }

    private static PanelContainer CreatePanel(Control parent, Vector2 position, Vector2 size, string title)
    {
        var panel = new PanelContainer { Position = position, Size = size, CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Pass };
        panel.AddThemeStyleboxOverride("panel", Box("#0c0d10", "#22242b", 0.9f));
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        if (!string.IsNullOrEmpty(title))
        {
            var heading = new Label { Text = $"▍ {title}", CustomMinimumSize = new Vector2(0, 18) };
            StyleLabel(heading, DimColor, true);
            body.AddChild(heading);
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
        label.AddThemeFontSizeOverride("font_size", bold ? 12 : 11);
    }

    private static void ButtonStyle(Button button, Color color, bool compact)
    {
        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_disabled_color", new Color("#3a3d47"));
        button.AddThemeColorOverride("font_hover_color", new Color("#07080a"));
        button.AddThemeStyleboxOverride("normal", Box("#0c0d10", "#3a3d47", 0.86f));
        button.AddThemeStyleboxOverride("hover", Box("#57c7ff", "#57c7ff", 1));
        button.AddThemeStyleboxOverride("disabled", Box("#0c0d10", "#22242b", 0.55f));
        button.AddThemeFontSizeOverride("font_size", compact ? 10 : 12);
    }

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
