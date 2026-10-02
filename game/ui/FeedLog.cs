using System.Collections.Generic;
using Godot;

namespace EtherBound.Game.Ui;

/// <summary>
/// The event feed in the footer slot: `HH:MM`, a category rule and the text, newest at the bottom.
/// The newest three rows are visible; the mouse wheel scrolls up through the last 100. Rows fade
/// with age and a new one steps in with a short brightness flash (`glitch-in`, STYLEGUIDE.md
/// [Sec. 11]), skipped when <see cref="HudTheme.Motion"/> is off.
/// </summary>
public partial class FeedLog : ScrollContainer
{
    internal sealed record Row(HBoxContainer Box, ColorRect Rule, Label Stamp, Label Text);

    private readonly VBoxContainer _column = new();
    private readonly List<Row> _rows = new();

    public FeedLog()
    {
        Name = "FeedLog";
        HorizontalScrollMode = ScrollMode.Disabled;
        VerticalScrollMode = ScrollMode.ShowNever;
        MouseFilter = MouseFilterEnum.Pass;
        _column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _column.AddThemeConstantOverride("separation", 0);
        AddChild(_column);
    }

    public int RowCount => _rows.Count;

    /// <summary>A pushed row the caller may still change: narration grows while the model writes it (Dev-008).</summary>
    public sealed class Handle
    {
        private readonly FeedLog _log;
        private readonly Row _row;

        internal Handle(FeedLog log, Row row) => (_log, _row) = (log, row);

        public void SetText(string text)
        {
            if (!GodotObject.IsInstanceValid(_row.Text)) return;
            var atBottom = _log.IsNearBottom();
            _row.Text.Text = text;
            if (atBottom) _log.CallDeferred(nameof(ScrollToEnd));
        }

        public void Remove() => _log.Drop(_row);
    }

    /// <summary>`wrap` lets a long line, such as narration, use as many lines as it needs instead of trimming to one.</summary>
    public Handle Push(int gameMinute, string text, string category, bool animate = true, bool wrap = false)
    {
        var atBottom = IsNearBottom();
        var tone = FeedFormat.ToneOf(category);
        var color = ToneColor(tone);

        var box = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", HudTheme.S(10));
        var rule = new ColorRect
        {
            Color = tone == FeedFormat.Tone.Plain ? HudTheme.LineHi : color,
            CustomMinimumSize = HudTheme.V(4, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddChild(rule);
        var stamp = new Label { Text = FeedFormat.Stamp(gameMinute), MouseFilter = MouseFilterEnum.Ignore };
        HudTheme.Label(stamp, HudTheme.Dim, HudTheme.TinyUnits);
        box.AddChild(stamp);
        var label = new Label
        {
            Text = text,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = !wrap,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            TextOverrunBehavior = wrap ? TextServer.OverrunBehavior.NoTrimming : TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        HudTheme.Label(label, color, HudTheme.TinyUnits);
        box.AddChild(label);

        _column.AddChild(box);
        var added = new Row(box, rule, stamp, label);
        _rows.Add(added);
        while (FeedFormat.Overflow(_rows.Count) > 0)
        {
            _column.RemoveChild(_rows[0].Box);
            _rows[0].Box.QueueFree();
            _rows.RemoveAt(0);
        }
        Fade();
        if (atBottom) CallDeferred(nameof(ScrollToEnd));
        if (animate && HudTheme.Motion) StepIn(label);
        return new Handle(this, added);
    }

    private void Drop(Row row)
    {
        if (!_rows.Remove(row)) return;
        _column.RemoveChild(row.Box);
        row.Box.QueueFree();
        Fade();
    }

    /// <summary>Re-applies sizes after the HUD scale changed.</summary>
    public void Restyle()
    {
        _column.AddThemeConstantOverride("separation", 0);
        foreach (var row in _rows)
        {
            row.Box.AddThemeConstantOverride("separation", HudTheme.S(10));
            row.Rule.CustomMinimumSize = HudTheme.V(4, 0);
            row.Stamp.AddThemeFontSizeOverride("font_size", HudTheme.S(HudTheme.TinyUnits));
            row.Text.AddThemeFontSizeOverride("font_size", HudTheme.S(HudTheme.TinyUnits));
        }
        CallDeferred(nameof(ScrollToEnd));
    }

    private static Color ToneColor(FeedFormat.Tone tone) => tone switch
    {
        FeedFormat.Tone.Warn => HudTheme.Yellow,
        FeedFormat.Tone.Fail => HudTheme.Red,
        FeedFormat.Tone.Act => HudTheme.Dim,
        FeedFormat.Tone.Seen => HudTheme.Cyan,
        FeedFormat.Tone.Ether => HudTheme.Magenta,
        _ => HudTheme.Text,
    };

    private void Fade()
    {
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Box.Modulate = new Color(1, 1, 1, FeedFormat.Opacity(_rows.Count - 1 - i));
    }

    private bool IsNearBottom()
    {
        var bar = GetVScrollBar();
        return bar.MaxValue - (ScrollVertical + bar.Page) <= HudTheme.S(40);
    }

    public void ScrollToTop() => ScrollVertical = 0;

    /// <summary>Scrolls to the newest row once the layout has caught up; for a driver that pushed many rows in one frame.</summary>
    public void ScrollToBottom() => CallDeferred(nameof(ScrollToEnd));

    private void ScrollToEnd() => ScrollVertical = (int)GetVScrollBar().MaxValue;

    private static void StepIn(Label text)
    {
        var steps = new[]
        {
            new Color(1.9f, 1.9f, 1.9f, 0.35f),
            new Color(1.5f, 1.5f, 1.5f, 1f),
            new Color(1.2f, 1.2f, 1.2f, 0.85f),
            Colors.White,
        };
        text.Modulate = steps[0];
        var tween = text.CreateTween();
        for (var i = 1; i < steps.Length; i++)
        {
            var next = steps[i];
            tween.TweenInterval(0.085);
            tween.TweenCallback(Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(text)) text.Modulate = next;
            }));
        }
    }
}
