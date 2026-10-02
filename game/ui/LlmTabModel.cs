using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EtherBound.Llm;
using EtherBound.Llm.Narration;

namespace EtherBound.Game.Ui;

/// <summary>What the LLM tab shows, as plain text. It never carries a key, only whether one is set.</summary>
public sealed record LlmTabView(
    bool JevKey, string JevKeyStatus, string JevModel, string FreeText, string JevSpend,
    bool ChatKey, string ChatKeyStatus, bool CanEditKeys, string NarratorModel, string ModelSource, bool Reasoning, int IdleSeconds, string NarratorState, string NarratorSpend,
    string Total, IReadOnlyList<string> Calls, IReadOnlyList<string> Hints);

/// <summary>
/// The LLM tab's rules without nodes (Dev-008 D8): what each row reads from the runtime and how an edit is applied.
/// An edit goes to the role registry, which applies it to the next call and saves it to `llm.json`; keys are only ever
/// reported as set or not set. Asserted in `game.tests/LlmTabRules.cs`.
/// </summary>
public static class LlmTabModel
{
    public static LlmTabView Build(LlmRuntime? runtime)
    {
        if (runtime is null)
            return new LlmTabView(false, "NOT SET", "-", "OFF", "", false, "NOT SET", false, "-", "-", false, 0, "OFF · NO LLM LAYER", "", "", Array.Empty<string>(),
                new[] { "THIS BUILD HAS NO LLM LAYER" });

        var (role, source) = runtime.Roles.Describe(Narrator.Role);
        var jev = runtime.Spend.Of("jev");
        var narrator = runtime.Spend.Of(Narrator.Role);
        var hints = new List<string>();
        if (!runtime.Interpreter.Configured) hints.Add("FREE TEXT: PASTE A TYPESAFE KEY IN THE JEV SECTION AND SAVE IT");
        if (!runtime.ChatConfigured) hints.Add("NARRATOR: PASTE AN OPENROUTER KEY IN ITS SECTION AND SAVE IT");
        else if (!role.Enabled) hints.Add($"NARRATOR: TYPE A MODEL ID BELOW, OR SET {LlmConfig.RoleModelVariable(Narrator.Role)}");
        hints.Add(runtime.CanEditKeys
            ? $"A KEY SAVED HERE BEATS THE ENVIRONMENT, IS KEPT IN {LlmConfig.FileName} AND IS NEVER SHOWN AGAIN"
            : "THIS RUNTIME HAS NO USER FOLDER, SO KEYS CANNOT BE SAVED HERE");

        return new LlmTabView(
            runtime.Interpreter.Configured, KeyStatus(runtime.SourceOf(KeyKind.Jev)), runtime.Jev.Model,
            runtime.Interpreter.Configured ? "ON" : "OFF · NO JEV KEY", Spend(jev),
            runtime.ChatConfigured, KeyStatus(runtime.SourceOf(KeyKind.OpenRouter)), runtime.CanEditKeys, role.Enabled ? role.Model : "(none)", source.ToUpperInvariant(), role.Reasoning, role.IdleMs / 1000,
            NarratorState(runtime, role), Spend(narrator),
            $"{Usd(runtime.Spend.TotalUsd)} OF {Usd(runtime.Spend.CapUsd)} CAP · {runtime.Spend.Calls} CALLS",
            runtime.Spend.Recent(5).Select(Describe).ToArray(), hints);
    }

    public static string NarratorState(LlmRuntime runtime, RoleConfig role)
    {
        if (!runtime.ChatConfigured) return "OFF · NO OPENROUTER KEY";
        if (!role.Enabled) return "OFF · NO MODEL SET";
        return runtime.Spend.Capped ? "STOPPED · SPEND CAP REACHED" : "READY";
    }

    /// <summary>Where a key in use came from. Never the key, and not even its end.</summary>
    public static string KeyStatus(KeySource source) => source switch
    {
        KeySource.Saved => "SET · SAVED IN GAME",
        KeySource.Environment => "SET · FROM ENVIRONMENT",
        _ => "NOT SET",
    };

    /// <summary>Saves a pasted key; the answer is the line the tab shows next to the box, and it never contains the key.</summary>
    public static string SaveKey(LlmRuntime runtime, KeyKind kind, string text)
    {
        var result = runtime.SaveKey(kind, text);
        return result.Ok ? "SAVED" : $"REFUSED · {result.Reason!.ToUpperInvariant()}";
    }

    /// <summary>Forgets the saved key; an environment key, if there is one, applies again.</summary>
    public static string RemoveKey(LlmRuntime runtime, KeyKind kind)
    {
        var result = runtime.RemoveKey(kind);
        return result.Ok ? (runtime.SourceOf(kind) == KeySource.Environment ? "REMOVED · THE ENVIRONMENT KEY APPLIES" : "REMOVED")
            : $"NOT REMOVED · {result.Reason!.ToUpperInvariant()}";
    }

    /// <summary>One cheap real call: the line says whether the vendor accepts the key in use.</summary>
    public static async System.Threading.Tasks.Task<string> TestKey(LlmRuntime runtime, KeyKind kind)
    {
        var result = await runtime.Test(kind).ConfigureAwait(false);
        return $"{(result.Ok ? "TEST OK" : "TEST FAILED")} · {result.Message.ToUpperInvariant()}";
    }

    /// <summary>Sets the narrator's model from the tab's text box; empty clears the tab's choice.</summary>
    public static void ApplyModel(LlmRuntime runtime, string text)
    {
        runtime.Roles.Set(Narrator.Role, new RoleOverride(Model: text.Trim()));
    }

    public static void ApplyReasoning(LlmRuntime runtime, bool on) => runtime.Roles.Set(Narrator.Role, new RoleOverride(Reasoning: on));

    /// <summary>The idle timeout in seconds as the player types it; anything unreadable is ignored.</summary>
    public static bool ApplyIdleSeconds(LlmRuntime runtime, string text)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds)) return false;
        runtime.Roles.Set(Narrator.Role, new RoleOverride(IdleMs: (int)Math.Clamp(seconds * 1000, RoleRegistry.MinIdleMs, RoleRegistry.MaxIdleMs)));
        return true;
    }

    /// <summary>Forgets everything the tab chose; the environment and the data defaults apply again.</summary>
    public static void ClearChoices(LlmRuntime runtime) => runtime.Roles.Clear(Narrator.Role);

    public static string Describe(CallRecord call)
    {
        var cost = call.CostUsd is { } usd ? Usd(usd) : "NO COST";
        return $"{call.Role.ToUpperInvariant()} · {call.Model} · {call.Outcome.ToUpperInvariant()} · {call.TokensIn}->{call.TokensOut} TOK · {cost}";
    }

    private static string Spend((int Calls, double Usd) of) => $"{of.Calls} CALLS · {Usd(of.Usd)}";

    private static string Usd(double usd) => "$" + usd.ToString(usd >= 1 ? "0.00" : "0.0000", CultureInfo.InvariantCulture);
}
