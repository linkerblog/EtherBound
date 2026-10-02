using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Sim.Tests;

public sealed class LlmCallLogRules
{
    [Fact]
    public void A_call_is_logged_with_the_game_minute_and_read_back_newest_first()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        engine.RecordLlmCall("narrator", "m1", 10, 5, 0.001, "ok", null, "prompt one", "response one");
        engine.AdvanceTime();
        engine.RecordLlmCall("narrator", "m1", 12, 0, null, "error", "HTTP 500", "prompt two", "");
        engine.RecordLlmCall("jev", "jev-latest", 100, 0, 0.0000042, "ok", null, "state", "answer");

        var all = engine.ReadLlmCalls(10);
        var narrator = engine.ReadLlmCalls(10, "narrator");

        Assert.Equal(new[] { "jev", "narrator", "narrator" }, all.Select(r => r.Role));
        Assert.Equal(2, narrator.Count);
        Assert.Equal("HTTP 500", narrator[0].Error);
        Assert.Null(narrator[0].CostUsd);
        Assert.Equal(0.001, narrator[1].CostUsd);
        Assert.Equal(0, narrator[1].GameMinute);
        Assert.Equal(1, narrator[0].GameMinute);
        Assert.Equal("prompt one", narrator[1].Prompt);
    }

    [Fact]
    public void Logging_a_call_is_not_a_gameplay_input_and_replay_ignores_it()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        var before = engine.ReadInputJournal().Count;
        var events = engine.ReadEvents(0, 1000).Count;

        engine.RecordLlmCall("narrator", "m", 1, 1, null, "ok", null, "p", "r");

        Assert.Equal(before, engine.ReadInputJournal().Count);
        Assert.Equal(events, engine.ReadEvents(0, 1000).Count);
    }

    [Fact]
    public void Pruning_keeps_full_text_for_the_newest_cuts_older_text_and_drops_the_oldest_rows()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        for (var i = 1; i <= 10; i++)
            engine.RecordLlmCall("narrator", "m", i, 0, null, "ok", null, new string('p', 100) + i, new string('r', 100) + i);

        engine.PruneLlmCalls(keepFull: 3, keepRows: 8, cutTo: 20);
        var rows = engine.ReadLlmCalls(100);

        Assert.Equal(8, rows.Count);
        Assert.Equal(Enumerable.Range(3, 8).Reverse(), rows.Select(r => r.TokensIn));
        Assert.All(rows.Take(3), r => Assert.True(r.Prompt.Length > 100 && r.Response.Length > 100));
        Assert.All(rows.Skip(3), r => Assert.Equal(20, r.Prompt.Length));
    }
}
