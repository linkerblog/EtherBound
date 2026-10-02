using EtherBound.Host;
using EtherBound.Llm;
using EtherBound.Sim.Core;

namespace EtherBound.Game.App;

/// <summary>
/// What the player's free-text line means on the client (Dev-007): a fresh line to send to Jev, the answer to a
/// pending "did you mean", or nothing. It decides presentation only; every action it releases goes to the
/// host like a menu click, and the engine validates it.
/// </summary>
public sealed class FreeTextFlow
{
    public enum Step { Nothing, Ask, Confirmed, Dropped, Unavailable }

    public sealed record Pending(GameAction Action, string Label);

    public sealed record Submission(Step Step, string Text = "", Pending? Confirmed = null);

    public sealed record FeedLine(string Text, string Category);

    public Pending? Confirmation { get; private set; }

    public void Reset() => Confirmation = null;

    /// <summary>
    /// Enter on the free-text box. With a question pending, empty or "yes" says yes and "no" says no; any
    /// other text drops the question and is read as a new line.
    /// </summary>
    public Submission Submit(string raw, bool available)
    {
        var text = raw.Trim();
        if (Confirmation is { } pending)
        {
            Confirmation = null;
            var word = text.ToLowerInvariant();
            if (word.Length == 0 || word is "y" or "yes" or "si" or "sí") return new Submission(Step.Confirmed, Confirmed: pending);
            if (word is "n" or "no") return new Submission(Step.Dropped);
        }
        if (text.Length == 0) return new Submission(Step.Nothing);
        return available ? new Submission(Step.Ask, text) : new Submission(Step.Unavailable, text);
    }

    public static FeedLine EchoOf(string text) => new($"> {text}", "info");

    public static FeedLine UnavailableLine() => new("FREE TEXT NEEDS A JEV KEY · SEE THE LLM TAB", "warn");

    public static FeedLine ConfirmedLine(Pending pending) => new($"-> {pending.Label.ToUpperInvariant()}", "seen");

    public static FeedLine DroppedLine() => new("DROPPED", "seen");

    /// <summary>The feed line for the host's answer; a <c>Confirm</c> also arms the pending question.</summary>
    public FeedLine Present(HostInterpretResponse response)
    {
        var label = response.Label.ToUpperInvariant();
        switch (response.Kind)
        {
            case InterpretKind.Accept:
                Confirmation = null;
                return new FeedLine($"-> {label}", "seen");
            case InterpretKind.Confirm when response.Action is { } action:
                Confirmation = new Pending(action, response.Label);
                return new FeedLine($"DID YOU MEAN: {label}? ENTER = YES", "seen");
            case InterpretKind.Blocked:
                Confirmation = null;
                return new FeedLine($"CAN'T {label} · {response.Message.ToUpperInvariant()}", "warn");
            default:
                Confirmation = null;
                return new FeedLine(response.Message.ToUpperInvariant(), "warn");
        }
    }
}
