using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EtherBound.Llm.Jev;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;

namespace EtherBound.Llm;

/// <summary>
/// One thing Niko could try right now, as Jev sees it. A candidate the engine would refuse is kept, with
/// its own reason, so "dig here" on asphalt is answered with why instead of "nothing matches".
/// </summary>
public sealed record Candidate(string Key, string Label, GameAction Action, bool Available = true, string? Reason = null);

public enum InterpretKind
{
    /// <summary>Confident enough to submit the action.</summary>
    Accept,
    /// <summary>Plausible but not sure: ask the player before submitting.</summary>
    Confirm,
    /// <summary>Nothing in reach matches; nothing is submitted.</summary>
    Reject,
    /// <summary>Jev understood a listed action the engine refuses right now; the reason is the engine's.</summary>
    Blocked,
    /// <summary>Jev gave no answer (no key, failure, timeout).</summary>
    Unavailable,
}

public sealed record Interpretation(InterpretKind Kind, Candidate? Candidate, double Confidence, double Fits, string Message,
    string Model, int TokensIn, int TokensOut, double CostUsd, IReadOnlyList<(string Entry, double Probability)> Top,
    int CandidateCount)
{
    /// <summary>The <c>llm.interpreted</c> payload: what was asked, what Jev answered and what came of it.</summary>
    public JsonObject ToEventJson(string text, int logTextChars) => Json.Obj(
        ("text", text.Length <= logTextChars ? text : text[..logTextChars]),
        ("model", Model),
        ("outcome", Kind switch
        {
            InterpretKind.Accept => "accepted",
            InterpretKind.Confirm => "confirm",
            InterpretKind.Blocked => "blocked",
            _ => "rejected",
        }),
        ("candidates", CandidateCount),
        ("entry", Candidate?.Key),
        ("label", Candidate?.Label),
        ("action", Candidate?.Action.ToJson()),
        ("confidence", Math.Round(Confidence, 4)),
        ("fits", Math.Round(Fits, 4)),
        ("top", new JsonArray(Top.Select(t => (JsonNode)Json.Obj(("entry", t.Entry),
            ("p", Math.Round(t.Probability, 4)))).ToArray())),
        ("tokens_in", TokensIn),
        ("tokens_out", TokensOut),
        ("cost_usd", CostUsd));
}

/// <summary>Turns Niko's menu into the closed list Jev chooses from.</summary>
public static class CandidateBuilder
{
    /// <summary>
    /// Entries the engine would run come first, then the ones it would refuse (they only fill what is
    /// left of the cap); each group is nearest tile first and in catalog order within a tile. The labels
    /// are the generated menu's own words plus where the entry acts, so nothing is authored.
    /// </summary>
    public static ImmutableArray<Candidate> Build(MenuPayload menu, int cap)
    {
        var ordered = menu.Entries
            .Select((entry, index) => (entry, index))
            .OrderBy(p => p.entry.Available ? 0 : 1)
            .ThenBy(p => Math.Abs(p.entry.TileDx) + Math.Abs(p.entry.TileDy))
            .ThenBy(p => p.index)
            .Take(cap);
        var candidates = ImmutableArray.CreateBuilder<Candidate>();
        foreach (var (entry, _) in ordered)
            candidates.Add(new Candidate($"e{(candidates.Count + 1).ToString("00", CultureInfo.InvariantCulture)}",
                Describe(entry, menu.Places), entry.Action, entry.Available, entry.Reason));
        return candidates.ToImmutable();
    }

    internal static string Describe(MenuEntry entry, IReadOnlyList<MenuPlace> places)
    {
        var label = entry.Label.ToLowerInvariant();
        // A tile entry has no subject of its own; the scanned place names its surface ("asphalt").
        var subject = !string.IsNullOrWhiteSpace(entry.Subject) ? entry.Subject
            : entry.Action.Target is TileTarget ? places.FirstOrDefault(p => p.Dx == entry.TileDx && p.Dy == entry.TileDy)?.Label
            : null;
        // Four wall slots share one tile and one material; the slot is what tells them apart.
        var slot = entry.Action.Target is EdgeTarget edge ? $", {EdgeWords(edge.Direction)}" : "";
        return $"{label}{(string.IsNullOrWhiteSpace(subject) ? "" : $": {subject}")}{slot} ({Where(entry.TileDx, entry.TileDy)})";
    }

    private static string EdgeWords(string direction) => direction switch
    {
        WallSlots.North => "on the north edge of the tile",
        WallSlots.West => "on the west edge of the tile",
        WallSlots.HalfH => "across the middle of the tile, horizontal",
        WallSlots.HalfV => "across the middle of the tile, vertical",
        _ => $"on the {direction} slot",
    };

    /// <summary>World axes: x grows east and y grows south, as the wall edges name them.</summary>
    internal static string Where(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return "here";
        var vertical = dy < 0 ? "north" : dy > 0 ? "south" : "";
        var horizontal = dx > 0 ? "east" : dx < 0 ? "west" : "";
        return vertical.Length > 0 && horizontal.Length > 0 ? $"{vertical}-{horizontal}" : vertical + horizontal;
    }
}

