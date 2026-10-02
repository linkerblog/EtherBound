using System.Text.Json.Nodes;
using EtherBound.Host;
using EtherBound.Llm;
using EtherBound.Llm.Chat;
using EtherBound.Llm.Jev;
using EtherBound.Llm.Narration;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Sim.Tests;

/// <summary>The narrator through the real host thread, with a scripted chat model standing in for the network.</summary>
public sealed class NarrationBatchRules : IDisposable
{
    private static readonly LlmSettings Settings = NoModel.Settings;
    private readonly string _directory = Directory.CreateTempSubdirectory("etherbound-narration").FullName;
    private string DatabasePath => Path.Combine(_directory, "world.db");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* a still-open sqlite file; the temp dir is disposable */ }
    }

    private static LlmRuntime Runtime(IChatModel chat, string? model = "test/model", LlmSettings? settings = null) =>
        new(settings ?? Settings, new NoJev(), chat: chat, roles: new RoleRegistry((settings ?? Settings).Roles,
            model is null ? new Dictionary<string, RoleOverride>() : new Dictionary<string, RoleOverride> { ["narrator"] = new(model) },
            null, _ => null));

    private static SimulationHost Host(LlmRuntime runtime, string? database = null)
    {
        var host = new SimulationHost(database ?? ":memory:", seed: 7, generator: "test", llm: runtime);
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(30)));
        return host;
    }

    private static GameAction InspectHere(SimulationHost host)
    {
        var player = host.LatestFrame!.Actors.Single(a => a.Id == Ids.Player);
        return GameAction.On("inspect", new TileTarget((int)Math.Floor(player.X), (int)Math.Floor(player.Y), player.H));
    }

    private static List<HostResponse> ReadUntil(SimulationHost host, Func<List<HostResponse>, bool> done, int seconds = 10)
    {
        var seen = new List<HostResponse>();
        Assert.True(SpinWait.SpinUntil(() =>
        {
            while (host.TryReadResponse(out var next)) seen.Add(next!);
            return done(seen);
        }, TimeSpan.FromSeconds(seconds)), "the host did not answer in time");
        return seen;
    }

    private static bool HasNarration(List<HostResponse> responses, int count = 1) => responses.OfType<HostNarrationDone>().Count(d => d.RequestId > 0) >= count;

    [Fact]
    public void Walking_narrates_nothing()
    {
        var chat = new ScriptedChat("Niko walks.");
        using var host = Host(Runtime(chat));

        for (var i = 0; i < 10; i++) Assert.True(host.TryMove(1, 0));
        Thread.Sleep(400);
        var responses = ReadUntil(host, _ => true);

        Assert.Empty(chat.Requests);
        Assert.Empty(responses.OfType<HostNarrationDone>());
    }

    [Fact]
    public void An_inspection_is_narrated_once_with_its_numbers_stripped_and_streamed_to_the_feed()
    {
        var chat = new ScriptedChat("Niko studies the ground. It is cold asphalt.");
        using var host = Host(Runtime(chat));

        Assert.True(host.TrySubmitAction(1, InspectHere(host)));
        var responses = ReadUntil(host, r => HasNarration(r));

        var done = responses.OfType<HostNarrationDone>().Single();
        Assert.Equal(NarrationStatus.Ok, done.Status);
        Assert.Equal("Niko studies the ground. It is cold asphalt.", done.Text);
        Assert.Contains(responses.OfType<HostNarrationDelta>(), d => d.RequestId == done.RequestId);
        Assert.Single(chat.Requests);
        var user = chat.Requests[0].Messages[^1].Content;
        Assert.Contains("Niko looks closely: Asphalt", user);
        Assert.DoesNotContain(" m ", user);
        Assert.Equal("system", chat.Requests[0].Messages[0].Role);
    }

    [Fact]
    public void A_wait_is_narrated_when_it_finishes_not_when_it_starts()
    {
        var chat = new ScriptedChat("Time passes. Nothing changes.");
        using var host = Host(Runtime(chat));
        Assert.True(host.TrySetClock(speed: 10));

        Assert.True(host.TrySubmitAction(1, GameAction.Wait()));
        var responses = ReadUntil(host, r => HasNarration(r), seconds: 20);

        Assert.Single(chat.Requests);
        Assert.Contains("Time passes.", chat.Requests[0].Messages[^1].Content);
        Assert.Equal(NarrationStatus.Ok, responses.OfType<HostNarrationDone>().Single().Status);
    }

    [Fact]
    public void Facts_that_arrive_while_a_call_is_running_merge_into_the_next_narration()
    {
        var release = new ManualResetEventSlim(false);
        var chat = new ScriptedChat((request, call) =>
        {
            if (call == 0) release.Wait(TimeSpan.FromSeconds(10));
            return new ChatResult($"Niko looks around, line {call}.", 10, 5, 0.0001);
        });
        using var host = Host(Runtime(chat));

        Assert.True(host.TrySubmitAction(1, InspectHere(host)));
        Assert.True(SpinWait.SpinUntil(() => chat.Requests.Count == 1, TimeSpan.FromSeconds(10)));
        Assert.True(host.TrySubmitAction(2, InspectHere(host)));
        Assert.True(host.TrySubmitAction(3, InspectHere(host)));
        Thread.Sleep(200);
        Assert.Single(chat.Requests);
        release.Set();
        var responses = ReadUntil(host, r => HasNarration(r, 2));

        Assert.Equal(2, chat.Requests.Count);
        Assert.Equal(2, chat.Requests[1].Messages[^1].Content.Split("Niko looks closely").Length - 1);
        Assert.Equal(2, responses.OfType<HostNarrationDone>().Count(d => d.RequestId > 0));
    }

    [Fact]
    public void A_narration_is_committed_as_an_event_logged_as_a_call_and_restored_after_a_restart()
    {
        var chat = new ScriptedChat("Niko studies the ground. It is cold asphalt.");
        using (var host = Host(Runtime(chat), DatabasePath))
        {
            Assert.True(host.TrySubmitAction(1, InspectHere(host)));
            ReadUntil(host, r => HasNarration(r));
        }

        using (var engine = new WorldEngine(DatabasePath))
        {
            engine.EnsureWorld(7);
            var narrated = Assert.Single(engine.ReadEvents(0, 100000, "llm.narrated"));
            Assert.Equal(Ids.Player, narrated.ActorId);
            Assert.Equal("Niko studies the ground. It is cold asphalt.", narrated.Data["text"]!.GetValue<string>());
            Assert.Equal("test/model", narrated.Data["model"]!.GetValue<string>());
            var call = Assert.Single(engine.ReadLlmCalls(10, "narrator"));
            Assert.Equal(("ok", "test/model", 10, 5), (call.Outcome, call.Model, call.TokensIn, call.TokensOut));
            Assert.Equal(0.0001, call.CostUsd);
            Assert.Contains("Niko looks closely", call.Prompt);
        }

        using var again = Host(Runtime(new ScriptedChat("unused")), DatabasePath);
        Assert.True(again.TryRequestNarrationHistory(5));
        var history = ReadUntil(again, r => r.OfType<HostNarrationHistory>().Any()).OfType<HostNarrationHistory>().Single();
        Assert.Equal(new[] { "Niko studies the ground. It is cold asphalt." }, history.Lines.Select(l => l.Text));
    }

    [Theory]
    [InlineData("works")]
    [InlineData("fails")]
    [InlineData("off")]
    public void A_replay_of_the_journal_is_identical_with_narration_working_failing_or_off(string mode)
    {
        IChatModel chat = mode == "fails"
            ? new ScriptedChat((_, _) => ChatResult.Failed("HTTP 500"))
            : new ScriptedChat("Niko studies the ground. It is cold asphalt.");
        using (var host = Host(Runtime(chat, model: mode == "off" ? null : "test/model"), DatabasePath))
        {
            Assert.True(host.TrySubmitAction(1, InspectHere(host)));
            if (mode == "off") Thread.Sleep(500);
            else ReadUntil(host, r => HasNarration(r));
            Assert.True(host.TrySubmitAction(2, GameAction.Wait()));
            ReadUntil(host, r => r.OfType<HostActionResponse>().Count(a => a.RequestId == 2) >= 1);
        }

        JsonObject state;
        List<EventRow> events;
        IReadOnlyList<InputJournalEntry> inputs;
        using (var engine = new WorldEngine(DatabasePath))
        {
            (events, inputs) = (engine.ReadEvents(0, 100000), engine.ReadInputJournal());
            engine.EnsureWorld(7);
            state = ReplayCheck.Essential(StateDump.Of(engine));
        }

        using var replay = new WorldEngine();
        InputReplay.Replay(replay, inputs);
        Assert.True(Json.Same(state, ReplayCheck.Essential(StateDump.Of(replay))));
        Assert.Equal(events.Select(e => (e.Type, e.Seq)), replay.ReadEvents(0, 100000).Select(e => (e.Type, e.Seq)));
        Assert.Equal(mode == "works" ? 1 : 0, events.Count(e => e.Type == "llm.narrated"));
    }

    [Fact]
    public void A_failed_call_is_logged_as_an_error_and_commits_no_narration()
    {
        using (var host = Host(Runtime(new ScriptedChat((_, _) => ChatResult.Failed("OpenRouter answered HTTP 500"))), DatabasePath))
        {
            Assert.True(host.TrySubmitAction(1, InspectHere(host)));
            var done = ReadUntil(host, r => HasNarration(r)).OfType<HostNarrationDone>().Single();
            Assert.Equal(NarrationStatus.Error, done.Status);
            Assert.Null(done.Text);
            Assert.Contains("500", done.Detail);
        }

        using var engine = new WorldEngine(DatabasePath);
        Assert.Empty(engine.ReadEvents(0, 100000, "llm.narrated"));
        var call = Assert.Single(engine.ReadLlmCalls(10, "narrator"));
        Assert.Equal("error", call.Outcome);
        Assert.Contains("500", call.Error);
    }

    [Fact]
    public void The_spend_cap_stops_the_narrator_says_so_once_and_play_goes_on()
    {
        var chat = new ScriptedChat((_, _) => new ChatResult("Niko studies the ground.", 10, 5, 2.0));
        var runtime = Runtime(chat);
        using var host = Host(runtime);

        Assert.True(host.TrySubmitAction(1, InspectHere(host)));
        ReadUntil(host, r => HasNarration(r));
        Assert.True(runtime.Spend.Capped);

        Assert.True(host.TrySubmitAction(2, InspectHere(host)));
        var capped = ReadUntil(host, r => r.OfType<HostNarrationDone>().Any(d => d.Status == NarrationStatus.Capped));
        Assert.True(host.TrySubmitAction(3, InspectHere(host)));
        var more = ReadUntil(host, r => r.OfType<HostActionResponse>().Any(a => a.RequestId == 3));
        Thread.Sleep(200);
        more.AddRange(ReadUntil(host, _ => true));

        Assert.Single(chat.Requests);
        Assert.Single(capped.OfType<HostNarrationDone>(), d => d.Status == NarrationStatus.Capped);
        Assert.DoesNotContain(more.OfType<HostNarrationDone>(), d => d.Status == NarrationStatus.Capped);
        Assert.True(capped.OfType<HostActionResponse>().All(a => a.Result.Accepted));
    }

    [Fact]
    public void With_no_model_set_nothing_is_called_and_facts_do_not_pile_up()
    {
        var chat = new ScriptedChat("never");
        using var host = Host(Runtime(chat, model: null));

        for (var i = 1; i <= 3; i++) Assert.True(host.TrySubmitAction(i, InspectHere(host)));
        ReadUntil(host, r => r.OfType<HostActionResponse>().Count() >= 3);
        Thread.Sleep(200);

        Assert.Empty(chat.Requests);
        Assert.Empty(ReadUntil(host, _ => true).OfType<HostNarrationDone>());
    }

    [Fact]
    public void A_host_without_a_chat_client_collects_nothing_and_plays_as_before()
    {
        using var host = new SimulationHost(seed: 7, generator: "test", llm: new LlmRuntime(Settings, new NoJev()));
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(30)));

        Assert.True(host.TrySubmitAction(1, InspectHere(host)));
        var responses = ReadUntil(host, r => r.OfType<HostActionResponse>().Any());

        Assert.True(responses.OfType<HostActionResponse>().Single().Result.Accepted);
        Assert.False(host.Llm!.ChatConfigured);
    }

    [Fact]
    public void A_new_game_drops_pending_facts_and_ignores_a_narration_that_finishes_after_it()
    {
        var release = new ManualResetEventSlim(false);
        var chat = new ScriptedChat((_, _) =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return new ChatResult("Niko studies the ground.", 10, 5, 0.0001);
        });
        using var host = Host(Runtime(chat), DatabasePath);

        Assert.True(host.TrySubmitAction(1, InspectHere(host)));
        Assert.True(SpinWait.SpinUntil(() => chat.Requests.Count == 1, TimeSpan.FromSeconds(10)));
        Assert.True(host.TryNewGame(2, 11, "test"));
        ReadUntil(host, r => r.OfType<HostNewGameResponse>().Any());
        release.Set();
        var done = ReadUntil(host, r => HasNarration(r)).OfType<HostNarrationDone>().Single();

        Assert.Equal(NarrationStatus.Error, done.Status);
        Assert.Null(done.Text);
        Assert.Contains("replaced", done.Detail);
    }
}
