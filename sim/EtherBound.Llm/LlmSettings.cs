using System.Globalization;
using Tomlyn;
using Tomlyn.Model;

namespace EtherBound.Llm;

/// <summary>What a role runs with when nothing overrides it. An empty model means the role is off.</summary>
public sealed record RoleConfig(string Model, bool Reasoning, int IdleMs, int MaxTokens)
{
    public bool Enabled => Model.Length > 0;
}

/// <summary>Tuning read from the embedded <c>Data/llm.toml</c>: thresholds, caps, roles and timeouts.</summary>
public sealed record LlmSettings(
    string JevModel,
    string JevEndpoint,
    double JevTimeoutSeconds,
    double JevPricePerMillionInput,
    double Accept,
    double Confirm,
    double Fits,
    int MaxCandidates,
    int MaxTextChars,
    int LogTextChars,
    string OpenRouterEndpoint,
    string OpenRouterKeyInfoEndpoint,
    IReadOnlyDictionary<string, RoleConfig> Roles,
    int NarrationMaxChars,
    int NarrationRecent,
    int NarrationMaxNearby,
    int HistoryLines,
    double SpendCapUsd,
    int KeepFullCalls,
    int KeepCallRows,
    int CutTextChars)
{
    public static LlmSettings Load() => Parse(ReadText("llm.toml"));

    public static LlmSettings Parse(string toml)
    {
        var root = TomlSerializer.Deserialize<TomlTable>(toml)!;
        var jev = Table(root, "jev");
        var interpret = Table(root, "interpret");
        var narration = Table(root, "narration");
        var spend = Table(root, "spend");
        var roles = new Dictionary<string, RoleConfig>(StringComparer.Ordinal);
        foreach (var (name, entry) in Table(root, "roles"))
        {
            var role = entry as TomlTable ?? throw new InvalidDataException($"llm.toml: [roles.{name}] must be a table");
            roles[name] = new RoleConfig((string)role["model"], (bool)role["reasoning"], Int(role["idle_ms"]), Int(role["max_tokens"]));
        }
        var settings = new LlmSettings(
            (string)jev["model"], (string)jev["endpoint"], Num(jev["timeout_s"]), Num(jev["price_per_million_input"]),
            Num(interpret["accept"]), Num(interpret["confirm"]), Num(interpret["fits"]),
            Int(interpret["max_candidates"]), Int(interpret["max_text_chars"]), Int(interpret["log_text_chars"]),
            (string)Table(root, "openrouter")["endpoint"], (string)Table(root, "openrouter")["key_info_endpoint"], roles,
            Int(narration["max_chars"]), Int(narration["recent"]), Int(narration["max_nearby"]), Int(narration["history_lines"]),
            Num(spend["cap_usd"]), Int(spend["keep_full"]), Int(spend["keep_rows"]), Int(spend["cut_text_chars"]));
        if (!(0 <= settings.Confirm && settings.Confirm <= settings.Accept && settings.Accept <= 1))
            throw new InvalidDataException("llm.toml: need 0 <= confirm <= accept <= 1");
        if (settings.MaxCandidates < 1) throw new InvalidDataException("llm.toml: max_candidates must be positive");
        if (settings.SpendCapUsd < 0) throw new InvalidDataException("llm.toml: cap_usd cannot be negative");
        return settings;
    }

    internal static string ReadText(string name)
    {
        using var stream = typeof(LlmSettings).Assembly.GetManifestResourceStream($"EtherBound.Llm.Data.{name}")
            ?? throw new FileNotFoundException($"embedded data file {name} is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static TomlTable Table(TomlTable root, string key) =>
        root.TryGetValue(key, out var value) && value is TomlTable table ? table : throw new InvalidDataException($"llm.toml: missing [{key}]");

    internal static double Num(object value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

    internal static int Int(object value) => (int)Convert.ToInt64(value, CultureInfo.InvariantCulture);
}
