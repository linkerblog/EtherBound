namespace EtherBound.Sim.Events;

/// <summary>Dispatch phases (Dev-023): order between systems is a declared fact, not a line order.</summary>
public enum Phase
{
    Core = 0,
    Minds = 1,
    Systems = 2,
    Replication = 3,
    Audit = 4,
}

/// <summary>A system attaches itself to the bus; the bus stays the only meeting point.</summary>
public interface ISystem
{
    string Name { get; }

    void Attach(EventBus bus);
}

/// <summary>
/// FIFO event bus (<c>events/bus.py</c>): only the engine enqueues, handlers run after commit and
/// outside the engine. A drain started while one runs returns at once; the outer drain dispatches
/// whatever the handlers enqueued, in order.
/// </summary>
public sealed class EventBus
{
    public const int MaxCascade = 10_000;

    private readonly List<(string Type, Action<SimEvent> Handler, string Name, Phase Phase, int Order)> _handlers = new();
    private (string Type, Action<SimEvent> Handler, string Name, Phase Phase, int Order)[] _handlerSnapshot =
        Array.Empty<(string Type, Action<SimEvent> Handler, string Name, Phase Phase, int Order)>();
    private readonly Queue<SimEvent> _queue = new();
    private int _handlerVersion;
    private int _snapshotVersion = -1;
    private bool _draining;

    /// <summary>Handler failures, in order; the bus logs and carries on, as the Python bus does.</summary>
    public List<(SimEvent Event, string Handler, Exception Error)> Failures { get; } = new();

    public void Subscribe(string eventType, Action<SimEvent> handler, string name, Phase phase = Phase.Systems)
    {
        _handlers.Add((eventType, handler, name, phase, _handlers.Count));
        _handlers.Sort((a, b) => a.Phase != b.Phase ? a.Phase.CompareTo(b.Phase) : a.Order.CompareTo(b.Order));
        _handlerVersion++;
    }

    public void Enqueue(IEnumerable<SimEvent> events)
    {
        foreach (var e in events) _queue.Enqueue(e);
    }

    public void Drain()
    {
        if (_draining) return;
        _draining = true;
        try
        {
            var dispatched = 0;
            while (_queue.Count > 0)
            {
                if (dispatched >= MaxCascade)
                {
                    _queue.Clear();
                    return;
                }
                var e = _queue.Dequeue();
                dispatched++;
                if (_snapshotVersion != _handlerVersion)
                {
                    _handlerSnapshot = _handlers.ToArray();
                    _snapshotVersion = _handlerVersion;
                }
                foreach (var (type, handler, name, _, _) in _handlerSnapshot)
                {
                    if (type != e.Type && type != "*") continue;
                    try
                    {
                        handler(e);
                    }
                    catch (Exception error)
                    {
                        Failures.Add((e, name, error));
                    }
                }
            }
        }
        finally
        {
            _draining = false;
        }
    }
}
