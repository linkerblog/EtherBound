using EtherBound.Game.App;
using EtherBound.Host;
using EtherBound.Llm;
using EtherBound.Sim.Core;
using Xunit;

namespace EtherBound.Game.Tests;

public sealed class FreeTextFlowTests
{
    private static HostInterpretResponse Answer(InterpretKind kind, string label = "wait (here)", string message = "", GameAction? action = null) =>
        new(1, kind, label, message, action);

    [Fact]
    public void A_fresh_line_is_sent_to_jev_when_it_is_available_and_reported_when_it_is_not()
    {
        var flow = new FreeTextFlow();

        Assert.Equal(new FreeTextFlow.Submission(FreeTextFlow.Step.Ask, "dig here"), flow.Submit("  dig here ", available: true));
        Assert.Equal(FreeTextFlow.Step.Unavailable, flow.Submit("dig here", available: false).Step);
        Assert.Equal(FreeTextFlow.Step.Nothing, flow.Submit("   ", available: true).Step);
    }

    [Fact]
    public void A_confirm_arms_a_question_and_enter_or_yes_releases_exactly_that_action()
    {
        var flow = new FreeTextFlow();
        var line = flow.Present(Answer(InterpretKind.Confirm, action: GameAction.Wait()));

        Assert.Equal("DID YOU MEAN: WAIT (HERE)? ENTER = YES", line.Text);
        Assert.NotNull(flow.Confirmation);

        var yes = flow.Submit("", available: true);
        Assert.Equal(FreeTextFlow.Step.Confirmed, yes.Step);
        Assert.Equal(GameAction.Wait(), yes.Confirmed!.Action);
        Assert.Null(flow.Confirmation);

        flow.Present(Answer(InterpretKind.Confirm, action: GameAction.Wait()));
        Assert.Equal(FreeTextFlow.Step.Confirmed, flow.Submit("Yes", available: true).Step);
    }

    [Fact]
    public void No_drops_the_question_and_any_other_text_replaces_it_with_a_new_line()
    {
        var flow = new FreeTextFlow();
        flow.Present(Answer(InterpretKind.Confirm, action: GameAction.Wait()));
        Assert.Equal(FreeTextFlow.Step.Dropped, flow.Submit("no", available: true).Step);
        Assert.Null(flow.Confirmation);

        flow.Present(Answer(InterpretKind.Confirm, action: GameAction.Wait()));
        var next = flow.Submit("take the shovel", available: true);
        Assert.Equal(FreeTextFlow.Step.Ask, next.Step);
        Assert.Equal("take the shovel", next.Text);
        Assert.Null(flow.Confirmation);
    }

    [Fact]
    public void Each_answer_kind_has_one_readable_line_and_only_confirm_keeps_a_question()
    {
        var flow = new FreeTextFlow();

        Assert.Equal("-> WAIT (HERE)", flow.Present(Answer(InterpretKind.Accept)).Text);
        Assert.Equal("CAN'T DIG: ASPHALT (HERE) · TOO HARD",
            flow.Present(Answer(InterpretKind.Blocked, "dig: Asphalt (here)", "too hard")).Text);
        Assert.Equal("NOTHING IN REACH MATCHES THAT.", flow.Present(Answer(InterpretKind.Reject, "", "Nothing in reach matches that.")).Text);
        Assert.Equal("warn", flow.Present(Answer(InterpretKind.Unavailable, "", "free text needs a Jev key")).Category);
        Assert.Null(flow.Confirmation);
    }
}
