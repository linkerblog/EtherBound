namespace EtherBound.Sim.Clock;

/// <summary>
/// The logic clock's rules (<c>clock.py</c>): speeds x1/x3/x10, pause and autopause locks. It
/// owns no timer; the host feeds it real seconds and runs the ticks it reports, so the sim never
/// reads a clock itself.
/// </summary>
public sealed class SimClock
{
    private int _locks;
    private double _accumulated;

    /// <param name="timeScale">Real seconds per game minute at x1.</param>
    public SimClock(double timeScale = 1.0) => TimeScale = timeScale;

    public double TimeScale { get; set; }
    public int Speed { get; private set; } = 1;
    public bool Paused { get; private set; }
    public bool Locked => _locks > 0;

    /// <summary>Real seconds between two ticks at the current speed.</summary>
    public double Interval => TimeScale / Speed;

    public void Load(int speed, bool paused)
    {
        SetSpeed(speed);
        Paused = paused;
    }

    public void SetSpeed(int speed)
    {
        if (speed is not (1 or 3 or 10)) throw new ArgumentException("speed must be 1, 3, or 10");
        Speed = speed;
    }

    public void SetPaused(bool paused) => Paused = paused;

    public void AcquireAutopause() => _locks++;

    public void ReleaseAutopause()
    {
        if (_locks == 0) throw new InvalidOperationException("autopause lock is not held");
        _locks--;
    }

    /// <summary>
    /// Advances real time and returns how many ticks are due. Like the Python loop, the interval
    /// keeps running while paused or locked; those ticks are simply skipped.
    /// </summary>
    public int Advance(double realSeconds)
    {
        _accumulated += realSeconds;
        var due = 0;
        while (_accumulated >= Interval)
        {
            _accumulated -= Interval;
            if (!Paused && !Locked) due++;
        }
        return due;
    }

    /// <summary>Seconds until the next tick, for a host that sleeps between ticks.</summary>
    public double UntilNextTick => Math.Max(0.0, Interval - _accumulated);
}
