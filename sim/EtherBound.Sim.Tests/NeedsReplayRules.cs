using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Minds;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Tests;

/// <summary>
/// A game day of needs, run from nothing but a seed: the same seed lives the same day, and a replay of
/// the journal, with no brain attached, reproduces it. Nothing here edits a row, so nothing is unjournaled.
/// </summary>
public sealed class NeedsReplayRules
{
    private const int Day = 25 * 60;

    private static WorldEngine NewDay(long seed = 7)
    {
        var engine = new WorldEngine();
        engine.NewGame(seed, "infinite");
        new ExtrasBrain(engine).Attach(engine.Bus);
        for (var i = 0; i < Day; i++) engine.AdvanceTime();
        return engine;
    }

    private static List<(string Type, int? Seq, string Actor, string Data)> Log(WorldEngine engine) =>
        engine.ReadEvents(0, 5_000_000).Select(e => (e.Type, (int?)e.Seq, e.ActorId ?? "", e.Data.ToJsonString())).ToList();

    private static Dictionary<string, string> NeedRows(WorldEngine engine) =>
        engine.OpenSession().Actors().ToDictionary(a => a.Id, a => a.Needs?.ToJsonString() ?? "null");

    [Fact]
    public void The_same_seed_lives_the_same_day_and_every_extra_eats_and_sleeps_in_it()
    {
        using var a = NewDay();
        using var b = NewDay();

        Assert.Equal(Log(a), Log(b));
        Assert.Equal(NeedRows(a), NeedRows(b));
        var consumed = a.ReadEvents(0, 5_000_000, "actor.consumed");
        Assert.NotEmpty(consumed);
        Assert.All(consumed, e => Assert.Equal("eat", e.Data.Str("op")));
        var slept = a.ReadEvents(0, 5_000_000, "actor.slept");
        Assert.NotEmpty(slept);
        var extras = a.GetState().Actors.Where(x => x.Kind == "extra").Select(x => x.Id).ToHashSet();
        Assert.Subset(extras, slept.Select(e => e.ActorId!).ToHashSet());
        // Niko is not an Extra: nobody feeds or beds him, and he keeps no row.
        Assert.DoesNotContain(consumed.Concat(slept), e => e.ActorId == Ids.Player);
        Assert.Equal("null", NeedRows(a)[Ids.Player]);
    }

    [Fact]
    public void Decay_writes_nothing_so_a_day_of_needs_costs_only_what_was_done_about_them()
    {
        using var day = NewDay();
        var types = day.ReadEvents(0, 5_000_000).Select(e => e.Type).ToHashSet();
        Assert.DoesNotContain(types, t => t.StartsWith("need.", StringComparison.Ordinal));
        var consumed = day.ReadEvents(0, 5_000_000, "actor.consumed").Count;
        var slept = day.ReadEvents(0, 5_000_000, "actor.slept").Count;
        // One row per outcome and no more: a level never moved on its own.
        Assert.InRange(consumed + slept, 1, Population.ExtraCount * 40);
    }

    [Fact]
    public void A_replay_of_the_journal_reproduces_the_day_without_the_brain()
    {
        IReadOnlyList<InputJournalEntry> inputs;
        JsonObject state;
        List<(string, int?, string, string)> log;
        Dictionary<string, string> needs;
        using (var original = NewDay())
        {
            inputs = original.ReadInputJournal();
            Assert.Contains(inputs, i => i.Kind == "submit" && i.Payload["action"]!["op"]!.GetValue<string>() == "eat");
            Assert.Contains(inputs, i => i.Kind == "submit" && i.Payload["action"]!["op"]!.GetValue<string>() == "sleep");
            state = StateDump.Of(original);
            log = Log(original);
            needs = NeedRows(original);
        }

        using var replay = new WorldEngine();
        InputReplay.Replay(replay, inputs);
        Assert.True(Json.Same(state, StateDump.Of(replay)));
        Assert.Equal(log, Log(replay));
        Assert.Equal(needs, NeedRows(replay));
    }
}
