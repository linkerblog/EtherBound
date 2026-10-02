namespace EtherBound.Llm;

/// <summary>One model call as the log and the LLM tab see it. The cost is the provider's own figure, null when absent.</summary>
public sealed record CallRecord(string Role, string Model, int TokensIn, int TokensOut, double? CostUsd, string Outcome,
    string? Error, string Prompt, string Response);

/// <summary>
/// What this session has spent, by the provider-reported cost, and the cap past which the narrator stops. A call
/// with no reported cost counts as zero and is counted apart, so the total never pretends to be exact.
/// </summary>
public sealed class SpendMeter
{
    private const int KeepRecent = 20;

    private readonly object _gate = new();
    private readonly Dictionary<string, (int Calls, double Usd)> _byRole = new(StringComparer.Ordinal);
    private readonly List<CallRecord> _recent = new();
    private bool _capAnnounced;

    public SpendMeter(double capUsd) => CapUsd = capUsd;

    public double CapUsd { get; }

    public double TotalUsd
    {
        get
        {
            lock (_gate) return _byRole.Values.Sum(v => v.Usd);
        }
    }

    public int Calls
    {
        get
        {
            lock (_gate) return _byRole.Values.Sum(v => v.Calls);
        }
    }

    public int CallsWithoutCost { get; private set; }

    /// <summary>A cap of zero means nothing may be spent, not that nothing is limited.</summary>
    public bool Capped => TotalUsd >= CapUsd;

    public void Add(CallRecord call)
    {
        lock (_gate)
        {
            _byRole.TryGetValue(call.Role, out var current);
            _byRole[call.Role] = (current.Calls + 1, current.Usd + (call.CostUsd ?? 0));
            if (call.CostUsd is null && call.Outcome != "error") CallsWithoutCost++;
            _recent.Add(call);
            if (_recent.Count > KeepRecent) _recent.RemoveAt(0);
        }
    }

    public (int Calls, double Usd) Of(string role)
    {
        lock (_gate) return _byRole.GetValueOrDefault(role);
    }

    /// <summary>The newest calls, newest first.</summary>
    public IReadOnlyList<CallRecord> Recent(int count)
    {
        lock (_gate) return _recent.AsEnumerable().Reverse().Take(count).ToArray();
    }

    /// <summary>True once, the first time it is asked after the cap is reached: the feed says so a single time.</summary>
    public bool AnnounceCapOnce()
    {
        lock (_gate)
        {
            if (!Capped || _capAnnounced) return false;
            _capAnnounced = true;
            return true;
        }
    }
}
