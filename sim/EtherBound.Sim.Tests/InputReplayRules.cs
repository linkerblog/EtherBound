using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Sim.Tests;

public sealed class InputReplayRules
{
    [Fact]
    public void Journal_replays_inputs_and_reproduces_state_and_committed_events()
    {
        JsonObject state;
        List<EventRow> events;
        IReadOnlyList<InputJournalEntry> inputs;
        using (var original = new WorldEngine())
        {
            original.EnsureWorld(98);
            original.NewGame(7, "test", new JsonObject());
            original.SetClock(speed: 3);
            Assert.True(original.Submit(Ids.Player, GameAction.Wait()).Accepted);
            original.AdvanceTime();
            Assert.True(original.Submit(Ids.Player, GameAction.Move(1, 0), 0.05).Accepted);
            Assert.True(original.Submit(Ids.Player, GameAction.Move(1, 0), 0.05).Accepted);
            var rejected = original.Submit(Ids.Player, GameAction.On("dig", new TileTarget(0, 0, 0)));
            Assert.False(rejected.Accepted);
            original.SetClock(paused: true);
            Assert.False(original.Submit(Ids.Player, GameAction.Move(0, 1), 0.05).Accepted);
            original.AdvanceTime();
            original.SetClock(paused: false);
            original.Submit(Ids.Player, GameAction.Move(0, 1), double.NaN);

            inputs = original.ReadInputJournal();
            Assert.NotEmpty(inputs);
            Assert.Equal(1, inputs[0].Sequence);
            Assert.Equal("new_game", inputs[0].Kind);
            Assert.Contains(inputs, input => input.Kind == "submit" && input.Payload["delta_seconds"] is JsonValue delta &&
                delta.GetValueKind() == System.Text.Json.JsonValueKind.String && delta.GetValue<string>() == "NaN");
            Assert.Contains(inputs, input => input.Kind == "submit" && input.Payload["action"]!["op"]!.GetValue<string>() == "dig");
            state = StateDump.Of(original);
            events = original.ReadEvents(0, 100000);
        }

        using var replay = new WorldEngine();
        InputReplay.Replay(replay, inputs);
        Assert.True(Json.Same(state, StateDump.Of(replay)));
        var replayedEvents = replay.ReadEvents(0, 100000);
        Assert.Equal(events.Count, replayedEvents.Count);
        for (var i = 0; i < events.Count; i++)
        {
            Assert.Equal(events[i].Type, replayedEvents[i].Type);
            Assert.Equal(events[i].ActorId, replayedEvents[i].ActorId);
            Assert.Equal(events[i].GameMinute, replayedEvents[i].GameMinute);
            Assert.True(Json.Same(events[i].Data, replayedEvents[i].Data), $"event {i}");
        }
        Assert.Equal(inputs.Count, replay.ReadInputJournal().Count);
        Assert.Contains(inputs, input => input.Kind == "submit" && input.RepeatCount == 2);
    }

    [Fact]
    public void Replay_rejects_an_unordered_journal()
    {
        using var engine = new WorldEngine();
        var inputs = new[]
        {
            new InputJournalEntry(2, "advance_time", new JsonObject()),
            new InputJournalEntry(1, "advance_time", new JsonObject()),
        };
        Assert.Throws<InvalidOperationException>(() => InputReplay.Replay(engine, inputs));
    }
}
