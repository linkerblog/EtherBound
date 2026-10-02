using System.Collections.Immutable;
using EtherBound.Llm.Chat;
using EtherBound.Llm.Jev;
using EtherBound.Llm.Narration;

namespace EtherBound.Llm;

/// <summary>What Test said about a key: it never carries the key, and an error message is scrubbed of it.</summary>
public sealed record KeyTestResult(bool Ok, string Message);

/// <summary>
/// What the host holds to talk to models: settings, Jev and the free-text interpreter, the OpenRouter client with
/// its roles and the narrator, and the session's spend. The host reads it from its own threads; the sim never sees
/// it, and none of it holds a key in a form the UI or a log could read. The clients are replaced as a unit when a key
/// is saved or removed (Dev-009): a call already running keeps the client it started with, the next call uses the new one.
/// </summary>
public sealed class LlmRuntime : IDisposable
{
    private sealed record Clients(IJev Jev, FreeTextInterpreter Interpreter, OpenRouterChat? Router, Narrator? Narrator,
        KeySource JevSource, KeySource ChatSource);

    private readonly object _gate = new();
    private readonly HttpClient? _http;
    private readonly string? _directory;
    private readonly Func<string, string?>? _environment;
    private readonly string _narratorPrompt;
    private Clients _clients;

    /// <summary>A runtime over given clients, for tests and tools. It has no user folder, so it cannot save keys.</summary>
    public LlmRuntime(LlmSettings settings, IJev jev, HttpClient? http = null, IChatModel? chat = null, RoleRegistry? roles = null,
        string? narratorPrompt = null)
    {
        Settings = settings;
        _http = http;
        Roles = roles ?? new RoleRegistry(settings.Roles, new Dictionary<string, RoleOverride>(), null, _ => null);
        Spend = new SpendMeter(settings.SpendCapUsd);
        _narratorPrompt = narratorPrompt ?? Narration.Narrator.DefaultPrompt();
        _clients = new Clients(jev, new FreeTextInterpreter(jev, settings), chat as OpenRouterChat,
            chat is null ? null : new Narrator(chat, Roles, settings, _narratorPrompt),
            jev.Configured ? KeySource.Saved : KeySource.None, chat is null ? KeySource.None : KeySource.Saved);
    }

    private LlmRuntime(LlmSettings settings, RoleRegistry roles, string? directory, Func<string, string?>? environment, HttpClient http)
    {
        Settings = settings;
        Roles = roles;
        Spend = new SpendMeter(settings.SpendCapUsd);
        _http = http;
        _directory = directory;
        _environment = environment;
        _narratorPrompt = Narration.Narrator.DefaultPrompt();
        _clients = Build();
    }

    public LlmSettings Settings { get; }
    public IJev Jev => Volatile.Read(ref _clients).Jev;
    public FreeTextInterpreter Interpreter => Volatile.Read(ref _clients).Interpreter;
    public RoleRegistry Roles { get; }
    public SpendMeter Spend { get; }

    /// <summary>Null when there is no OpenRouter key: the game plays on with the engine's own lines.</summary>
    public Narrator? Narrator => Volatile.Read(ref _clients).Narrator;

    /// <summary>True when a key is set, whether or not a model is.</summary>
    public bool ChatConfigured => Narrator is not null;

    /// <summary>True when the narrator would actually be called now: a key, a model and room under the cap.</summary>
    public bool NarratorReady => Narrator is { Enabled: true } && !Spend.Capped;

    /// <summary>True when keys can be saved: the runtime was made with a user folder to keep them in.</summary>
    public bool CanEditKeys => _directory is not null;

    /// <summary>Where the key in use came from; never the key.</summary>
    public KeySource SourceOf(KeyKind kind) => kind == KeyKind.Jev ? Volatile.Read(ref _clients).JevSource : Volatile.Read(ref _clients).ChatSource;

    /// <summary>Counts a call toward the session's spend and keeps it for the LLM tab.</summary>
    public void Record(CallRecord call) => Spend.Add(call);

