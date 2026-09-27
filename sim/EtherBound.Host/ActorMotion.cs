using EtherBound.Sim.Engine.Ops;

namespace EtherBound.Host;

/// <summary>
/// Where the client draws an actor between two frames (Fix18): a linear segment from the drawn
/// position to the latest committed one. Presentational only; the sim stays the source of truth.
/// </summary>
public sealed class ActorMotion
{
    private ActorMotion((double X, double Y, double Z) position, double now)
    {
        From = To = position;
        StartedAt = now;
    }

    public (double X, double Y, double Z) From { get; private set; }
    public (double X, double Y, double Z) To { get; private set; }
    public double StartedAt { get; private set; }
    public double Duration { get; private set; } = 1;

    public static ActorMotion At((double X, double Y, double Z) position, double now) => new(position, now);

    /// <summary>
    /// The farthest a legitimate update moves an actor in <paramref name="duration"/>; anything
    /// beyond it (spawn, new game, climb) is drawn as a jump rather than a glide.
    /// </summary>
    public static double MaxStep(double duration) => 2 * MoveOp.WalkingSpeed * duration + 0.5;

    public (double X, double Y, double Z) Sample(double now)
    {
        var t = Math.Clamp((now - StartedAt) / Duration, 0, 1);
        return (From.X + (To.X - From.X) * t, From.Y + (To.Y - From.Y) * t, From.Z + (To.Z - From.Z) * t);
    }

    public void Retarget((double X, double Y, double Z) position, double duration, double maxStep, double now)
    {
        if (position == To) return;
        // Starting from the drawn point, not the old target, keeps an early update from jumping.
        var drawn = Sample(now);
        var (dx, dy, dz) = (position.X - drawn.X, position.Y - drawn.Y, position.Z - drawn.Z);
        From = Math.Sqrt(dx * dx + dy * dy + dz * dz) > maxStep ? position : drawn;
        To = position;
        StartedAt = now;
        Duration = Math.Max(duration, 1e-6);
    }
}
