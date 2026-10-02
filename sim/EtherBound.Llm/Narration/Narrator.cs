using EtherBound.Llm.Chat;

namespace EtherBound.Llm.Narration;

public enum NarrationStatus
{
    /// <summary>The text passed every check and may be shown.</summary>
    Ok,
    /// <summary>Two attempts failed the checks; the engine's own line stands.</summary>
    Rejected,
    /// <summary>The call failed (network, HTTP, idle timeout); the engine's own line stands.</summary>
    Error,
    /// <summary>No model is set for the role; nothing was called.</summary>
    Disabled,
    /// <summary>The session's spend cap is reached; the host stops the narrator and says so once.</summary>
    Capped,
}

public sealed record NarrationOutcome(NarrationStatus Status, string? Text, IReadOnlyList<CallRecord> Calls, string? Detail = null);

/// <summary>
/// Packet in, text out. It proposes prose only: it never touches state, every call it makes comes back as a
/// <see cref="CallRecord"/> for the log, and a text that fails a check gets one retry that names the failure.
/// </summary>
public sealed class Narrator
{
    public const string Role = "narrator";

    private readonly IChatModel _chat;
    private readonly RoleRegistry _roles;
    private readonly LlmSettings _settings;
    private readonly string _systemPrompt;

    public Narrator(IChatModel chat, RoleRegistry roles, LlmSettings settings, string systemPrompt)
    {
        _chat = chat;
        _roles = roles;
        _settings = settings;
        _systemPrompt = systemPrompt;
    }

    public static string DefaultPrompt() => LlmSettings.ReadText("narrator.md");

    public bool Enabled => _roles.Resolve(Role).Enabled;

    /// <param name="onDelta">Each piece of text as it streams; it can include text that a check later rejects.</param>
    /// <param name="onRestart">Called before a retry streams, so the caller can clear what it showed.</param>
    public async Task<NarrationOutcome> NarrateAsync(NarrationPacket packet, Action<string>? onDelta = null, Action? onRestart = null,
        CancellationToken ct = default)
    {
        var role = _roles.Resolve(Role);
        if (!role.Enabled) return new NarrationOutcome(NarrationStatus.Disabled, null, Array.Empty<CallRecord>(), "no narrator model is set");

        var calls = new List<CallRecord>();
        var messages = new List<ChatMessage> { new("system", _systemPrompt), new("user", packet.ToUserMessage()) };
        string? failure = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
            {
                onRestart?.Invoke();
                messages.Add(new ChatMessage("user", $"That was rejected because {failure}. Write the narration again."));
            }
            var request = new ChatRequest(role.Model, messages.ToArray(), role.Reasoning, role.MaxTokens, TimeSpan.FromMilliseconds(role.IdleMs));
            var result = await _chat.Complete(request, onDelta, ct).ConfigureAwait(false);
            var prompt = string.Join("\n\n", messages.Select(m => $"[{m.Role}]\n{m.Content}"));
            if (!result.Ok)
            {
                calls.Add(new CallRecord(Role, role.Model, result.TokensIn, result.TokensOut, result.CostUsd, "error", result.Error, prompt, result.Text));
                return new NarrationOutcome(NarrationStatus.Error, null, calls, result.Error);
            }
            var text = NarrationChecks.Clean(result.Text);
            failure = NarrationChecks.Truncation(text, result.FinishReason) ?? NarrationChecks.Reject(text, packet.Recent, _settings.NarrationMaxChars);
            if (failure is null)
            {
                calls.Add(new CallRecord(Role, role.Model, result.TokensIn, result.TokensOut, result.CostUsd, "ok", null, prompt, text));
                return new NarrationOutcome(NarrationStatus.Ok, text, calls);
            }
            calls.Add(new CallRecord(Role, role.Model, result.TokensIn, result.TokensOut, result.CostUsd, "rejected", failure, prompt, result.Text));
            messages.Add(new ChatMessage("assistant", result.Text));
        }
        return new NarrationOutcome(NarrationStatus.Rejected, null, calls, failure);
    }
}
