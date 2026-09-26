using EtherBound.Sim.Clock;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Engine.Ops;
using EtherBound.Sim.Events;
using EtherBound.Sim.Rng;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

/// <summary>The bus's ordering, reentrancy and failure rules (ported from <c>test_events.py</c>).</summary>
public class EventBusDispatch
{
    [Fact]
    public void Wildcard_and_specific_handlers_run_in_subscription_order()
    {
        var bus = new EventBus();
        var calls = new List<string>();
        bus.Subscribe("*", _ => calls.Add("base"), "base");
        bus.Subscribe("clock.changed", _ => calls.Add("specific"), "specific");
        bus.Enqueue(new[] { SimEvent.ClockChanged(3, false) });
        bus.Drain();
        Assert.Equal(new[] { "base", "specific" }, calls);
    }

    [Fact]
    public void Reentrant_drain_returns_and_the_outer_drain_stays_fifo()
    {
        var bus = new EventBus();
        var calls = new List<string>();
        bus.Subscribe("clock.changed", e =>
        {
            calls.Add($"first:{e.Type}");
            bus.Enqueue(new[] { SimEvent.ClockTicked() });
            bus.Drain();
        }, "first");
        bus.Subscribe("*", e => calls.Add($"last:{e.Type}"), "last");
        bus.Enqueue(new[] { SimEvent.ClockChanged(3, false) });
        bus.Drain();
        Assert.Equal(new[] { "first:clock.changed", "last:clock.changed", "last:clock.ticked" }, calls);
    }

    [Fact]
    public void A_subscription_added_during_drain_starts_on_the_next_event()
    {
        var bus = new EventBus();
        var calls = new List<string>();
        bus.Subscribe("clock.changed", _ =>
        {
            calls.Add("first");
            bus.Subscribe("clock.ticked", _ => calls.Add("late"), "late");
        }, "first");
        bus.Enqueue(new[] { SimEvent.ClockChanged(3, false), SimEvent.ClockTicked() });

        bus.Drain();

        Assert.Equal(new[] { "first", "late" }, calls);
    }

    [Fact]
    public void Phases_order_systems_regardless_of_subscription_order()
    {
        var bus = new EventBus();
        var calls = new List<string>();
        bus.Subscribe("clock.ticked", _ => calls.Add("replication"), "hub", Phase.Replication);
        bus.Subscribe("clock.ticked", _ => calls.Add("minds"), "extras", Phase.Minds);
        bus.Subscribe("clock.ticked", _ => calls.Add("core"), "clock", Phase.Core);
        bus.Enqueue(new[] { SimEvent.ClockTicked() });
        bus.Drain();
        Assert.Equal(new[] { "core", "minds", "replication" }, calls);
    }

    [Fact]
    public void A_failing_handler_is_recorded_and_the_rest_still_run()
    {
        var bus = new EventBus();
        var calls = new List<int>();
        bus.Subscribe("*", _ => throw new InvalidOperationException("handler failed"), "broken");
        bus.Subscribe("*", e => calls.Add(e.Seq), "healthy");
        var first = SimEvent.ClockTicked();
        first.Seq = 1;
        var second = SimEvent.ClockTicked();
        second.Seq = 2;
        bus.Enqueue(new[] { first, second });
        bus.Drain();
        Assert.Equal(new[] { 1, 2 }, calls);
        Assert.Equal(2, bus.Failures.Count);
    }

    [Fact]
    public void A_self_feeding_cascade_stops_at_the_limit()
    {
        var bus = new EventBus();
        var dispatched = 0;
        bus.Subscribe("*", _ =>
        {
            dispatched++;
            bus.Enqueue(new[] { SimEvent.ClockTicked() });
        }, "self-feeding");
        bus.Enqueue(new[] { SimEvent.ClockTicked() });
        bus.Drain();
        Assert.Equal(EventBus.MaxCascade, dispatched);
    }

    [Fact]
    public void A_duplicate_handler_registration_is_rejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => OpCatalog.Register(new MoveOp(), new MoveOp()));
        Assert.Contains("already registered: move", error.Message);
    }
}

/// <summary>What the engine logs and when (ported from <c>test_events.py</c>).</summary>
public class EventLog
{
    private static WorldEngine Engine()
    {
        var engine = new WorldEngine();
        engine.EnsureWorld(123);
        engine.Bus.Drain();
        return engine;
    }

