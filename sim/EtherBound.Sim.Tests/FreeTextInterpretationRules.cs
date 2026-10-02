using System.Collections.Immutable;
using System.Text.Json.Nodes;
using EtherBound.Llm;
using EtherBound.Llm.Jev;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Sim.Tests;

public sealed class FreeTextInterpretationRules
{
    private static readonly LlmSettings Settings = LlmSettings.Load();

    private static MenuEntry Entry(string op, string label, string? subject, GameAction action, int dx = 0, int dy = 0,
        bool available = true, string? reason = null) =>
        new(op, label, Array.Empty<string>(), available, reason, subject, action, dx, dy);

    private static MenuPayload Menu(params MenuEntry[] entries) => new(0, 0, 0, "Grass", entries,
        new[] { new MenuPlace(0, 0, 0, "Grass"), new MenuPlace(1, 0, 0, "Sand") });

    private static ImmutableArray<Candidate> Three() => ImmutableArray.Create(
        new Candidate("e01", "wait (here)", GameAction.Wait()),
        new Candidate("e02", "take: Shovel (east)", GameAction.On("take", new ObjectTarget(5)), true),
        new Candidate("e03", "dig: Asphalt (here)", GameAction.On("dig", new TileTarget(1, 1, 0)), false, "too hard to dig"));

    private static ScriptedJev Answering(string choice, double confidence, double fits = 0.9) => new((_, questions) =>
        ScriptedJev.Result(
            (FreeTextInterpreter.EntryQuestion, ScriptedJev.Pick((ChoiceQuestion)questions[FreeTextInterpreter.EntryQuestion], choice, confidence)),
            (FreeTextInterpreter.FitsQuestion, new NoulAnswer(fits))));

    private static Task<Interpretation> Run(ScriptedJev jev, string text = "pick up the shovel") =>
        new FreeTextInterpreter(jev, Settings).InterpretAsync(text, Three());

    [Fact]
    public async Task High_confidence_accepts_exactly_the_action_of_the_chosen_entry()
    {
        var result = await Run(Answering("e02", 0.95));

        Assert.Equal(InterpretKind.Accept, result.Kind);
        Assert.Equal(GameAction.On("take", new ObjectTarget(5)), result.Candidate!.Action);
    }

    [Fact]
    public async Task Middling_confidence_asks_the_player_first()
    {
        var result = await Run(Answering("e02", 0.5));

        Assert.Equal(InterpretKind.Confirm, result.Kind);
        Assert.Equal("take: Shovel (east)", result.Candidate!.Label);
    }

    [Theory]
    [InlineData("none", 0.99, 0.9)]
    [InlineData("e02", 0.99, 0.2)]
    [InlineData("e02", 0.2, 0.9)]
    [InlineData("e99", 0.99, 0.9)]
    public async Task Nothing_matching_submits_nothing(string choice, double confidence, double fits)
    {
        var result = await Run(Answering(choice, confidence, fits));

        Assert.Equal(InterpretKind.Reject, result.Kind);
        Assert.Null(result.Candidate);
    }

    [Fact]
    public async Task A_missing_or_wrong_answer_is_a_reject_never_a_guess()
    {
        var empty = await Run(new ScriptedJev((_, _) => ScriptedJev.Result()));
        var wrongType = await Run(new ScriptedJev((_, _) => ScriptedJev.Result(
            (FreeTextInterpreter.EntryQuestion, new NoulAnswer(0.9)))));

        Assert.Equal(InterpretKind.Reject, empty.Kind);
        Assert.Equal(InterpretKind.Reject, wrongType.Kind);
    }

    [Fact]
    public async Task An_entry_the_engine_refuses_explains_itself_with_the_engines_reason_and_is_not_submittable()
    {
        var blocked = await Run(Answering("e03", 0.95), "dig here");
        var unsure = await Run(Answering("e03", 0.2), "dig here");

        Assert.Equal(InterpretKind.Blocked, blocked.Kind);
        Assert.Equal("too hard to dig", blocked.Message);
        Assert.Equal(InterpretKind.Reject, unsure.Kind);
    }

    [Fact]
    public async Task No_key_means_unavailable_and_jev_is_never_asked()
    {
        var result = await new FreeTextInterpreter(new NoJev(), Settings).InterpretAsync("dig", Three());

        Assert.Equal(InterpretKind.Unavailable, result.Kind);
        Assert.Contains("Jev key", result.Message);
    }

    [Fact]
    public async Task A_jev_failure_is_unavailable_with_its_message()
    {
        var jev = new ScriptedJev((_, _) => JevResult.Unavailable("scripted", "Jev timed out after 15 s"));

        var result = await Run(jev);

        Assert.Equal(InterpretKind.Unavailable, result.Kind);
        Assert.Equal("Jev timed out after 15 s", result.Message);
    }

