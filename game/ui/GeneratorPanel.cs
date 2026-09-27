using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using EtherBound.Host;
using Godot;

namespace EtherBound.Game.Ui;

public sealed record NewGameRequest(long Seed, string Generator, string OptionsJson, bool Paused);

public partial class GeneratorPanel : PanelContainer
{
    private static readonly Color TextColor = new("#d6d8de");
    private static readonly Color MutedColor = new("#929daa");
    private static readonly Color AccentColor = new("#57c7ff");
    private readonly bool _newGameMode;
    private readonly List<HostGenerator> _generators = new();
    private readonly Dictionary<string, (HostOptionField Field, Control Row, Control Editor)> _editors = new(StringComparer.Ordinal);
    private OptionButton _generator = null!;
    private SpinBox _seed = null!;
    private Label _version = null!;
    private Label _bays = null!;
    private VBoxContainer _fields = null!;
    private Label _error = null!;
    private string? _selectedKey;
    private long _currentSeed;
    private bool _paused;

    public GeneratorPanel(bool newGameMode)
    {
        _newGameMode = newGameMode;
        Visible = !newGameMode;
        CustomMinimumSize = new Vector2(440, 0);
        AddThemeStyleboxOverride("panel", PanelStyle());
    }

    public event Action<NewGameRequest>? Confirmed;
    public event Action? Cancelled;

    public string SelectedGenerator => _generators.Count == 0 ? "test" : _generators[Math.Max(0, _generator.Selected)].Key;

