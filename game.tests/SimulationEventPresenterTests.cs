using System.Collections.Immutable;
using EtherBound.Game.App;
using EtherBound.Host;
using EtherBound.Sim.Core;
using Xunit;

namespace EtherBound.Game.Tests;

public sealed class SimulationEventPresenterTests
{
    [Fact]
    public void Work_completion_with_a_detail_event_renders_one_summary()
    {
        var events = ImmutableArray.Create(
            new HostSimulationEvent(10, 15, "terrain.dug", Ids.Player,
                "{\"removed\":\"topsoil\",\"exposed\":\"dirt\",\"dug\":1}"),
            new HostSimulationEvent(11, 15, "activity.finished", Ids.Player,
                "{\"op\":\"dig\",\"outcome\":\"completed\",\"reason\":null}"));

        Assert.True(SimulationEventPresenter.TryFormat(events, out var text, out var category));
        Assert.Equal("DUG TOPSOIL", text);
        Assert.Equal("act", category);
    }

    [Fact]
    public void Activity_notice_is_rendered_when_no_more_specific_event_exists()
    {
        var events = ImmutableArray.Create(new HostSimulationEvent(12, 15, "activity.finished", Ids.Player,
            "{\"op\":\"wait\",\"outcome\":\"interrupted\",\"reason\":\"move\"}"));

        Assert.True(SimulationEventPresenter.TryFormat(events, out var text, out var category));
        Assert.Equal("WAIT INTERRUPTED move", text);
        Assert.Equal("fail", category);
    }

    [Fact]
    public void Non_presentational_movement_events_do_not_create_feed_rows()
    {
        var events = ImmutableArray.Create(new HostSimulationEvent(13, 15, "actor.moved", Ids.Player, "{}"));

        Assert.False(SimulationEventPresenter.TryFormat(events, out var text, out _));
        Assert.Empty(text);
    }
}
