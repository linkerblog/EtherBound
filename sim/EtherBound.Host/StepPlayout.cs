namespace EtherBound.Host;

/// <summary>
/// Draws the player's WASD steps on their own 50 ms schedule instead of on the frames they happen to
/// arrive in (Fix19). Each committed position is shown as reached one step after the step's ideal
/// send time, played back a short delay <see cref="Delay"/> behind real time so that late frames are
/// already in hand. Presentational only: it never draws a position the sim has not committed.
/// </summary>
public sealed class StepPlayout
{
    public const double MinDelay = 0.010, MaxDelay = 0.100, DelayMargin = 0.004;
    public const double LatenessWindow = 1.0;
    /// <summary>How far the playback clock may run fast or slow while the delay settles.</summary>
    public const double RateLimit = 0.02;
    private const double Interval = 1.0 / SimulationHost.MoveHz;

    private readonly Dictionary<long, double> _sent = new();
    private readonly List<(double At, (double X, double Y, double Z) Pos)> _samples = new();
    private readonly Queue<(double At, double Lateness)> _lateness = new();
    private long _applied;
    private double _render = double.NaN;

    public double Delay { get; private set; } = double.NaN;

    /// <summary>Nothing left to play: no samples, or the playback has reached the newest one.</summary>
    public bool Idle => _samples.Count == 0 || _render >= _samples[^1].At;

    /// <summary>Records the ideal time of the client's step <paramref name="index"/> (1-based, like <c>MovesApplied</c>).</summary>
    public void Sent(long index, double idealAt) => _sent[index] = idealAt;

    /// <summary>
    /// Starts a walk at <paramref name="at"/>. From idle it restarts playback from the drawn
    /// <paramref name="position"/>; mid-playback it only holds the last sample until then.
    /// </summary>
    public void Anchor((double X, double Y, double Z) position, double at)
    {
        if (Idle)
        {
            _samples.Clear();
            _samples.Add((at, position));
            _render = double.NaN;
        }
        // With steps still in flight their own samples come first, so a hold here would warp them.
        else if (_sent.Count == 0 && at > _samples[^1].At)
        {
            _samples.Add((at, _samples[^1].Pos));
        }
    }

    /// <summary>
    /// Takes a frame's player position. Returns false when the frame is not a step this playout can
    /// schedule (a move outside WASD, a jump, an unknown step); the playout is then cleared, the
    /// caller draws the player some other way, and the next scheduled step takes over again.
    /// </summary>
    public bool Applied(long movesApplied, (double X, double Y, double Z) position, double now)
    {
        if (movesApplied == _applied)
        {
            if (_samples.Count > 0 && position == _samples[^1].Pos) return true;
            Clear();
            return false;
        }
        var steps = movesApplied - _applied;
        _applied = movesApplied;
        var known = _sent.TryGetValue(movesApplied, out var idealAt);
        foreach (var index in _sent.Keys.Where(index => index <= movesApplied).ToArray()) _sent.Remove(index);
        if (!known || steps < 0 ||
            _samples.Count > 0 && Distance(position, _samples[^1].Pos) > ActorMotion.MaxStep(Interval) * steps)
        {
            Clear();
            return false;
        }
        if (_samples.Count == 0) _render = double.NaN;
        _samples.Add((_samples.Count == 0 ? idealAt + Interval : Math.Max(idealAt + Interval, _samples[^1].At), position));
        // The frame was needed when the first of its steps began, not the last one.
        _lateness.Enqueue((now, now - (idealAt - (steps - 1) * Interval)));
        return true;
    }

    /// <summary>The position to draw this frame, or null when the playout has nothing to show.</summary>
    public (double X, double Y, double Z)? Sample(double now, double delta)
    {
        if (_samples.Count == 0) return null;
        while (_lateness.Count > 0 && _lateness.Peek().At < now - LatenessWindow) _lateness.Dequeue();
        // With no recent history a step is read at most two frames after its ideal time.
        Delay = Math.Clamp((_lateness.Count > 0 ? _lateness.Max(entry => entry.Lateness) : 2 * delta) + DelayMargin,
            MinDelay, MaxDelay);

        var target = now - Delay;
        if (double.IsNaN(_render)) _render = target;
        else
        {
            // Steer towards the target delay a little at a time: a jump would move the player backwards.
            var next = _render + delta;
            _render = next + Math.Clamp(target - next, -RateLimit * delta, RateLimit * delta);
        }
        // Starved: hold on the newest committed position rather than guess past it.
        _render = Math.Min(_render, _samples[^1].At);

        while (_samples.Count > 1 && _samples[1].At <= _render) _samples.RemoveAt(0);
        var (fromAt, from) = _samples[0];
        if (_samples.Count == 1 || _render <= fromAt) return from;
        var (toAt, to) = _samples[1];
        var t = (_render - fromAt) / (toAt - fromAt);
        return (from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t, from.Z + (to.Z - from.Z) * t);
    }

    private void Clear()
    {
        _samples.Clear();
        _render = double.NaN;
    }

    private static double Distance((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        var (dx, dy, dz) = (a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
