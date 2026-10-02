using System.Text.Json.Nodes;
using EtherBound.Host;
using EtherBound.Llm;
using EtherBound.Llm.Jev;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Sim.Tests;

/// <summary>Free text through the real host thread, with a scripted Jev standing in for the network.</summary>
public sealed class FreeTextHostRules : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("etherbound-freetext").FullName;
    private string DatabasePath => Path.Combine(_directory, "world.db");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* a still-open sqlite file on a slow machine; the temp dir is disposable */ }
    }

    /// <summary>Picks the option whose description starts with <paramref name="prefix"/>.</summary>
    private static ScriptedJev Picking(string prefix, double confidence = 0.95, Action? beforeAnswering = null) => new((_, questions) =>
    {
        beforeAnswering?.Invoke();
        var entry = (ChoiceQuestion)questions[FreeTextInterpreter.EntryQuestion];
        var chosen = entry.Options.First(o => o.Description?.StartsWith(prefix, StringComparison.Ordinal) == true).Name;
        return ScriptedJev.Result(
            (FreeTextInterpreter.EntryQuestion, ScriptedJev.Pick(entry, chosen, confidence)),
            (FreeTextInterpreter.FitsQuestion, new NoulAnswer(0.95)));
    });

    private static SimulationHost Host(IJev jev, string? database = null)
    {
        var host = new SimulationHost(database ?? ":memory:", seed: 7, generator: "test", llm: new LlmRuntime(LlmSettings.Load(), jev));
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(30)));
        return host;
    }

    private static List<HostResponse> ReadUntil(SimulationHost host, Func<List<HostResponse>, bool> done)
    {
        var seen = new List<HostResponse>();
        Assert.True(SpinWait.SpinUntil(() =>
        {
            while (host.TryReadResponse(out var next)) seen.Add(next!);
            return done(seen);
        }, TimeSpan.FromSeconds(10)), "the host did not answer in time");
        return seen;
    }

    [Fact]
    public void A_confident_choice_submits_the_menu_action_through_the_ordinary_door()
    {
        using var host = Host(Picking("wait"));

        Assert.True(host.TryInterpretText(1, "let some time pass"));
        var responses = ReadUntil(host, r => r.OfType<HostActionResponse>().Any());

        var interpreted = Assert.Single(responses.OfType<HostInterpretResponse>());
        Assert.Equal(InterpretKind.Accept, interpreted.Kind);
        Assert.Equal("wait (here)", interpreted.Label);
        var action = Assert.Single(responses.OfType<HostActionResponse>());
        Assert.True(action.Result.Accepted, action.Result.Reason);
        Assert.Equal(GameAction.Wait(), action.Result.Action);
        Assert.NotNull(action.Result.Activity);
        Assert.DoesNotContain(action.Events, e => e.Type.StartsWith("llm.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_middling_choice_asks_and_submits_nothing()
    {
        using var host = Host(Picking("wait", confidence: 0.5));
        var before = host.LatestFrame!;

        Assert.True(host.TryInterpretText(1, "maybe wait"));
        var responses = ReadUntil(host, r => r.OfType<HostInterpretResponse>().Any());

        var interpreted = Assert.Single(responses.OfType<HostInterpretResponse>());
        Assert.Equal(InterpretKind.Confirm, interpreted.Kind);
        Assert.Equal(GameAction.Wait(), interpreted.Action);
        Thread.Sleep(200);
        Assert.Empty(ReadUntil(host, _ => true).OfType<HostActionResponse>());
        Assert.Null(host.LatestFrame!.Actors.First(a => a.Id == Ids.Player).Activity);
        Assert.Equal(before.Actors.First(a => a.Id == Ids.Player).X, host.LatestFrame.Actors.First(a => a.Id == Ids.Player).X);
    }

    [Fact]
    public void Without_a_key_free_text_says_so_and_changes_nothing()
    {
        using var host = Host(new NoJev());

        Assert.False(host.FreeTextAvailable);
        Assert.True(host.TryInterpretText(1, "wait"));
        var responses = ReadUntil(host, r => r.OfType<HostInterpretResponse>().Any());

        var interpreted = Assert.Single(responses.OfType<HostInterpretResponse>());
        Assert.Equal(InterpretKind.Unavailable, interpreted.Kind);
        Assert.Contains("Jev key", interpreted.Message);
        Assert.Empty(responses.OfType<HostActionResponse>());
    }

    [Fact]
    public void A_host_with_no_llm_runtime_behaves_like_no_key()
    {
        using var host = new SimulationHost(seed: 7, generator: "test");
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(30)));

        Assert.False(host.FreeTextAvailable);
        Assert.True(host.TryInterpretText(1, "wait"));
        Assert.Equal(InterpretKind.Unavailable, ReadUntil(host, r => r.OfType<HostInterpretResponse>().Any())
            .OfType<HostInterpretResponse>().Single().Kind);
    }

    [Fact]
    public void A_newer_enter_cancels_the_pending_call_and_only_the_newer_one_answers()
    {
        var release = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        var calls = 0;
        var jev = new ScriptedJev((state, questions) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
            var entry = (ChoiceQuestion)questions[FreeTextInterpreter.EntryQuestion];
            return ScriptedJev.Result(
                (FreeTextInterpreter.EntryQuestion, ScriptedJev.Pick(entry, FreeTextInterpreter.None, 0.9)),
                (FreeTextInterpreter.FitsQuestion, new NoulAnswer(0.0)));
        });
        using var host = Host(jev);

        Assert.True(host.TryInterpretText(1, "first"));
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(host.TryInterpretText(2, "second"));
        release.Set();
        var responses = ReadUntil(host, r => r.OfType<HostInterpretResponse>().Any(x => x.RequestId == 2));
        Thread.Sleep(200);
        responses.AddRange(ReadUntil(host, _ => true));

        Assert.All(responses.OfType<HostInterpretResponse>(), r => Assert.Equal(2, r.RequestId));
        Assert.Single(responses.OfType<HostInterpretResponse>());
    }

    [Fact]
    public void An_action_that_went_stale_while_jev_thought_is_rejected_by_the_engine_with_its_reason()
    {
        SimulationHost? host = null;
        var jev = Picking("wait", beforeAnswering: () => host!.TrySetClock(paused: true));
        host = Host(jev);

        Assert.True(host.TryInterpretText(1, "wait a bit"));
        var responses = ReadUntil(host, r => r.OfType<HostActionResponse>().Any());
        host.Dispose();

        var action = Assert.Single(responses.OfType<HostActionResponse>());
        Assert.False(action.Result.Accepted);
        Assert.Equal("paused", action.Result.Reason);
        Assert.Null(action.Result.Activity);
    }

    [Fact]
    public void The_decision_is_logged_and_a_replay_of_the_journal_needs_no_jev()
    {
        using (var host = Host(Picking("wait"), DatabasePath))
        {
            Assert.True(host.TryInterpretText(1, "let some time pass, please"));
            ReadUntil(host, r => r.OfType<HostActionResponse>().Any());
        }

        JsonObject state;
        List<EventRow> events;
        IReadOnlyList<InputJournalEntry> inputs;
        List<LlmCallRow> calls;
        using (var engine = new WorldEngine(DatabasePath))
        {
            calls = engine.ReadLlmCalls(10, "jev");
            events = engine.ReadEvents(0, 100000);
            inputs = engine.ReadInputJournal();
            engine.EnsureWorld(7);
            state = ReplayCheck.Essential(StateDump.Of(engine));
        }

        var jevCall = Assert.Single(calls);
        Assert.Equal(("jev", "scripted", "ok"), (jevCall.Role, jevCall.Model, jevCall.Outcome));
        Assert.Contains("e02", jevCall.Response); // `sleep` precedes `wait` in the catalog, so `wait` is the second candidate

        var logged = Assert.Single(events, e => e.Type == "llm.interpreted");
        Assert.Equal(Ids.Player, logged.ActorId);
        Assert.Equal("accepted", logged.Data["outcome"]!.GetValue<string>());
        Assert.Equal("let some time pass, please", logged.Data["text"]!.GetValue<string>());
        Assert.Equal("scripted", logged.Data["model"]!.GetValue<string>());
        // The decision precedes the action it caused.
        Assert.True(logged.Seq < events.First(e => e.Type == "activity.started" && e.ActorId == Ids.Player).Seq);
        Assert.Contains(inputs, i => i.Kind == "record_decision");
        Assert.Contains(inputs, i => i.Kind == "submit" && i.Payload["action"]!["op"]!.GetValue<string>() == "wait");

        using var replay = new WorldEngine();
        InputReplay.Replay(replay, inputs);
        Assert.True(Json.Same(state, ReplayCheck.Essential(StateDump.Of(replay))));
        var replayed = replay.ReadEvents(0, 100000);
        Assert.Equal(events.Select(e => (e.Type, e.Seq)), replayed.Select(e => (e.Type, e.Seq)));
    }

    [Fact]
    public void Saving_a_key_turns_free_text_on_in_a_running_host_with_no_restart()
    {
        // The fake vendor reads the question it was sent and picks the "wait" candidate.
        FakeHandler? handler = null;
        handler = new FakeHandler(_ =>
        {
            var options = JsonNode.Parse(handler!.Body!)!["questions"]![FreeTextInterpreter.EntryQuestion]!["criteria"]!.AsObject();
            var wait = options.First(o => o.Value?.GetValue<string>().StartsWith("wait", StringComparison.Ordinal) == true).Key;
            var answers = new JsonObject
            {
                [FreeTextInterpreter.EntryQuestion] = new JsonObject
                {
                    ["type"] = "choice", ["choice"] = wait, ["confidence"] = 0.95,
                    ["probabilities"] = new JsonObject { [wait] = 0.95 },
                },
                [FreeTextInterpreter.FitsQuestion] = new JsonObject { ["type"] = "noul", ["noul"] = 0.95 },
            };
            return FakeHandler.Json(new JsonObject { ["answers"] = answers }.ToJsonString());
        });
        using var runtime = LlmRuntime.Create(_directory, _ => null, handler);
        using var host = new SimulationHost(seed: 7, generator: "test", llm: runtime);
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(30)));

        Assert.False(host.FreeTextAvailable);
        Assert.True(host.TryInterpretText(1, "wait"));
        Assert.Equal(InterpretKind.Unavailable, ReadUntil(host, r => r.OfType<HostInterpretResponse>().Any()).OfType<HostInterpretResponse>().Single().Kind);
        Assert.Equal(0, handler.Calls);

        Assert.True(runtime.SaveKey(KeyKind.Jev, "key-typed-in-game-1").Ok);

        Assert.True(host.FreeTextAvailable);
        Assert.True(host.TryInterpretText(2, "wait"));
        var responses = ReadUntil(host, r => r.OfType<HostActionResponse>().Any());
        Assert.Equal(InterpretKind.Accept, responses.OfType<HostInterpretResponse>().Single().Kind);
        Assert.True(responses.OfType<HostActionResponse>().Single().Result.Accepted);
        Assert.Equal("key-typed-in-game-1", handler.Request!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public void Only_declared_decision_kinds_can_be_recorded()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");

        Assert.Throws<ArgumentException>(() => engine.RecordDecision(Ids.Player, "actor.moved", new JsonObject()));
        Assert.Throws<KeyNotFoundException>(() => engine.RecordDecision("ghost", "llm.interpreted", new JsonObject()));
        engine.RecordDecision(Ids.Player, "llm.interpreted", new JsonObject { ["text"] = "hi" });
        Assert.Single(engine.ReadEvents(0, 1000, "llm.interpreted"));
    }
}
