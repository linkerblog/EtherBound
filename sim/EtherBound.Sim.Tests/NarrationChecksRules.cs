using EtherBound.Llm;
using EtherBound.Llm.Chat;
using EtherBound.Llm.Narration;

namespace EtherBound.Sim.Tests;

public sealed class NarrationChecksRules
{
    private static readonly LlmSettings Settings = NoModel.Settings;

    private static RoleRegistry Roles(string? model = "test/model", bool? reasoning = null) => new(Settings.Roles,
        model is null ? new Dictionary<string, RoleOverride>() : new Dictionary<string, RoleOverride> { ["narrator"] = new(model, reasoning) },
        null, _ => null);

    private static NarrationPacket Packet(params string[] recent) =>
        new("night", "asphalt", new[] { "shovel" }, new[] { "Niko picked up the shovel." }, recent, 1, 2);

    private static Narrator NarratorOver(IChatModel chat, RoleRegistry? roles = null) =>
        new(chat, roles ?? Roles(), Settings, "system prompt");

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("The shovel is two metres away.", "distance")]
    [InlineData("He walked 12 m north.", "distance")]
    [InlineData("It lies 3 tiles east.", "distance")]
    [InlineData("A long way, about 40 feet.", "distance")]
    public void Empty_text_and_stated_distances_are_rejected(string text, string why)
    {
        var reason = NarrationChecks.Reject(NarrationChecks.Clean(text), Array.Empty<string>(), 400);

        Assert.NotNull(reason);
        Assert.Contains(why == "empty" ? "empty" : "distance", reason);
    }

    [Fact]
    public void Over_long_and_repeated_text_is_rejected_and_good_text_passes()
    {
        Assert.Contains("longer", NarrationChecks.Reject(new string('x', 401), Array.Empty<string>(), 400));
        Assert.Contains("repeated", NarrationChecks.Reject("Niko waits.", new[] { "niko waits." }, 400));
        Assert.Null(NarrationChecks.Reject("Niko picks up the shovel. It is heavier than it looks.", new[] { "Niko waits." }, 400));
        Assert.Null(NarrationChecks.Reject("He waited five minutes.", Array.Empty<string>(), 400));
    }

    [Fact]
    public void Clean_collapses_whitespace_and_strips_quotes_a_model_wraps_the_text_in()
    {
        Assert.Equal("Niko waits. Time passes.", NarrationChecks.Clean("  \"Niko waits.\n\n  Time passes.\"  "));
    }

    [Fact]
    public async Task A_good_first_answer_is_the_narration_and_the_call_is_recorded()
    {
        var chat = new ScriptedChat("Niko picks up the shovel. It is cold in his hand.");

        var outcome = await NarratorOver(chat).NarrateAsync(Packet());

        Assert.Equal(NarrationStatus.Ok, outcome.Status);
        Assert.Equal("Niko picks up the shovel. It is cold in his hand.", outcome.Text);
        var call = Assert.Single(outcome.Calls);
        Assert.Equal(("narrator", "test/model", "ok"), (call.Role, call.Model, call.Outcome));
        Assert.Contains("Niko picked up the shovel.", call.Prompt);
        Assert.Equal(outcome.Text, call.Response);
    }

    [Fact]
    public async Task A_rejected_first_answer_gets_one_retry_that_names_the_failure()
    {
        var chat = new ScriptedChat("The shovel is 3 m away.", "Niko picks up the shovel.");
        var restarts = 0;

        var outcome = await NarratorOver(chat).NarrateAsync(Packet(), onRestart: () => restarts++);

        Assert.Equal(NarrationStatus.Ok, outcome.Status);
        Assert.Equal("Niko picks up the shovel.", outcome.Text);
        Assert.Equal(1, restarts);
        Assert.Equal(new[] { "rejected", "ok" }, outcome.Calls.Select(c => c.Outcome));
        var retry = chat.Requests[1].Messages;
        Assert.Contains("it stated a distance", retry[^1].Content);
        Assert.Equal("assistant", retry[^2].Role);
    }

    [Fact]
    public async Task Two_failures_leave_the_engine_line_standing()
    {
        var chat = new ScriptedChat("It is 3 m away.", "Still 4 m away.");

        var outcome = await NarratorOver(chat).NarrateAsync(Packet());

        Assert.Equal(NarrationStatus.Rejected, outcome.Status);
        Assert.Null(outcome.Text);
        Assert.Equal(2, chat.Requests.Count);
        Assert.All(outcome.Calls, c => Assert.Equal("rejected", c.Outcome));
    }

    [Fact]
    public async Task A_model_that_ran_out_of_tokens_is_named_as_such_and_a_cut_off_text_is_never_shown()
    {
        var empty = new ScriptedChat((_, _) => new ChatResult("", 10, 300, 0.0001, FinishReason: "length"));
        var emptyOutcome = await NarratorOver(empty).NarrateAsync(Packet());
        Assert.Equal(NarrationStatus.Rejected, emptyOutcome.Status);
        Assert.Contains("token budget", emptyOutcome.Detail);

        var cut = new ScriptedChat((_, call) => call == 0
            ? new ChatResult("Morning light holds on the asphalt. N", 10, 300, 0.0001, FinishReason: "length")
            : new ChatResult("Niko waits.", 10, 5, 0.0001, FinishReason: "stop"));
        var cutOutcome = await NarratorOver(cut).NarrateAsync(Packet());
        Assert.Equal(NarrationStatus.Ok, cutOutcome.Status);
        Assert.Equal("Niko waits.", cutOutcome.Text);
        Assert.Contains("cut off", cut.Requests[1].Messages[^1].Content);
    }

    [Fact]
    public async Task A_failed_call_is_an_error_with_no_retry()
    {
        var chat = new ScriptedChat((_, _) => ChatResult.Failed("OpenRouter answered HTTP 500"));

        var outcome = await NarratorOver(chat).NarrateAsync(Packet());

        Assert.Equal(NarrationStatus.Error, outcome.Status);
        Assert.Single(chat.Requests);
        Assert.Equal("error", Assert.Single(outcome.Calls).Outcome);
        Assert.Contains("500", outcome.Detail);
    }

    [Fact]
    public async Task With_no_model_nothing_is_called()
    {
        var chat = new ScriptedChat("never used");
        var narrator = NarratorOver(chat, Roles(model: null));

        var outcome = await narrator.NarrateAsync(Packet());

        Assert.Equal(NarrationStatus.Disabled, outcome.Status);
        Assert.False(narrator.Enabled);
        Assert.Empty(chat.Requests);
    }

    [Fact]
    public async Task Reasoning_is_off_by_default_and_reaches_the_request_only_when_the_role_sets_it()
    {
        var plain = new ScriptedChat("Niko waits.");
        await NarratorOver(plain).NarrateAsync(Packet());
        Assert.False(plain.Requests[0].Reasoning);

        var reasoning = new ScriptedChat("Niko waits.");
        await NarratorOver(reasoning, Roles(reasoning: true)).NarrateAsync(Packet());
        Assert.True(reasoning.Requests[0].Reasoning);
    }

    [Fact]
    public async Task Text_streams_to_the_caller_while_it_is_written()
    {
        var pieces = new List<string>();

        await NarratorOver(new ScriptedChat("Niko picks it up.")).NarrateAsync(Packet(), pieces.Add);

        Assert.Equal("Niko picks it up.", string.Concat(pieces).Trim());
    }

    [Fact]
    public void The_embedded_prompt_says_what_the_narrator_may_not_do()
    {
        var prompt = Narrator.DefaultPrompt();

        Assert.Contains("two or three short sentences", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never state a distance", prompt);
        Assert.Contains("propose text only", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
