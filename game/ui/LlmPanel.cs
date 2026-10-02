using System.Collections.Generic;
using System.Linq;
using EtherBound.Llm;
using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The LLM tab (`ALT+3`): what each model role runs with, what it has cost this session and the last calls. The model,
/// the reasoning flag and the idle timeout of the narrator are edited here and apply to the next call with no restart;
/// keys are never shown or edited, only reported as set or not. What each row says comes from `LlmTabModel`.
/// </summary>
public partial class LlmPanel : PanelContainer
{
    private const double RefreshSeconds = 0.5;

    private readonly LlmRuntime? _runtime;
    private readonly Dictionary<string, Label> _values = new();
    private readonly List<Label> _calls = new();
    private Label _hints = null!;
    private readonly Dictionary<KeyKind, Label> _keyNotes = new();
    private readonly Dictionary<KeyKind, System.Threading.Tasks.Task<string>> _tests = new();
    private readonly List<Control> _keyControls = new();
    private LineEdit _model = null!, _idle = null!;
    private Button _reasoning = null!;
    private double _sinceRefresh = RefreshSeconds;

    public LlmPanel(LlmRuntime? runtime)
    {
        _runtime = runtime;
        Name = "LlmView";
        MouseFilter = MouseFilterEnum.Pass;
        AddThemeStyleboxOverride("panel", HudTheme.PanelFrame(modal: true));
    }

    public override void _Ready()
    {
        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", HudTheme.S(10));
        var title = new Label { Text = "LLM" };
        HudTheme.Label(title, HudTheme.Cyan, HudTheme.TitleUnits);
        page.AddChild(title);

        page.AddChild(HudTheme.Heading("FREE TEXT · JEV"));
        page.AddChild(Row("STATE", "freetext"));
        page.AddChild(Row("KEY", "jevkey"));
        page.AddChild(KeyEditor(KeyKind.Jev));
        page.AddChild(Row("MODEL", "jevmodel"));
        page.AddChild(Row("USE", "jevspend"));

        page.AddChild(HudTheme.Heading("NARRATOR · OPENROUTER"));
        page.AddChild(Row("STATE", "narrator"));
        page.AddChild(Row("KEY", "chatkey"));
        page.AddChild(KeyEditor(KeyKind.OpenRouter));
        page.AddChild(Row("MODEL FROM", "source"));
        page.AddChild(ModelRow());
        page.AddChild(ReasoningRow());
        page.AddChild(IdleRow());
        page.AddChild(Row("USE", "narratorspend"));

        page.AddChild(HudTheme.Heading("SESSION"));
        page.AddChild(Row("SPENT", "total"));
        for (var i = 0; i < 5; i++)
        {
            var call = new Label { ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            HudTheme.Label(call, HudTheme.Dim, HudTheme.SmallUnits);
            _calls.Add(call);
            page.AddChild(call);
        }

        _hints = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        HudTheme.Label(_hints, HudTheme.Yellow, HudTheme.SmallUnits);
        page.AddChild(_hints);
        AddChild(page);
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        foreach (var (kind, test) in _tests.ToArray())
            if (test.IsCompleted)
            {
                _keyNotes[kind].Text = test.IsCompletedSuccessfully ? test.Result : "TEST FAILED · THE CALL DID NOT FINISH";
                _tests.Remove(kind);
            }
        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshSeconds) return;
        _sinceRefresh = 0;
        Refresh();
    }

