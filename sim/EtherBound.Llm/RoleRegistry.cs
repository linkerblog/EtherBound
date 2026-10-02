namespace EtherBound.Llm;

/// <summary>
/// The place roles are resolved: the LLM tab's choice first, then <c>ETHERBOUND_&lt;ROLE&gt;_MODEL</c>, then the
/// data default. A role exists only if <c>Data/llm.toml</c> declares it. Edits apply to the next call and are
/// written to <c>llm.json</c> at once, so a new game keeps them.
/// </summary>
public sealed class RoleRegistry
{
    public const int MinIdleMs = 1_000;
    public const int MaxIdleMs = 300_000;

    private readonly object _gate = new();
    private readonly IReadOnlyDictionary<string, RoleConfig> _defaults;
    private readonly Func<string, string?> _environment;
    private readonly string? _directory;
    private Dictionary<string, RoleOverride> _overrides;

    public RoleRegistry(IReadOnlyDictionary<string, RoleConfig> defaults, IReadOnlyDictionary<string, RoleOverride> overrides,
        string? directory, Func<string, string?>? environment = null)
    {
        _defaults = defaults;
        _directory = directory;
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _overrides = overrides.Where(p => defaults.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> Roles => _defaults.Keys.Order(StringComparer.Ordinal).ToArray();

    public RoleConfig Resolve(string role) => Describe(role).Config;

    /// <summary>The effective config and where the model came from: <c>tab</c>, <c>env</c> or <c>default</c>.</summary>
    public (RoleConfig Config, string ModelSource) Describe(string role)
    {
        if (!_defaults.TryGetValue(role, out var fallback)) throw new KeyNotFoundException($"unknown role: {role}");
        RoleOverride? chosen;
        lock (_gate) _overrides.TryGetValue(role, out chosen);
        string model;
        string source;
        if (!string.IsNullOrWhiteSpace(chosen?.Model))
            (model, source) = (chosen!.Model!.Trim(), "tab");
        else if (_environment(LlmConfig.RoleModelVariable(role))?.Trim() is { Length: > 0 } fromEnvironment)
            (model, source) = (fromEnvironment, "env");
        else
            (model, source) = (fallback.Model, "default");
        var idle = Math.Clamp(chosen?.IdleMs ?? fallback.IdleMs, MinIdleMs, MaxIdleMs);
        return (new RoleConfig(model, chosen?.Reasoning ?? fallback.Reasoning, idle, fallback.MaxTokens), source);
    }

    /// <summary>Merges the non-null fields of <paramref name="change"/> over the role's current choice and saves.</summary>
    public void Set(string role, RoleOverride change)
    {
        if (!_defaults.ContainsKey(role)) throw new KeyNotFoundException($"unknown role: {role}");
        lock (_gate)
        {
            _overrides.TryGetValue(role, out var current);
            current ??= new RoleOverride();
            _overrides[role] = new RoleOverride(
                change.Model is null ? current.Model : change.Model.Trim(),
                change.Reasoning ?? current.Reasoning,
                change.IdleMs is { } idle ? Math.Clamp(idle, MinIdleMs, MaxIdleMs) : current.IdleMs);
            Save();
        }
    }

    /// <summary>Forgets the tab's choices for a role; the environment and the data default apply again.</summary>
    public void Clear(string role)
    {
        lock (_gate)
        {
            if (_overrides.Remove(role)) Save();
        }
    }

    private void Save()
    {
        if (_directory is not null) LlmConfig.SaveRoles(_directory, new Dictionary<string, RoleOverride>(_overrides));
    }
}
