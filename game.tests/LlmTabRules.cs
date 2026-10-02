using EtherBound.Game.App;
using EtherBound.Game.Ui;
using EtherBound.Llm;
using EtherBound.Llm.Narration;
using Xunit;

namespace EtherBound.Game.Tests;

public sealed class LlmTabRules : IDisposable
{
    private const string JevKey = "tk-jev-secret-789";
    private const string ChatKey = "sk-or-chat-secret-321";
    private readonly string _directory = Directory.CreateTempSubdirectory("etherbound-llmtab").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class HttpStatusHandler : HttpMessageHandler
    {
        private readonly System.Net.HttpStatusCode _status;

        public HttpStatusHandler(System.Net.HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status));
    }

    // The shipped settings with the narrator's default model emptied, so the tab's "no model" states stay testable.
    private static readonly LlmSettings NoModelSettings = LlmSettings.Load() with
    {
        Roles = LlmSettings.Load().Roles.ToDictionary(p => p.Key, p => p.Value with { Model = "" }),
    };

    private LlmRuntime Keyed() => LlmRuntime.Create(_directory, name => name switch
    {
        LlmConfig.TypeSafeKeyVariable => JevKey,
        LlmConfig.OpenRouterKeyVariable => ChatKey,
        _ => null,
    }, settings: NoModelSettings);

    private LlmRuntime Unkeyed() => LlmRuntime.Create(_directory, _ => null, settings: NoModelSettings);

    [Fact]
    public void A_build_without_an_llm_layer_says_so_and_does_not_fail()
    {
        var view = LlmTabModel.Build(null);

        Assert.Contains("NO LLM LAYER", view.NarratorState);
        Assert.Empty(view.Calls);
    }

    [Fact]
    public void Without_keys_each_feature_says_which_key_it_needs_and_where_it_goes()
    {
        using var runtime = Unkeyed();

        var view = LlmTabModel.Build(runtime);

        Assert.False(view.JevKey);
        Assert.False(view.ChatKey);
        Assert.Equal("OFF · NO JEV KEY", view.FreeText);
        Assert.Equal("OFF · NO OPENROUTER KEY", view.NarratorState);
        Assert.Equal(("NOT SET", "NOT SET"), (view.JevKeyStatus, view.ChatKeyStatus));
        Assert.Contains(view.Hints, h => h.Contains("PASTE A TYPESAFE KEY"));
        Assert.Contains(view.Hints, h => h.Contains("PASTE AN OPENROUTER KEY"));
    }

    [Fact]
    public void With_keys_and_no_model_the_narrator_is_off_and_asks_for_one()
    {
        using var runtime = Keyed();

        var view = LlmTabModel.Build(runtime);

        Assert.True(view.JevKey);
        Assert.True(view.ChatKey);
        Assert.Equal("ON", view.FreeText);
        Assert.Equal("OFF · NO MODEL SET", view.NarratorState);
        Assert.Equal("(none)", view.NarratorModel);
        Assert.Equal("DEFAULT", view.ModelSource);
        Assert.False(view.Reasoning);
        Assert.Contains(view.Hints, h => h.Contains("TYPE A MODEL ID"));
    }

    [Fact]
    public void A_key_is_never_part_of_anything_the_tab_shows()
    {
        using var runtime = Keyed();
        LlmTabModel.ApplyModel(runtime, "some/model");
        runtime.Record(new CallRecord("narrator", "some/model", 10, 5, 0.001, "error", "HTTP 401", "prompt", "response"));

        var everything = string.Join("\n", LlmTabModel.Build(runtime).ToString(),
            string.Join("\n", LlmTabModel.Build(runtime).Calls), string.Join("\n", LlmTabModel.Build(runtime).Hints));

        Assert.DoesNotContain(JevKey, everything);
        Assert.DoesNotContain(ChatKey, everything);
    }

    [Fact]
    public void A_key_pasted_in_the_tab_is_saved_and_used_and_the_row_says_where_it_came_from_and_never_what_it_is()
    {
        using var runtime = Unkeyed();
        const string pasted = "sk-pasted-secret-5566";

        var note = LlmTabModel.SaveKey(runtime, KeyKind.OpenRouter, "  " + pasted + "  ");
        var view = LlmTabModel.Build(runtime);

        Assert.Equal("SAVED", note);
        Assert.Equal(("SET · SAVED IN GAME", true, "NOT SET"), (view.ChatKeyStatus, view.ChatKey, view.JevKeyStatus));
        Assert.Equal("OFF · NO MODEL SET", view.NarratorState);
        var everything = string.Join("\n", view, note, string.Join("\n", view.Hints), string.Join("\n", view.Calls));
        Assert.DoesNotContain(pasted, everything);
        Assert.DoesNotContain(pasted[^4..], everything);
        // The next start finds it in llm.json.
        using var again = Unkeyed();
        Assert.Equal("SET · SAVED IN GAME", LlmTabModel.Build(again).ChatKeyStatus);
    }

    [Fact]
    public void A_key_of_the_wrong_shape_is_refused_with_a_reason_and_nothing_changes()
    {
        using var runtime = Unkeyed();

        var note = LlmTabModel.SaveKey(runtime, KeyKind.Jev, "short");

        Assert.StartsWith("REFUSED · ", note);
        Assert.DoesNotContain("short", note.Replace("SHORTER", ""), StringComparison.OrdinalIgnoreCase);
        Assert.False(LlmTabModel.Build(runtime).JevKey);
    }

    [Fact]
    public void An_environment_key_is_labelled_as_such_and_a_saved_one_replaces_it_until_removed()
    {
        using var runtime = Keyed();
        Assert.Equal(("SET · FROM ENVIRONMENT", "SET · FROM ENVIRONMENT"),
            (LlmTabModel.Build(runtime).JevKeyStatus, LlmTabModel.Build(runtime).ChatKeyStatus));

        LlmTabModel.SaveKey(runtime, KeyKind.Jev, "typed-in-the-game-1");
        Assert.Equal("SET · SAVED IN GAME", LlmTabModel.Build(runtime).JevKeyStatus);

        var note = LlmTabModel.RemoveKey(runtime, KeyKind.Jev);
        Assert.Equal("REMOVED · THE ENVIRONMENT KEY APPLIES", note);
        Assert.Equal("SET · FROM ENVIRONMENT", LlmTabModel.Build(runtime).JevKeyStatus);
    }

    [Fact]
    public void A_runtime_that_cannot_save_says_so_in_the_tab()
    {
        using var runtime = new LlmRuntime(LlmSettings.Load(), new EtherBound.Llm.Jev.NoJev());

        var view = LlmTabModel.Build(runtime);

        Assert.False(view.CanEditKeys);
        Assert.Contains(view.Hints, h => h.Contains("CANNOT BE SAVED"));
        Assert.StartsWith("REFUSED · ", LlmTabModel.SaveKey(runtime, KeyKind.Jev, "long-enough-key-1"));
    }

    [Fact]
    public async Task Testing_a_key_reads_as_one_line_without_the_key()
    {
        var handler = new HttpStatusHandler(System.Net.HttpStatusCode.Unauthorized);
        using var runtime = LlmRuntime.Create(_directory, _ => null, handler, NoModelSettings);
        LlmTabModel.SaveKey(runtime, KeyKind.OpenRouter, "sk-or-test-key-9876");

        var line = await LlmTabModel.TestKey(runtime, KeyKind.OpenRouter);

        Assert.StartsWith("TEST FAILED · ", line);
        Assert.Contains("REJECTED", line);
        Assert.DoesNotContain("sk-or-test-key-9876", line);
        Assert.Equal("TEST FAILED · NO KEY IS SET", await LlmTabModel.TestKey(runtime, KeyKind.Jev));
    }

    [Fact]
    public void A_model_typed_in_the_tab_applies_to_the_next_call_and_survives_a_new_game()
    {
        using var runtime = Keyed();
        LlmTabModel.ApplyModel(runtime, "  some/model  ");
        LlmTabModel.ApplyReasoning(runtime, true);
        Assert.True(LlmTabModel.ApplyIdleSeconds(runtime, "45"));

        var now = LlmTabModel.Build(runtime);
        Assert.Equal(("some/model", "TAB", true, 45, "READY"), (now.NarratorModel, now.ModelSource, now.Reasoning, now.IdleSeconds, now.NarratorState));
        Assert.Equal(45_000, runtime.Roles.Resolve(Narrator.Role).IdleMs);

        // A new game opens a fresh runtime from the same user folder: llm.json carries the choices over.
        using var again = Keyed();
        var after = LlmTabModel.Build(again);
        Assert.Equal(("some/model", true, 45), (after.NarratorModel, after.Reasoning, after.IdleSeconds));
    }

    [Fact]
    public void Clearing_the_choices_returns_to_the_environment_and_the_defaults()
    {
        using var runtime = Keyed();
        LlmTabModel.ApplyModel(runtime, "some/model");

        LlmTabModel.ClearChoices(runtime);

        var view = LlmTabModel.Build(runtime);
        Assert.Equal(("(none)", "DEFAULT"), (view.NarratorModel, view.ModelSource));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("NaN")]
    public void An_unreadable_idle_timeout_is_ignored(string text)
    {
        using var runtime = Keyed();
        var before = runtime.Roles.Resolve(Narrator.Role).IdleMs;

        Assert.False(LlmTabModel.ApplyIdleSeconds(runtime, text));
        Assert.Equal(before, runtime.Roles.Resolve(Narrator.Role).IdleMs);
    }

    [Fact]
    public void The_idle_timeout_is_clamped()
    {
        using var runtime = Keyed();

        Assert.True(LlmTabModel.ApplyIdleSeconds(runtime, "0"));
        Assert.Equal(RoleRegistry.MinIdleMs, runtime.Roles.Resolve(Narrator.Role).IdleMs);
        Assert.True(LlmTabModel.ApplyIdleSeconds(runtime, "99999"));
        Assert.Equal(RoleRegistry.MaxIdleMs, runtime.Roles.Resolve(Narrator.Role).IdleMs);
    }

    [Fact]
    public void A_reached_cap_reads_as_stopped_and_spend_and_calls_are_listed_newest_first()
    {
        using var runtime = Keyed();
        LlmTabModel.ApplyModel(runtime, "some/model");
        runtime.Record(new CallRecord("narrator", "some/model", 100, 20, 0.6, "ok", null, "", ""));
        runtime.Record(new CallRecord("jev", "jev-latest", 50, 0, 0.0001, "ok", null, "", ""));
        runtime.Record(new CallRecord("narrator", "some/model", 100, 20, 0.5, "ok", null, "", ""));

        var view = LlmTabModel.Build(runtime);

        Assert.Equal("STOPPED · SPEND CAP REACHED", view.NarratorState);
        Assert.Equal("$1.10 OF $1.00 CAP · 3 CALLS", view.Total);
        Assert.Equal("2 CALLS · $1.10", view.NarratorSpend);
        Assert.StartsWith("NARRATOR · some/model · OK · 100->20 TOK · $0.5000", view.Calls[0]);
        Assert.StartsWith("JEV · jev-latest", view.Calls[1]);
    }

    [Fact]
    public void A_call_without_a_reported_cost_says_so_instead_of_showing_zero()
    {
        Assert.Contains("NO COST", LlmTabModel.Describe(new CallRecord("narrator", "m", 1, 1, null, "ok", null, "", "")));
    }

    [Fact]
    public void A_failing_narrator_is_announced_once_per_kind_and_a_cap_every_time()
    {
        var notices = new NarrationNotices();

        Assert.Equal("NARRATOR OFFLINE · HTTP 500", notices.Notice(NarrationStatus.Error, "http 500"));
        Assert.Null(notices.Notice(NarrationStatus.Error, "http 500"));
        Assert.Null(notices.Notice(NarrationStatus.Ok, null));
        Assert.NotNull(notices.Notice(NarrationStatus.Error, "timeout"));
        Assert.Null(notices.Notice(NarrationStatus.Rejected, "it stated a distance"));
        Assert.Null(notices.Notice(NarrationStatus.Disabled, null));
        Assert.StartsWith("NARRATOR STOPPED", notices.Notice(NarrationStatus.Capped, "the spend cap of 1.00 USD is reached"));
    }
}
