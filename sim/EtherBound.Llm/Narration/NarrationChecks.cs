using System.Text.RegularExpressions;

namespace EtherBound.Llm.Narration;

/// <summary>The cheap deterministic checks every narration passes before it reaches the feed (Dev-008 D5).</summary>
public static partial class NarrationChecks
{
    // A figure or a number word, then a unit of length: "12 m", "two metres", "half a metre", "a few tiles".
    [GeneratedRegex(@"\b(?:\d+(?:[.,]\d+)?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|thirty|forty|fifty|hundred|several|a\s+few|half\s+a)\s*(?:m|km|cm|mm|ft|feet|foot|yards?|metres?|meters?|tiles?|blocks?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Distance();

    /// <summary>The text as it will be shown: trimmed, one paragraph, with the quotes a model likes to wrap it in removed.</summary>
    public static string Clean(string text)
    {
        var collapsed = Regex.Replace(text.Trim(), @"\s+", " ");
        if (collapsed.Length >= 2 && collapsed[0] == '"' && collapsed[^1] == '"') collapsed = collapsed[1..^1].Trim();
        return collapsed;
    }

    /// <summary>
    /// A model that ran out of tokens either wrote nothing or stopped mid-sentence. Reasoning models do it when their
    /// thinking eats the budget, so the failure says so instead of "it was empty".
    /// </summary>
    public static string? Truncation(string text, string? finishReason)
    {
        if (finishReason != "length") return null;
        return string.IsNullOrWhiteSpace(text)
            ? "the model spent its whole token budget before writing (a reasoning model?)"
            : "it was cut off at the token limit";
    }

    /// <summary>Why the text must not be shown, or null when it can.</summary>
    public static string? Reject(string text, IReadOnlyList<string> recent, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "it was empty";
        if (text.Length > maxChars) return $"it was longer than {maxChars} characters";
        if (recent.Any(line => string.Equals(Clean(line), text, StringComparison.OrdinalIgnoreCase))) return "it repeated an earlier line";
        if (Distance().IsMatch(text)) return "it stated a distance";
        return null;
    }
}