/// <summary>
/// Free text to one of the engine's own actions, through Jev. Jev picks among the candidates; the
/// code reads the confidence and decides. It never invents an action and never writes state.
/// </summary>
public sealed class FreeTextInterpreter
{
    public const string None = "none";
    public const string EntryQuestion = "entry";
    public const string FitsQuestion = "fits";

    private readonly IJev _jev;
    private readonly LlmSettings _settings;

    public FreeTextInterpreter(IJev jev, LlmSettings settings)
    {
        _jev = jev;
        _settings = settings;
    }

    public bool Configured => _jev.Configured;

    public async Task<Interpretation> InterpretAsync(string text, IReadOnlyList<Candidate> candidates, CancellationToken ct = default)
    {
        if (!_jev.Configured) return Failed(_jev.Model, "free text needs a Jev key", candidates.Count);
        var clean = Clean(text, _settings.MaxTextChars);
        if (clean.Length == 0 || candidates.Count == 0)
            return new Interpretation(InterpretKind.Reject, null, 0, 0, "Nothing in reach matches that.", _jev.Model, 0, 0, 0,
                Array.Empty<(string, double)>(), candidates.Count);

        var result = await _jev.Ask(State(clean, candidates), Questions(candidates), ct).ConfigureAwait(false);
        if (!result.Available) return Failed(result.Model, result.Error!, candidates.Count);
        return Decide(result, candidates);
    }

    internal static string State(string text, IReadOnlyList<Candidate> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine("A player of a game writes, in free text, what their character Niko does next.");
        builder.AppendLine("Niko can try exactly the actions listed below, and nothing else, right now.");
        builder.AppendLine("Actions:");
        foreach (var candidate in candidates)
            builder.AppendLine($"{candidate.Key} = {candidate.Label}{(candidate.Available ? "" : " [not possible right now]")}");
        builder.AppendLine();
        builder.Append($"The player writes: \"{text}\"");
        return builder.ToString();
    }

    internal static IReadOnlyDictionary<string, JevQuestion> Questions(IReadOnlyList<Candidate> candidates)
    {
        var options = candidates.Select(c => new JevOption(c.Key, c.Label)).ToList();
        options.Add(new JevOption(None, "none of the listed actions matches what the player wrote"));
        return new Dictionary<string, JevQuestion>
        {
            [EntryQuestion] = new ChoiceQuestion("Which listed action does the player's text ask Niko to do?", options.ToImmutableArray()),
            [FitsQuestion] = new NoulQuestion("Does the player's text ask for one of the listed actions?",
                "the text asks Niko to do one of the listed actions", "the text asks for something else, or only talks"),
        };
    }

    internal Interpretation Decide(JevResult result, IReadOnlyList<Candidate> candidates)
    {
        var reject = (string message, double confidence, double fits, IReadOnlyList<(string, double)> top) =>
            new Interpretation(InterpretKind.Reject, null, confidence, fits, message, result.Model, result.TokensIn, result.TokensOut,
                result.CostUsd, top, candidates.Count);

        if (!result.Answers.TryGetValue(EntryQuestion, out var raw) || raw is not ChoiceAnswer choice)
            return reject("Jev did not answer that.", 0, 0, Array.Empty<(string, double)>());
        var fits = result.Answers.TryGetValue(FitsQuestion, out var fitsRaw) && fitsRaw is NoulAnswer noul ? noul.Noul : 0;
        var top = choice.Probabilities.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(3).Select(p => (p.Key, p.Value)).ToList();
        var picked = candidates.FirstOrDefault(c => c.Key == choice.Choice);
        if (picked is null || fits < _settings.Fits)
            return reject("Nothing in reach matches that.", choice.Confidence, fits, top);
        if (!picked.Available && choice.Confidence >= _settings.Confirm)
            return new Interpretation(InterpretKind.Blocked, picked, choice.Confidence, fits, picked.Reason ?? "not possible right now",
                result.Model, result.TokensIn, result.TokensOut, result.CostUsd, top, candidates.Count);
        if (!picked.Available)
            return reject("Nothing in reach matches that.", choice.Confidence, fits, top);
        if (choice.Confidence >= _settings.Accept)
            return new Interpretation(InterpretKind.Accept, picked, choice.Confidence, fits, "", result.Model, result.TokensIn,
                result.TokensOut, result.CostUsd, top, candidates.Count);
        if (choice.Confidence >= _settings.Confirm)
            return new Interpretation(InterpretKind.Confirm, picked, choice.Confidence, fits, "", result.Model, result.TokensIn,
                result.TokensOut, result.CostUsd, top, candidates.Count);
        return reject("Nothing in reach matches that.", choice.Confidence, fits, top);
    }

    private static Interpretation Failed(string model, string error, int count) =>
        new(InterpretKind.Unavailable, null, 0, 0, error, model, 0, 0, 0, Array.Empty<(string, double)>(), count);

    /// <summary>One line, no control characters, capped: the text is quoted inside Jev's state.</summary>
    internal static string Clean(string text, int cap)
    {
        var builder = new StringBuilder(Math.Min(text.Length, cap));
        foreach (var c in text)
        {
            if (builder.Length >= cap) break;
            builder.Append(char.IsControl(c) ? ' ' : c == '"' ? '\'' : c);
        }
        return builder.ToString().Trim();
    }
}
