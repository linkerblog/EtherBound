using EtherBound.Host;
using EtherBound.Sim.Engine.Ops;

namespace EtherBound.Sim.Tests;

public sealed class ActorMotionRules
{
    private const double Step = 1.0 / SimulationHost.MoveHz;
    private const double Frame = 1.0 / 75;

    [Fact]
    public void A_segment_runs_linearly_from_its_start_to_its_target()
    {
        var motion = ActorMotion.At((0, 0, 0), 10);
        motion.Retarget((0.2, 0, 0), Step, ActorMotion.MaxStep(Step), 10);

        Assert.Equal((0.0, 0.0, 0.0), motion.Sample(10));
        Assert.Equal(0.1, motion.Sample(10 + Step / 2).X, 9);
        Assert.Equal((0.2, 0.0, 0.0), motion.Sample(10 + Step));
        Assert.Equal((0.2, 0.0, 0.0), motion.Sample(11));
    }

    [Fact]
    public void An_early_retarget_starts_from_the_drawn_point()
    {
        var motion = ActorMotion.At((0, 0, 0), 0);
        motion.Retarget((0.2, 0, 0), Step, ActorMotion.MaxStep(Step), 0);

        motion.Retarget((0.4, 0, 0), Step, ActorMotion.MaxStep(Step), Step / 2);

        Assert.Equal(0.1, motion.From.X, 9);
        Assert.Equal(0.1, motion.Sample(Step / 2).X, 9);
    }

    [Fact]
    public void A_far_target_snaps_and_a_repeated_target_changes_nothing()
    {
        var motion = ActorMotion.At((0, 0, 0), 0);

        motion.Retarget((5, 0, 0), Step, ActorMotion.MaxStep(Step), 1);
        Assert.Equal((5.0, 0.0, 0.0), motion.Sample(1));

        motion.Retarget((5, 0, 0), Step, ActorMotion.MaxStep(Step), 2);
        Assert.Equal(1, motion.StartedAt);
    }

    [Fact]
    public void An_extra_tick_of_walking_glides_instead_of_snapping()
    {
        var motion = ActorMotion.At((0, 0, 0), 0);
        motion.Retarget((MoveOp.WalkingSpeed, 0, 0), 1, ActorMotion.MaxStep(1), 0);

        Assert.Equal(MoveOp.WalkingSpeed / 2, motion.Sample(0.5).X, 9);
    }

    [Fact]
    public void Held_movement_at_75_fps_advances_every_frame_near_walking_speed()
    {
        var motion = ActorMotion.At((0, 0, 0), 0);
        var shown = Walk((target, now) =>
        {
            motion.Retarget((target, 0, 0), Step, ActorMotion.MaxStep(Step), now);
            return motion.Sample(now).X;
        });
        var expected = MoveOp.WalkingSpeed * Frame;

        // Skip the first step: it starts from rest, like any walk.
        Assert.All(Deltas(shown).Skip(4), delta => Assert.InRange(delta, expected * 0.75, expected * 1.25));
    }

    [Fact]
    public void The_old_fixed_speed_chase_stops_between_steps()
    {
        // Fix18 F2: MoveToward at 12 m/s finishes a 0.2 m step in about one frame, then waits.
        var drawn = 0.0;
        var shown = Walk((target, _) => drawn = Math.Min(target, drawn + 12 * Frame));

        Assert.Contains(0.0, Deltas(shown).Skip(4));
    }

    // Replays WorldClient's pacing: a step goes out on the frame the accumulator reaches one
    // interval, and the frame it produces is drawn on the next one. draw(targetX, now) -> drawn X.
    private static List<double> Walk(Func<double, double, double> draw)
    {
        var accumulator = Step;
        var sent = 0;
        var target = 0.0;
        var shown = new List<double>();
        for (var frame = 0; frame < 150; frame++)
        {
            var now = frame * Frame;
            shown.Add(draw(target, now));
            accumulator += Frame;
            while (accumulator >= Step)
            {
                sent++;
                accumulator -= Step;
            }
            target = sent * MoveOp.WalkingSpeed * Step;
        }
        return shown;
    }

    private static IEnumerable<double> Deltas(List<double> shown) =>
        shown.Zip(shown.Skip(1), (a, b) => Math.Round(b - a, 12));
}
