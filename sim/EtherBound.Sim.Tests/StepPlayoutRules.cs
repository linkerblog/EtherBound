using EtherBound.Host;
using EtherBound.Sim.Engine.Ops;
using EtherBound.Sim.Rng;

namespace EtherBound.Sim.Tests;

public sealed class StepPlayoutRules
{
    private const double Step = 1.0 / SimulationHost.MoveHz;
    private const double StepLength = MoveOp.WalkingSpeed * Step;

    [Theory]
    [InlineData(60)]
    [InlineData(75)]
    [InlineData(144)]
    public void Held_walking_keeps_walking_speed_on_every_frame_despite_late_frames(int fps)
    {
        var rng = new PyRandom(19);
        var run = Walk(fps, _ => rng.Uniform(0, 2.0 / fps));

        AssertSteady(run, fps, from: 0.6, to: 2.9);
    }

    [Fact]
    public void A_stall_holds_the_last_position_never_rewinds_and_recovers()
    {
        const int fps = 75;
        var run = Walk(fps, index => index == 16 ? 0.120 : 0);
        var stallStart = 0.5 + 16 * Step;

        Assert.All(Deltas(run), delta => Assert.True(delta.Distance >= -1e-9, $"moved backwards at {delta.At:0.000}"));
        Assert.Contains(Deltas(run), delta => delta.At > stallStart && delta.At < stallStart + 0.2 && delta.Distance < 1e-9);
        AssertSteady(run, fps, from: stallStart + 1.0, to: 2.9);
    }

    [Fact]
    public void Several_steps_in_one_frame_keep_the_speed()
    {
        const int fps = 75;
        // Every fourth step is read together with the next one.
        var run = Walk(fps, index => index % 4 == 0 ? Step : 0);

        AssertSteady(run, fps, from: 0.8, to: 2.9);
    }

    [Fact]
    public void The_first_segment_starts_from_the_anchor()
    {
        var playout = new StepPlayout();
        playout.Anchor((1, 0, 0), 10);
        playout.Sent(1, 10);

        Assert.Equal((1.0, 0.0, 0.0), playout.Sample(10, 1.0 / 75));
        Assert.True(playout.Applied(1, (1 + StepLength, 0, 0), 10.02));
        var drawn = playout.Sample(10.05, 0.03)!.Value;
        Assert.InRange(drawn.X, 1, 1 + StepLength);
    }

    [Fact]
    public void Moves_outside_wasd_and_jumps_hand_the_player_back()
    {
        var playout = new StepPlayout();
        playout.Anchor((0, 0, 0), 0);
        playout.Sent(1, 0);
        Assert.True(playout.Applied(1, (StepLength, 0, 0), 0.02));

        Assert.False(playout.Applied(1, (StepLength, 0, 3), 0.03));
        Assert.Null(playout.Sample(0.04, 0.01));

        playout.Anchor((StepLength, 0, 3), 1);
        playout.Sent(2, 1);
        Assert.False(playout.Applied(2, (40, 0, 3), 1.02));
    }

    // Replays WorldClient's order per frame (read the frame, draw, send steps) for a key held from
    // 0.5 s to 3 s. A step reaches the client on the first frame after its send frame plus 1 ms
    // plus lateFor(index); frames never overtake each other.
    private static List<(double At, double X)> Walk(int fps, Func<long, double> lateFor)
    {
        var frame = 1.0 / fps;
        var playout = new StepPlayout();
        var arrivals = new List<double>();
        var accumulator = 0.0;
        var sinceLastStep = Step;
        var walking = false;
        var drawn = 0.0;
        var run = new List<(double, double)>();
        for (var f = 0; f < fps * 4; f++)
        {
            var now = f * frame;
            var applied = arrivals.Count(arrival => arrival <= now);
            var position = applied * StepLength;
            var owned = playout.Applied(applied, (position, 0, 0), now);
            drawn = playout.Sample(now, frame)?.X ?? (owned ? drawn : position);
            run.Add((now, drawn));

            sinceLastStep += frame;
            if (now < 0.5 || now >= 3)
            {
                accumulator = 0;
                walking = false;
                continue;
            }
            var starting = !walking;
            accumulator = walking ? accumulator + frame : Math.Min(Step, sinceLastStep);
            walking = true;
            while (accumulator >= Step)
            {
                accumulator -= Step;
                var idealAt = now - accumulator;
                if (starting) playout.Anchor((drawn, 0, 0), idealAt);
                starting = false;
                playout.Sent(arrivals.Count + 1, idealAt);
                var arrival = now + 0.001 + lateFor(arrivals.Count + 1);
                arrivals.Add(Math.Max(arrival, arrivals.Count > 0 ? arrivals[^1] : 0));
                sinceLastStep = 0;
            }
        }
        return run;
    }

    private static IEnumerable<(double At, double Distance)> Deltas(List<(double At, double X)> run) =>
        run.Zip(run.Skip(1), (a, b) => (b.At, b.X - a.X));

    private static void AssertSteady(List<(double At, double X)> run, int fps, double from, double to)
    {
        var expected = MoveOp.WalkingSpeed / fps;
        var window = Deltas(run).Where(delta => delta.At >= from && delta.At <= to).ToList();
        Assert.NotEmpty(window);
        Assert.All(window, delta => Assert.True(Math.Abs(delta.Distance / expected - 1) <= 0.03,
            $"{fps} fps: speed x{delta.Distance / expected:0.000} at {delta.At:0.000}"));
    }
}
