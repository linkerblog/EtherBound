namespace EtherBound.Llm.Chat;

public sealed record ChatMessage(string Role, string Content);

/// <param name="Reasoning">Sent to the provider only when true: a model that does not reason turns slow when told to.</param>
/// <param name="IdleTimeout">Resets on every streamed chunk; it is never a total timeout, so a long reasoning call survives.</param>
public sealed record ChatRequest(string Model, IReadOnlyList<ChatMessage> Messages, bool Reasoning, int MaxTokens, TimeSpan IdleTimeout);

/// <summary>What a streamed completion came to. <see cref="CostUsd"/> is the provider's own figure, null when absent.</summary>
/// <param name="FinishReason">Why the model stopped: <c>stop</c> when it finished, <c>length</c> when it ran out of tokens.</param>
public sealed record ChatResult(string Text, int TokensIn, int TokensOut, double? CostUsd, string? Error = null, string? FinishReason = null)
{
    public bool Ok => Error is null;

    public static ChatResult Failed(string error, string partial = "") => new(partial, 0, 0, null, error);
}

public interface IChatModel
{
    /// <summary>
    /// Streams a completion, calling <paramref name="onDelta"/> with each piece of text. Failures other than
    /// the caller's own cancellation come back as a result with an <see cref="ChatResult.Error"/>.
    /// </summary>
    Task<ChatResult> Complete(ChatRequest request, Action<string>? onDelta, CancellationToken ct = default);
}

/// <summary>Canned replies for tests, in order; the last one repeats. Each call is kept for inspection.</summary>
public sealed class ScriptedChat : IChatModel
{
    private readonly Func<ChatRequest, int, ChatResult> _reply;
    private readonly List<ChatRequest> _requests = new();
    private readonly object _gate = new();

    public ScriptedChat(Func<ChatRequest, int, ChatResult> reply) => _reply = reply;

    public ScriptedChat(params string[] texts)
        : this((_, call) => new ChatResult(texts[Math.Min(call, texts.Length - 1)], 10, 5, 0.0001))
    {
    }

    public IReadOnlyList<ChatRequest> Requests
    {
        get
        {
            lock (_gate) return _requests.ToArray();
        }
    }

    public Task<ChatResult> Complete(ChatRequest request, Action<string>? onDelta, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        int call;
        lock (_gate)
        {
            call = _requests.Count;
            _requests.Add(request);
        }
        var result = _reply(request, call);
        if (result.Ok && onDelta is not null)
            foreach (var word in result.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                onDelta(word + " ");
        return Task.FromResult(result);
    }
}