    public override void _Ready()
    {
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 7);
        AddChild(body);
        var title = new Label { Text = _newGameMode ? "NEW WORLD" : "MAP GENERATOR" };
        StyleLabel(title, AccentColor);
        title.AddThemeFontSizeOverride("font_size", 14);
        body.AddChild(title);
        body.AddChild(new ColorRect
        {
            Color = new Color("#25313b"),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var generatorRow = new HBoxContainer();
        generatorRow.AddChild(FieldLabel("GENERATOR"));
        _generator = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        StyleOptionButton(_generator);
        _generator.ItemSelected += _ => SelectGenerator();
        generatorRow.AddChild(_generator);
        body.AddChild(generatorRow);
        _version = new Label { Text = "" };
        StyleLabel(_version, MutedColor);
        body.AddChild(_version);
        _bays = new Label { Text = "" };
        StyleLabel(_bays, MutedColor);
        body.AddChild(_bays);

        if (_newGameMode)
        {
            var seedRow = new HBoxContainer();
            seedRow.AddChild(FieldLabel("SEED"));
            _seed = new SpinBox { MinValue = 0, MaxValue = int.MaxValue, Step = 1, Rounded = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            StyleSpinBox(_seed);
            seedRow.AddChild(_seed);
            var randomize = new Button { Text = "RANDOMIZE" };
            StyleButton(randomize);
            randomize.Pressed += RandomizeSeed;
            seedRow.AddChild(randomize);
            body.AddChild(seedRow);
        }

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _fields = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_fields);
        body.AddChild(scroll);
        _error = new Label { Text = "" };
        StyleLabel(_error, new Color("#ff5c57"));
        body.AddChild(_error);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var cancel = new Button { Text = "CANCEL" };
        StyleButton(cancel);
        cancel.Pressed += () =>
        {
            if (_newGameMode) Visible = false;
            Cancelled?.Invoke();
        };
        if (_newGameMode) buttons.AddChild(cancel);
        var apply = new Button { Text = _newGameMode ? "NEW" : "REGENERATE", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        StyleButton(apply, primary: true);
        apply.Pressed += Confirm;
        buttons.AddChild(apply);
        body.AddChild(buttons);
    }

    public void UpdateFrame(WorldFrame frame)
    {
        var keys = frame.Generators.Select(spec => spec.Key).ToArray();
        var changed = !_generators.Select(spec => spec.Key).SequenceEqual(keys, StringComparer.Ordinal);
        if (changed)
        {
            _generators.Clear();
            _generators.AddRange(frame.Generators);
            _generator.Clear();
            foreach (var spec in _generators) _generator.AddItem(spec.Name);
            if (_generators.Count == 0) return;
            _generator.Selected = Math.Max(0, _generators.FindIndex(spec => spec.Key == frame.Generator));
            SelectGenerator();
            if (_selectedKey == frame.Generator) LoadOptions(frame.GenOptionsJson);
        }
        _currentSeed = frame.Seed;
        _paused = frame.Paused;
        if (_newGameMode && !IsVisibleInTree() && !_seed.Value.Equals((double)frame.Seed)) _seed.Value = frame.Seed;
        if (!changed && _selectedKey != frame.Generator && !IsVisibleInTree())
        {
            _generator.Selected = Math.Max(0, _generators.FindIndex(spec => spec.Key == frame.Generator));
            SelectGenerator();
            LoadOptions(frame.GenOptionsJson);
        }
        else if (!changed && !IsVisibleInTree() && _selectedKey == frame.Generator)
        {
            LoadOptions(frame.GenOptionsJson);
        }
    }

    public void OpenNew()
    {
        if (!_newGameMode) return;
        Visible = true;
        _error.Text = "";
        if (_seed is not null && !_seed.HasFocus()) _seed.Value = _currentSeed;
        SelectGenerator();
    }

    public void CloseNew()
    {
        if (_newGameMode) Visible = false;
    }

    public void ShowError(string message) => _error.Text = message.ToUpperInvariant();

    private void SelectGenerator()
    {
        if (_generator.Selected < 0 || _generator.Selected >= _generators.Count) return;
        var spec = _generators[_generator.Selected];
        _selectedKey = spec.Key;
        _version.Text = $"{spec.Name.ToUpperInvariant()}  ·  GENERATOR V{spec.Version}" +
            (spec.Bays.IsDefaultOrEmpty ? "" : $"  ·  {spec.Bays.Length} BAYS");
        _bays.Text = spec.Bays.IsDefaultOrEmpty ? "" : string.Join("  ·  ", spec.Bays.Select(bay => bay.Key.ToUpperInvariant()));
        BuildFields(spec);
    }

    private void BuildFields(HostGenerator spec)
    {
        foreach (var child in _fields.GetChildren())
        {
            _fields.RemoveChild(child);
            child.QueueFree();
        }
        _editors.Clear();
        foreach (var field in spec.Fields)
        {
            var row = new HBoxContainer();
            row.AddChild(FieldLabel(field.Label.ToUpperInvariant()));
            Control editor;
            if (field.Kind == "choice")
            {
                var choices = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                StyleOptionButton(choices);
                foreach (var choice in field.Choices) choices.AddItem(choice);
                choices.Selected = Math.Max(0, FindChoice(field, field.DefaultJson));
                choices.ItemSelected += _ => UpdateFieldVisibility();
                editor = choices;
            }
            else
            {
                var value = ReadNumber(field.DefaultJson);
                var spin = new SpinBox
                {
                    MinValue = field.Minimum ?? 0,
                    MaxValue = field.Maximum ?? int.MaxValue,
                    Step = field.Step ?? (field.Kind == "int" ? 1 : 0.1),
                    Rounded = field.Kind == "int",
                    Value = value,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                StyleSpinBox(spin);
                editor = spin;
            }
            row.AddChild(editor);
            _fields.AddChild(row);
            _editors[field.Path] = (field, row, editor);
        }
        UpdateFieldVisibility();
    }

    private void UpdateFieldVisibility()
    {
        var feature = _editors.TryGetValue("feature", out var selection) && selection.Editor is OptionButton choice && choice.Selected >= 0
            ? choice.GetItemText(choice.Selected) : null;
        foreach (var (field, row, _) in _editors.Values)
            row.Visible = field.Group is null || field.Group == feature;
        var visibleFields = _editors.Values.Count(editor => editor.Row.Visible);
        var height = (_newGameMode ? 205 : 165) + visibleFields * 38;
        Size = CustomMinimumSize = new Vector2(440, Mathf.Clamp(height, 200, 520));
    }

    private void LoadOptions(string json)
    {
        JsonObject? options;
        try { options = JsonNode.Parse(json) as JsonObject; }
        catch (System.Text.Json.JsonException) { return; }
        if (options is null) return;
        foreach (var (field, _, editor) in _editors.Values)
        {
            var node = GetPath(options, field.Path);
            if (node is null) continue;
            if (editor is OptionButton choice && node is JsonValue choiceValue && choiceValue.TryGetValue<string>(out var selected))
            {
                var index = field.Choices.IndexOf(selected);
                if (index >= 0) choice.Selected = index;
            }
            else if (editor is SpinBox spin && TryNumber(node, out var number)) spin.Value = number;
        }
        UpdateFieldVisibility();
    }

    private static JsonNode? GetPath(JsonObject root, string path)
    {
        JsonNode? value = root;
        foreach (var part in path.Split('.')) value = (value as JsonObject)?[part];
        return value;
    }

    private void Confirm()
    {
        if (_generators.Count == 0) return;
        _error.Text = "";
        var options = new JsonObject();
        foreach (var (field, _, editor) in _editors.Values) SetPath(options, field.Path, Value(field, editor));
        var seed = _newGameMode ? (long)_seed.Value : _currentSeed;
        Confirmed?.Invoke(new NewGameRequest(seed, SelectedGenerator, options.ToJsonString(), _paused));
    }

    private void RandomizeSeed()
    {
        var random = new RandomNumberGenerator();
        random.Randomize();
        _seed.Value = random.RandiRange(0, int.MaxValue);
    }

    private static JsonNode Value(HostOptionField field, Control editor) => editor switch
    {
        OptionButton choice => JsonValue.Create(choice.Selected >= 0 ? choice.GetItemText(choice.Selected) : "")!,
        SpinBox number when field.Kind == "int" => JsonValue.Create((int)number.Value)!,
        SpinBox number => JsonValue.Create(number.Value)!,
        _ => JsonNode.Parse(field.DefaultJson ?? "null") ?? JsonValue.Create((string?)null)!,
    };

    private static void SetPath(JsonObject root, string path, JsonNode value)
    {
        var parts = path.Split('.');
        var current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is not JsonObject child) current[parts[i]] = child = new JsonObject();
            current = child;
        }
        current[parts[^1]] = value;
    }

    private static int FindChoice(HostOptionField field, string? defaultJson)
    {
        if (defaultJson is null) return 0;
        try
        {
            var value = JsonNode.Parse(defaultJson)?.GetValue<string>();
            return field.Choices.IndexOf(value ?? "");
        }
        catch (System.Text.Json.JsonException) { return 0; }
    }

    private static double ReadNumber(string? value)
    {
        if (value is null) return 0;
        try
        {
            return JsonNode.Parse(value) is JsonValue parsed && TryNumber(parsed, out var result) ? result : 0;
        }
        catch (System.Text.Json.JsonException) { return 0; }
    }

    private static bool TryNumber(JsonNode node, out double number)
    {
        if (node is JsonValue value && value.TryGetValue<double>(out number)) return true;
        if (node is JsonValue integer && integer.TryGetValue<long>(out var whole))
        {
            number = whole;
            return true;
        }
        number = 0;
        return false;
    }

    private static Label FieldLabel(string text)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(112, 0) };
        StyleLabel(label, MutedColor);
        return label;
    }

    private static void StyleLabel(Label label, Color color)
    {
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 12);
    }

    private static void StyleOptionButton(OptionButton button)
    {
        StyleField(button);
        var popup = button.GetPopup();
        popup.AddThemeColorOverride("font_color", TextColor);
        popup.AddThemeColorOverride("font_hover_color", TextColor);
        popup.AddThemeStyleboxOverride("panel", FieldBox("#0b1016", "#35414c"));
    }

    private static void StyleSpinBox(SpinBox spin)
    {
        StyleField(spin);
        StyleField(spin.GetLineEdit());
    }

    private static void StyleField(Control control)
    {
        control.AddThemeColorOverride("font_color", TextColor);
        control.AddThemeColorOverride("font_hover_color", TextColor);
        control.AddThemeColorOverride("font_disabled_color", MutedColor);
        control.AddThemeStyleboxOverride("normal", FieldBox("#10151b", "#303a44"));
        control.AddThemeStyleboxOverride("hover", FieldBox("#141e26", "#506271"));
        control.AddThemeStyleboxOverride("focus", FieldBox("#101820", "#57c7ff"));
        control.AddThemeStyleboxOverride("disabled", FieldBox("#0b1015", "#29313a"));
        control.AddThemeFontSizeOverride("font_size", 12);
    }

    private static void StyleButton(Button button, bool primary = false)
    {
        button.AddThemeColorOverride("font_color", primary ? AccentColor : TextColor);
        button.AddThemeColorOverride("font_hover_color", TextColor);
        button.AddThemeColorOverride("font_pressed_color", new Color("#071117"));
        button.AddThemeColorOverride("font_disabled_color", MutedColor);
        button.AddThemeStyleboxOverride("normal", primary
            ? FieldBox("#12303c", "#57c7ff")
            : FieldBox("#10151b", "#303a44"));
        button.AddThemeStyleboxOverride("hover", FieldBox("#1a2b35", "#70d5ff"));
        button.AddThemeStyleboxOverride("pressed", FieldBox("#57c7ff", "#a5e6ff"));
        button.AddThemeStyleboxOverride("focus", FieldBox("#101820", "#57c7ff"));
        button.AddThemeStyleboxOverride("disabled", FieldBox("#0b1015", "#29313a"));
        button.AddThemeFontSizeOverride("font_size", 12);
    }

    private static StyleBoxFlat FieldBox(string background, string border) => new()
    {
        BgColor = new Color(background),
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

    private static StyleBoxFlat PanelStyle() => new()
    {
        BgColor = new Color("#0b1016"),
        BorderColor = new Color("#35414c"),
        BorderWidthLeft = 2,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        ContentMarginLeft = 12,
        ContentMarginTop = 10,
        ContentMarginRight = 12,
        ContentMarginBottom = 10,
        CornerRadiusBottomLeft = 0,
        CornerRadiusBottomRight = 0,
        CornerRadiusTopLeft = 0,
        CornerRadiusTopRight = 0,
    };
}