    private Control Row(string label, string key)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", HudTheme.S(12));
        var name = new Label { Text = label, CustomMinimumSize = HudTheme.V(260, 0) };
        HudTheme.Label(name, HudTheme.LineHi, HudTheme.SmallUnits);
        row.AddChild(name);
        var value = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        HudTheme.Label(value, HudTheme.Text, HudTheme.SmallUnits);
        _values[key] = value;
        row.AddChild(value);
        return row;
    }

    /// <summary>
    /// A write-only key box (Dev-009): masked while typed, cleared the moment it is saved or removed, never filled
    /// from the runtime, so no key can come back out of the HUD. Enter saves.
    /// </summary>
    private Control KeyEditor(KeyKind kind)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", HudTheme.S(12));
        var name = new Label { Text = "PASTE KEY", CustomMinimumSize = HudTheme.V(260, 0) };
        HudTheme.Label(name, HudTheme.LineHi, HudTheme.SmallUnits);
        row.AddChild(name);
        var box = new LineEdit { Secret = true, PlaceholderText = "paste, then Enter or SAVE", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        HudTheme.Input(box);
        row.AddChild(box);
        var note = new Label { CustomMinimumSize = HudTheme.V(420, 0), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        HudTheme.Label(note, HudTheme.Yellow, HudTheme.SmallUnits);
        _keyNotes[kind] = note;

        void Save()
        {
            var text = box.Text;
            box.Text = "";
            box.ReleaseFocus();
            if (_runtime is null) return;
            note.Text = LlmTabModel.SaveKey(_runtime, kind, text);
            Refresh(force: true);
        }

        box.TextSubmitted += _ => Save();
        var save = new Button { Text = "SAVE" };
        HudTheme.Button(save, HudTheme.Green);
        save.Pressed += Save;
        var remove = new Button { Text = "REMOVE" };
        HudTheme.Button(remove, HudTheme.Dim);
        remove.Pressed += () =>
        {
            box.Text = "";
            if (_runtime is null) return;
            note.Text = LlmTabModel.RemoveKey(_runtime, kind);
            Refresh(force: true);
        };
        var test = new Button { Text = "TEST" };
        HudTheme.Button(test, HudTheme.Cyan);
        test.Pressed += () =>
        {
            if (_runtime is null || _tests.ContainsKey(kind)) return;
            note.Text = "TESTING...";
            var runtime = _runtime;
            _tests[kind] = System.Threading.Tasks.Task.Run(() => LlmTabModel.TestKey(runtime, kind));
        };
        row.AddChild(save);
        row.AddChild(remove);
        row.AddChild(test);
        row.AddChild(note);
        _keyControls.Add(box);
        _keyControls.Add(save);
        _keyControls.Add(remove);
        _keyControls.Add(test);
        return row;
    }

    private Control ModelRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", HudTheme.S(12));
        var name = new Label { Text = "MODEL ID", CustomMinimumSize = HudTheme.V(260, 0) };
        HudTheme.Label(name, HudTheme.LineHi, HudTheme.SmallUnits);
        row.AddChild(name);
        _model = new LineEdit { PlaceholderText = "provider/model, then Enter", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        HudTheme.Input(_model);
        _model.TextSubmitted += text =>
        {
            if (_runtime is not null) LlmTabModel.ApplyModel(_runtime, text);
            _model.ReleaseFocus();
            Refresh();
        };
        row.AddChild(_model);
        var clear = new Button { Text = "CLEAR CHOICES" };
        HudTheme.Button(clear, HudTheme.Dim);
        clear.Pressed += () =>
        {
            if (_runtime is not null) LlmTabModel.ClearChoices(_runtime);
            _model.Text = "";
            Refresh(force: true);
        };
        row.AddChild(clear);
        return row;
    }

    private Control ReasoningRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", HudTheme.S(12));
        var name = new Label { Text = "REASONING", CustomMinimumSize = HudTheme.V(260, 0) };
        HudTheme.Label(name, HudTheme.LineHi, HudTheme.SmallUnits);
        row.AddChild(name);
        _reasoning = new Button { ToggleMode = true };
        _reasoning.Toggled += on =>
        {
            if (_runtime is not null) LlmTabModel.ApplyReasoning(_runtime, on);
            Refresh(force: true);
        };
        row.AddChild(_reasoning);
        var note = new Label { Text = "KEEP IT OFF FOR A MODEL THAT DOES NOT REASON: IT TURNS THREE TIMES SLOWER" };
        HudTheme.Label(note, HudTheme.Dim, HudTheme.TinyUnits);
        row.AddChild(note);
        return row;
    }

    private Control IdleRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", HudTheme.S(12));
        var name = new Label { Text = "IDLE TIMEOUT (S)", CustomMinimumSize = HudTheme.V(260, 0) };
        HudTheme.Label(name, HudTheme.LineHi, HudTheme.SmallUnits);
        row.AddChild(name);
        _idle = new LineEdit { CustomMinimumSize = HudTheme.V(160, 0) };
        HudTheme.Input(_idle);
        _idle.TextSubmitted += text =>
        {
            if (_runtime is not null) LlmTabModel.ApplyIdleSeconds(_runtime, text);
            _idle.ReleaseFocus();
            Refresh(force: true);
        };
        row.AddChild(_idle);
        var note = new Label { Text = "RESETS ON EVERY STREAMED CHUNK" };
        HudTheme.Label(note, HudTheme.Dim, HudTheme.TinyUnits);
        row.AddChild(note);
        return row;
    }

    private void Refresh(bool force = false)
    {
        var view = LlmTabModel.Build(_runtime);
        Set("freetext", view.FreeText, view.JevKey ? HudTheme.Green : HudTheme.Yellow);
        Set("jevmodel", view.JevModel);
        Set("jevspend", view.JevSpend);
        Set("narrator", view.NarratorState, view.NarratorState == "READY" ? HudTheme.Green : HudTheme.Yellow);
        Set("jevkey", view.JevKeyStatus, view.JevKey ? HudTheme.Green : HudTheme.Yellow);
        Set("chatkey", view.ChatKeyStatus, view.ChatKey ? HudTheme.Green : HudTheme.Yellow);
        foreach (var control in _keyControls)
        {
            if (control is LineEdit edit) edit.Editable = view.CanEditKeys;
            else if (control is Button button) button.Disabled = !view.CanEditKeys;
        }
        Set("source", view.ModelSource);
        Set("narratorspend", view.NarratorSpend);
        Set("total", view.Total);
        // A box the player is typing in is theirs until they leave it.
        if (force || !_model.HasFocus()) _model.Text = view.NarratorModel == "(none)" ? "" : view.NarratorModel;
        if (force || !_idle.HasFocus()) _idle.Text = view.IdleSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _reasoning.SetPressedNoSignal(view.Reasoning);
        _reasoning.Text = view.Reasoning ? "ON" : "OFF";
        HudTheme.Segment(_reasoning, view.Reasoning ? HudTheme.Yellow : HudTheme.Dim, view.Reasoning);
        for (var i = 0; i < _calls.Count; i++) _calls[i].Text = i < view.Calls.Count ? view.Calls[i] : (i == 0 ? "NO CALLS YET" : "");
        _hints.Text = string.Join("\n", view.Hints);
    }

    private void Set(string key, string text, Color? color = null)
    {
        _values[key].Text = text;
        _values[key].AddThemeColorOverride("font_color", color ?? HudTheme.Text);
    }
}