    [Fact]
    public async Task Empty_text_or_no_candidates_never_reaches_jev()
    {
        var jev = Answering("e01", 1);
        var interpreter = new FreeTextInterpreter(jev, Settings);

        Assert.Equal(InterpretKind.Reject, (await interpreter.InterpretAsync("   ", Three())).Kind);
        Assert.Equal(InterpretKind.Reject, (await interpreter.InterpretAsync("dig", ImmutableArray<Candidate>.Empty)).Kind);
        Assert.Equal(0, jev.Calls);
    }

    [Fact]
    public async Task The_state_lists_every_candidate_marks_refused_ones_and_quotes_the_cleaned_text()
    {
        var jev = Answering("e01", 0.9);

        await new FreeTextInterpreter(jev, Settings).InterpretAsync("say \"hi\"\nthen\tleave", Three());

        var state = jev.LastState!;
        Assert.Contains("e01 = wait (here)", state);
        Assert.Contains("e02 = take: Shovel (east)", state);
        Assert.Contains("e03 = dig: Asphalt (here) [not possible right now]", state);
        Assert.Contains("The player writes: \"say 'hi' then leave\"", state);
        var entry = (ChoiceQuestion)jev.LastQuestions![FreeTextInterpreter.EntryQuestion];
        Assert.Equal(new[] { "e01", "e02", "e03", FreeTextInterpreter.None }, entry.Options.Select(o => o.Name));
        Assert.IsType<NoulQuestion>(jev.LastQuestions[FreeTextInterpreter.FitsQuestion]);
    }

    [Fact]
    public async Task Long_text_is_capped_before_it_is_quoted()
    {
        var jev = Answering("e01", 0.9);

        await new FreeTextInterpreter(jev, Settings).InterpretAsync(new string('x', 5000), Three());

        var quoted = jev.LastState!.Split("The player writes: \"")[1].TrimEnd('"');
        Assert.Equal(Settings.MaxTextChars, quoted.Length);
    }

    [Fact]
    public void Candidates_run_first_then_refused_ones_and_never_pass_the_cap()
    {
        var menu = Menu(
            Entry("dig", "Dig", "Grass", GameAction.On("dig", new TileTarget(0, 0, 0)), available: false, reason: "no tool"),
            Entry("take", "Take", "Shovel", GameAction.On("take", new ObjectTarget(1)), dx: 1),
            Entry("wait", "Wait", null, GameAction.Wait()),
            Entry("inspect", "Inspect", null, GameAction.On("inspect", new TileTarget(1, 0, 0)), dx: 1));

        var all = CandidateBuilder.Build(menu, 10);
        var capped = CandidateBuilder.Build(menu, 2);

        Assert.Equal(new[] { "e01", "e02", "e03", "e04" }, all.Select(c => c.Key));
        Assert.Equal(new[] { "wait (here)", "take: Shovel (east)", "inspect: Sand (east)", "dig: Grass (here)" }, all.Select(c => c.Label));
        Assert.Equal(new[] { true, true, true, false }, all.Select(c => c.Available));
        Assert.Equal("no tool", all[3].Reason);
        Assert.Equal(new[] { "wait (here)", "take: Shovel (east)" }, capped.Select(c => c.Label));
    }

    [Fact]
    public void The_candidate_list_of_one_menu_is_deterministic()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        var player = engine.GetState().Actors.First(a => a.Id == Ids.Player);

        var first = CandidateBuilder.Build(engine.Menu(Ids.Player, player.X, player.Y, player.Z, 1), 40);
        var second = CandidateBuilder.Build(engine.Menu(Ids.Player, player.X, player.Y, player.Z, 1), 40);

        Assert.Equal(first.Select(c => (c.Key, c.Label, c.Available)), second.Select(c => (c.Key, c.Label, c.Available)));
        Assert.True(first.Length <= 40);
        Assert.Contains(first, c => c.Label == "wait (here)");
    }

    [Fact]
    public void Wall_slots_of_one_tile_have_distinct_labels()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        var player = engine.GetState().Actors.First(a => a.Id == Ids.Player);

        var labels = CandidateBuilder.Build(engine.Menu(Ids.Player, player.X, player.Y, player.Z, 1), 200).Select(c => c.Label).ToList();

        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Fact]
    public async Task The_event_payload_names_what_was_asked_and_answered_and_truncates_the_text()
    {
        var result = await Run(Answering("e02", 0.9), "pick up the shovel");

        var payload = result.ToEventJson(new string('t', 500), Settings.LogTextChars);

        Assert.Equal(Settings.LogTextChars, payload["text"]!.GetValue<string>().Length);
        Assert.Equal("accepted", payload["outcome"]!.GetValue<string>());
        Assert.Equal("e02", payload["entry"]!.GetValue<string>());
        Assert.Equal(3, payload["candidates"]!.GetValue<int>());
        Assert.Equal("take", payload["action"]!["op"]!.GetValue<string>());
        Assert.Equal(0.9, payload["confidence"]!.GetValue<double>());
        Assert.Equal("e02", payload["top"]![0]!["entry"]!.GetValue<string>());
        Assert.True(payload["top"]!.AsArray().Count <= 3);
        Assert.IsType<JsonObject>(payload);
    }
}