    [Fact]
    public void Moves_are_logged_once_per_tile()
    {
        using var engine = Engine();
        var start = engine.ReadEvents(type: "actor.moved").Count;
        engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.2);
        engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.02);
        var moved = engine.ReadEvents(type: "actor.moved");
        Assert.Equal(1, moved.Count - start);
        Assert.Equal(121, moved[^1].Data["from_tile"]!.Int("x"));
        Assert.Equal(122, moved[^1].Data["to_tile"]!.Int("x"));
    }

    [Fact]
    public void A_paused_move_is_rejected_without_event_or_dispatch()
    {
        using var engine = Engine();
        var seen = new List<SimEvent>();
        engine.Bus.Subscribe("*", seen.Add, "capture");
        engine.SetClock(paused: true);
        seen.Clear();
        var before = engine.ReadEvents(limit: 100000).Count;
        var result = engine.Submit(Ids.Player, GameAction.Move(1, 0));
        Assert.False(result.Accepted);
        Assert.Equal("paused", result.Reason);
        Assert.Equal(before, engine.ReadEvents(limit: 100000).Count);
        Assert.Empty(seen);
    }

    [Fact]
    public void A_new_game_restarts_the_log_with_its_initial_events()
    {
        using var engine = Engine();
        engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.2);
        engine.NewGame(7);
        var types = engine.ReadEvents(limit: 100000).Select(e => e.Type).ToList();
        Assert.Equal("world.generated", types[0]);
        Assert.Equal("clock.changed", types[^1]);
        Assert.Equal(Enumerable.Repeat("actor.spawned", 7), types.Skip(1).Take(types.Count - 2));
    }

    [Fact]
    public void A_submit_from_a_clock_handler_dispatches_after_the_clock_event()
    {
        using var engine = Engine();
        var dispatched = new List<SimEvent>();
        var submissions = new List<ActionResult>();
        engine.Bus.Subscribe("clock.changed", _ => submissions.Add(engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.2)), "submit-on-clock");
        engine.Bus.Subscribe("*", dispatched.Add, "capture");
        engine.SetClock(speed: 3);
        Assert.Single(submissions);
        Assert.True(submissions[0].Accepted);
        Assert.Equal(new[] { "clock.changed", "actor.moved" }, dispatched.Select(e => e.Type));
        Assert.Equal(dispatched[0].Seq + 1, dispatched[1].Seq);
        Assert.Equal(dispatched[1].Seq, engine.ReadEvents(type: "actor.moved")[^1].Seq);
    }

    [Fact]
    public void Ticks_dispatch_but_are_not_persisted()
    {
        using var engine = Engine();
        var seen = new List<SimEvent>();
        engine.Bus.Subscribe("clock.ticked", seen.Add, "tick");
        var before = engine.ReadEvents(limit: 100000).Count;
        engine.AdvanceTime();
        Assert.Single(seen);
        Assert.Equal(1, seen[0].GameMinute);
        Assert.Equal(before, engine.ReadEvents(limit: 100000).Count);
    }

    [Fact]
    public void Clock_events_are_logged_only_on_changes()
    {
        using var engine = Engine();
        var before = engine.ReadEvents(type: "clock.changed").Count;
        engine.SetClock(paused: false, speed: 1);
        engine.SetClock(speed: 3);
        Assert.Equal(before + 1, engine.ReadEvents(type: "clock.changed").Count);
    }

    [Fact]
    public void The_sequence_continues_after_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etherbound-seq-{Guid.NewGuid():N}.db");
        try
        {
            int lastSeq;
            using (var engine = new WorldEngine(path))
            {
                engine.EnsureWorld(123);
                engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.2);
                lastSeq = engine.ReadEvents(limit: 100000)[^1].Seq;
            }
            using (var restarted = new WorldEngine(path))
            {
                var count = restarted.ReadEvents(limit: 100000).Count;
                restarted.EnsureWorld();
                Assert.Equal(count, restarted.ReadEvents(limit: 100000).Count);
                restarted.Submit(Ids.Player, GameAction.Move(1, 0), 0.2);
                Assert.Equal(lastSeq + 1, restarted.ReadEvents(limit: 100000)[^1].Seq);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}

/// <summary>Clock and RNG rules (ported from <c>test_clock.py</c> and <c>test_rng.py</c>).</summary>
public class ClockRules
{
    [Fact]
    public void The_clock_ticks_pauses_and_honours_autopause_locks()
    {
        var clock = new SimClock(timeScale: 0.02);
        Assert.Equal(2, clock.Advance(0.04));
        clock.SetPaused(true);
        Assert.Equal(0, clock.Advance(0.05));
        clock.SetPaused(false);
        clock.AcquireAutopause();
        Assert.Equal(0, clock.Advance(0.05));
        clock.ReleaseAutopause();
        Assert.True(clock.Advance(0.02) >= 1);
        Assert.Throws<InvalidOperationException>(clock.ReleaseAutopause);
    }

    [Fact]
    public void Speed_divides_the_interval_and_only_three_speeds_exist()
    {
        var clock = new SimClock(timeScale: 1.0);
        clock.SetSpeed(10);
        Assert.Equal(10, clock.Advance(1.0));
        Assert.Throws<ArgumentException>(() => clock.SetSpeed(2));
    }

    [Fact]
    public void Named_rng_streams_are_deterministic_and_independent()
    {
        var first = new RngStreams(42).Stream("movement");
        var second = new RngStreams(42).Stream("movement");
        var other = new RngStreams(42).Stream("economy");
        Assert.Equal(Enumerable.Range(0, 5).Select(_ => first.Random()).ToList(), Enumerable.Range(0, 5).Select(_ => second.Random()).ToList());
        Assert.NotEqual(first.Random(), other.Random());
    }
}