    /// <summary>A runtime from <c>llm.json</c> and the environment; without a key the matching feature is off.</summary>
    public static LlmRuntime Create(string? configDirectory, Func<string, string?>? environment = null, HttpMessageHandler? handler = null,
        LlmSettings? settings = null)
    {
        settings ??= LlmSettings.Load();
        var config = LlmConfig.Load(configDirectory, environment);
        var roles = new RoleRegistry(settings.Roles, config.Roles, configDirectory, environment);
        return new LlmRuntime(settings, roles, configDirectory, environment, handler is null ? new HttpClient() : new HttpClient(handler));
    }

    /// <summary>
    /// Checks a pasted key and, if it passes, saves it to <c>llm.json</c> and starts using it with no restart (Dev-009).
    /// Nothing is written for a refused key. The result never carries the key.
    /// </summary>
    public KeyCheckResult SaveKey(KeyKind kind, string? raw)
    {
        if (_directory is null) return new KeyCheckResult(false, "", "this runtime has no user folder to save a key in");
        var check = KeyCheck.Validate(raw);
        if (!check.Ok) return check with { Key = "" };
        try
        {
            LlmConfig.SaveKey(_directory, kind, check.Key);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new KeyCheckResult(false, "", $"could not write {LlmConfig.FileName}: {error.Message.Replace(check.Key, "[key]", StringComparison.Ordinal)}");
        }
        Rebuild();
        return new KeyCheckResult(true, "", null);
    }

    /// <summary>Forgets the saved key only; the environment's one, if any, applies again.</summary>
    public KeyCheckResult RemoveKey(KeyKind kind)
    {
        if (_directory is null) return new KeyCheckResult(false, "", "this runtime has no user folder to save a key in");
        try
        {
            LlmConfig.RemoveKey(_directory, kind);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new KeyCheckResult(false, "", $"could not write {LlmConfig.FileName}: {error.Message}");
        }
        Rebuild();
        return new KeyCheckResult(true, "", null);
    }

    /// <summary>One cheap real call that says whether the vendor accepts the key in use. It spends nothing and logs nothing.</summary>
    public async Task<KeyTestResult> Test(KeyKind kind, CancellationToken ct = default)
    {
        var clients = Volatile.Read(ref _clients);
        if (kind == KeyKind.Jev)
        {
            if (!clients.Jev.Configured) return new KeyTestResult(false, "no key is set");
            var question = new NoulQuestion("Is this sentence a connection test?");
            var result = await clients.Jev.Ask("This is a connection test.", new Dictionary<string, JevQuestion> { ["test"] = question }, ct).ConfigureAwait(false);
            return result.Available ? new KeyTestResult(true, "Jev accepted the key") : new KeyTestResult(false, result.Error!);
        }
        if (clients.Router is null) return new KeyTestResult(false, "no key is set");
        return await clients.Router.CheckKey(Settings.OpenRouterKeyInfoEndpoint, ct).ConfigureAwait(false);
    }

    private void Rebuild()
    {
        lock (_gate) Volatile.Write(ref _clients, Build());
    }

    private Clients Build()
    {
        var config = LlmConfig.Load(_directory, _environment);
        IJev jev = config.TypeSafeKey is { } typeSafeKey
            ? new TypeSafeJev(_http!, typeSafeKey, config.JevModel ?? Settings.JevModel, Settings.JevEndpoint,
                Settings.JevPricePerMillionInput, TimeSpan.FromSeconds(Settings.JevTimeoutSeconds))
            : new NoJev();
        var router = config.OpenRouterKey is { } chatKey ? new OpenRouterChat(_http!, chatKey, Settings.OpenRouterEndpoint) : null;
        return new Clients(jev, new FreeTextInterpreter(jev, Settings), router,
            router is null ? null : new Narrator(router, Roles, Settings, _narratorPrompt), config.TypeSafeKeySource, config.OpenRouterKeySource);
    }

    public void Dispose() => _http?.Dispose();
}
