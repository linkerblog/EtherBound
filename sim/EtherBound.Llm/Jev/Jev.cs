using System.Collections.Immutable;

namespace EtherBound.Llm.Jev;

/// <summary>One option of a choice question: its name and an optional description.</summary>
public sealed record JevOption(string Name, string? Description);

/// <summary>A typed question Jev evaluates against a state; Jev never writes prose.</summary>
public abstract record JevQuestion(string Instructions);

/// <summary>"Which of these?" The answer is always one of <see cref="Options"/>, with a confidence.</summary>
public sealed record ChoiceQuestion(string Instructions, ImmutableArray<JevOption> Options) : JevQuestion(Instructions);

/// <summary>"Yes or no?" The answer is the probability of yes.</summary>
public sealed record NoulQuestion(string Instructions, string? WhenTrue = null, string? WhenFalse = null) : JevQuestion(Instructions);

public abstract record JevAnswer;

/// <param name="Confidence">How peaked the distribution is, 0..1; a flat shape means Jev is unsure.</param>
/// <param name="Probabilities">The full distribution across the options.</param>
public sealed record ChoiceAnswer(string Choice, double Confidence, IReadOnlyDictionary<string, double> Probabilities) : JevAnswer;

public sealed record NoulAnswer(double Noul) : JevAnswer;

/// <summary>
/// What a call returned. <see cref="Error"/> is set when there was no answer at all (no key, HTTP
/// failure, timeout, unreadable body); an unavailable result never carries guessed answers.
/// </summary>
public sealed record JevResult(string Model, IReadOnlyDictionary<string, JevAnswer> Answers, int TokensIn, int TokensOut,
    double CostUsd, string? Error = null)
{
    public bool Available => Error is null;

    public static JevResult Unavailable(string model, string error) =>
        new(model, new Dictionary<string, JevAnswer>(), 0, 0, 0, error);
}

public interface IJev
{
    /// <summary>The model id calls are made against, or a placeholder when none is configured.</summary>
    string Model { get; }

    /// <summary>False for <see cref="NoJev"/>: the caller says so instead of calling.</summary>
    bool Configured { get; }

    /// <summary>
    /// Evaluates every question in parallel against the same <paramref name="state"/>. Failures other
    /// than cancellation come back as an unavailable result.
    /// </summary>
    Task<JevResult> Ask(string state, IReadOnlyDictionary<string, JevQuestion> questions, CancellationToken ct = default);
}

/// <summary>No key: every call is unavailable. It never guesses, unlike a canned mock (Dev-007 D2).</summary>
public sealed class NoJev : IJev
{
    public string Model => "none";
    public bool Configured => false;

    public Task<JevResult> Ask(string state, IReadOnlyDictionary<string, JevQuestion> questions, CancellationToken ct = default) =>
        Task.FromResult(JevResult.Unavailable(Model, "free text needs a Jev key"));
}

/// <summary>Canned answers for tests: the function sees the state and the questions it was asked.</summary>
public sealed class ScriptedJev : IJev
{
    private readonly Func<string, IReadOnlyDictionary<string, JevQuestion>, JevResult> _answer;
    private int _calls;

    public ScriptedJev(Func<string, IReadOnlyDictionary<string, JevQuestion>, JevResult> answer) => _answer = answer;

    public string Model => "scripted";
    public bool Configured => true;
    public int Calls => Volatile.Read(ref _calls);
    public string? LastState { get; private set; }
    public IReadOnlyDictionary<string, JevQuestion>? LastQuestions { get; private set; }

    public Task<JevResult> Ask(string state, IReadOnlyDictionary<string, JevQuestion> questions, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _calls);
        LastState = state;
        LastQuestions = questions;
        return Task.FromResult(_answer(state, questions));
    }

    /// <summary>A choice answer with the given confidence; the rest of the mass is spread evenly.</summary>
    public static ChoiceAnswer Pick(ChoiceQuestion question, string choice, double confidence)
    {
        var others = question.Options.Where(o => o.Name != choice).ToList();
        var rest = others.Count == 0 ? 0 : (1 - confidence) / others.Count;
        var probabilities = question.Options.ToDictionary(o => o.Name, o => o.Name == choice ? confidence : rest);
        return new ChoiceAnswer(choice, confidence, probabilities);
    }

    public static JevResult Result(params (string Id, JevAnswer Answer)[] answers) =>
        new("scripted", answers.ToDictionary(a => a.Id, a => a.Answer), 0, 0, 0);
}
