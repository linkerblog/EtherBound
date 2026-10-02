using System.Text.Json;
using System.Text.Json.Nodes;

namespace EtherBound.Llm;

/// <summary>A choice made in the LLM tab; every field is optional and a missing one falls through to the next source.</summary>
public sealed record RoleOverride(string? Model = null, bool? Reasoning = null, int? IdleMs = null);

/// <summary>The two vendors that need a key.</summary>
public enum KeyKind { Jev, OpenRouter }

/// <summary>Where the key in use came from: typed in the game and saved, an environment variable, or nowhere.</summary>
public enum KeySource { None, Saved, Environment }

/// <summary>
/// Keys and per-install choices, resolved from the environment first and <c>llm.json</c> in the user
/// data directory second. Never inside the repo, and a key is never written to any log, event or frame.
/// </summary>
public sealed record LlmConfig(string? TypeSafeKey, string? JevModel, string? OpenRouterKey, IReadOnlyDictionary<string, RoleOverride> Roles,
    KeySource TypeSafeKeySource = KeySource.None, KeySource OpenRouterKeySource = KeySource.None)
{
    public const string TypeSafeKeyVariable = "ETHERBOUND_TYPESAFE_KEY";
    public const string JevModelVariable = "ETHERBOUND_JEV_MODEL";
    public const string OpenRouterKeyVariable = "OPENROUTER_API_KEY";
    public const string FileName = "llm.json";

    public static string RoleModelVariable(string role) => $"ETHERBOUND_{role.ToUpperInvariant()}_MODEL";

    public static string EnvironmentVariable(KeyKind kind) => kind == KeyKind.Jev ? TypeSafeKeyVariable : OpenRouterKeyVariable;

    public static string FileField(KeyKind kind) => kind == KeyKind.Jev ? "typesafe_key" : "openrouter_key";

    public static LlmConfig Load(string? directory, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var file = ReadFile(directory);
        var roles = new Dictionary<string, RoleOverride>(StringComparer.Ordinal);
        if (file["roles"] is JsonObject saved)
            foreach (var (name, node) in saved)
                if (node is JsonObject role)
                    roles[name] = new RoleOverride(
                        role["model"] is JsonValue m && m.TryGetValue<string>(out var model) ? model : null,
                        role["reasoning"] is JsonValue r && r.TryGetValue<bool>(out var reasoning) ? reasoning : null,
                        role["idle_ms"] is JsonValue i && i.TryGetValue<int>(out var idle) ? idle : null);
        // A key typed in the game beats an environment variable, as a model chosen in the tab does: a stale
        // variable must not silently override what the player just pasted (Dev-009 D3).
        var (jevKey, jevSource) = Key(file, environment, KeyKind.Jev);
        var (chatKey, chatSource) = Key(file, environment, KeyKind.OpenRouter);
        return new LlmConfig(jevKey, First(environment(JevModelVariable), Text(file, "jev_model")), chatKey, roles, jevSource, chatSource);
    }

    private static (string? Key, KeySource Source) Key(JsonObject file, Func<string, string?> environment, KeyKind kind)
    {
        if (First(Text(file, FileField(kind))) is { } saved) return (saved, KeySource.Saved);
        if (First(environment(EnvironmentVariable(kind))) is { } fromEnvironment) return (fromEnvironment, KeySource.Environment);
        return (null, KeySource.None);
    }

    /// <summary>
    /// Saves a key typed in the game to <c>llm.json</c> as plain text (Dev-009 D1), keeping every other field. The caller
    /// validates it first (<see cref="KeyCheck"/>); the key is not echoed anywhere.
    /// </summary>
    public static void SaveKey(string directory, KeyKind kind, string key) => Update(directory, file => file[FileField(kind)] = key);

    /// <summary>Forgets the saved key only; the environment's one, if any, applies again.</summary>
    public static void RemoveKey(string directory, KeyKind kind) => Update(directory, file => file.Remove(FileField(kind)));

    /// <summary>
    /// Writes the role choices back, keeping every other key of the file (the keys among them) untouched.
    /// An empty set removes the section.
    /// </summary>
    public static void SaveRoles(string directory, IReadOnlyDictionary<string, RoleOverride> roles) => Update(directory, file =>
    {
        if (roles.Count == 0)
        {
            file.Remove("roles");
            return;
        }
        var section = new JsonObject();
        foreach (var (name, value) in roles.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var node = new JsonObject();
            if (value.Model is not null) node["model"] = value.Model;
            if (value.Reasoning is not null) node["reasoning"] = value.Reasoning;
            if (value.IdleMs is not null) node["idle_ms"] = value.IdleMs;
            section[name] = node;
        }
        file["roles"] = section;
    });

    /// <summary>Read-modify-write of the whole file: the other fields stay, and a failed write leaves the old file intact.</summary>
    private static void Update(string directory, Action<JsonObject> change)
    {
        Directory.CreateDirectory(directory);
        var file = ReadFile(directory);
        change(file);
        var path = Path.Combine(directory, FileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private static JsonObject ReadFile(string? directory)
    {
        if (directory is null) return new JsonObject();
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    private static string? Text(JsonObject file, string key) =>
        file[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string? First(params string?[] values) => values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
