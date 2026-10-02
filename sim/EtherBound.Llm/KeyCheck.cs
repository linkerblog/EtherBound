namespace EtherBound.Llm;

/// <summary>The outcome of checking a pasted key: the cleaned key, or why it is refused. Never echoes the key.</summary>
public sealed record KeyCheckResult(bool Ok, string Key, string? Reason);

/// <summary>
/// What a pasted key must look like before it is saved (Dev-009 D5): trimmed, 8 to 256 printable ASCII characters, no
/// spaces and no line breaks. It judges the shape only; whether the vendor accepts the key is what Test is for.
/// </summary>
public static class KeyCheck
{
    public const int MinLength = 8;
    public const int MaxLength = 256;

    public static KeyCheckResult Validate(string? raw)
    {
        var key = (raw ?? "").Trim();
        if (key.Length == 0) return new KeyCheckResult(false, "", "the key is empty");
        if (key.Length < MinLength) return new KeyCheckResult(false, "", $"the key is shorter than {MinLength} characters");
        if (key.Length > MaxLength) return new KeyCheckResult(false, "", $"the key is longer than {MaxLength} characters");
        if (key.Any(c => c <= ' ' || c > '~')) return new KeyCheckResult(false, "", "the key has a space, a line break or a character that is not plain ASCII");
        return new KeyCheckResult(true, key, null);
    }
}
