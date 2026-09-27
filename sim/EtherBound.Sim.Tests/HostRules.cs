using System.Text.Json.Nodes;
using EtherBound.Host;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Tests;

public sealed class HostRules
{
    [Fact]
    public void Physics_action_responses_copy_mutable_json_identifiers()
    {
        var action = GameAction.Vector("throw", new ObjectTarget(42), 1, 0);
        var trajectory = new[]
        {
            PhysicsPosition.Actor(Ids.Player, 1, 2, 3),
            new PhysicsPosition("object", JsonValue.Create(42)!, 2, 3, 4),
        };
        var result = new ActionResult(true, Ids.Player, action, 1, 2, 0, 3, null, null, null,
            Array.Empty<CarriedObject>(), 0, trajectory);

        var detached = HostActionResult.Copy(9, result);

        Assert.Equal("niko", detached.Trajectory[0].ActorId);
        Assert.Equal(42, detached.Trajectory[1].ObjectId);
        Assert.Null(detached.Trajectory[1].ActorId);
    }

    [Fact]
    public void A_default_move_lasts_one_move_step()
    {
        var delta = typeof(SimulationHost).GetMethod(nameof(SimulationHost.TryMove))!.GetParameters()[2].DefaultValue;

        Assert.Equal(1.0 / SimulationHost.MoveHz, (double)delta!);
    }

    [Fact]
    public void Frames_count_every_move_the_host_handled_even_one_that_goes_nowhere()
    {
        using var host = new SimulationHost(seed: 7, generator: "lab");
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, host.LatestFrame!.MovesApplied);

        Assert.True(host.TryMove(1, 0));
        Assert.True(host.TryMove(0, 0));
        Assert.True(host.TryMove(0, 1));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (host.LatestFrame!.MovesApplied < 3 && DateTime.UtcNow < deadline)
            host.WaitForFrameAfter(host.LatestFrame.Sequence, TimeSpan.FromMilliseconds(200));
        Assert.Equal(3, host.LatestFrame!.MovesApplied);
    }

    [Fact]
    public void Host_publishes_a_detached_frame_and_runs_the_clock_on_its_worker()
    {
        using var host = new SimulationHost(seed: 7, generator: "lab");
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(30)));
        var initial = host.LatestFrame!;
        Assert.NotEqual(Environment.CurrentManagedThreadId, host.WorkerThreadId);
        Assert.Equal(7, initial.Seed);
        Assert.Equal("lab", initial.Generator);
        Assert.Contains(initial.Actors, actor => actor.Id == Ids.Player);
        Assert.NotEmpty(initial.Chunks);
        Assert.All(initial.Chunks, chunk => Assert.Equal(ChunkConst.CellCount, chunk.GroundH.Length));
        Assert.Contains(initial.Generators, spec => spec.Key == "lab" && spec.Fields.Any(field => field.Path == "relief.amplitude"));

        var initialPlayer = initial.Actors.Single(actor => actor.Id == Ids.Player);
        Assert.True(host.TryMove(1, 0));
        Assert.True(host.WaitForFrameAfter(initial.Sequence, TimeSpan.FromSeconds(5)));
        var moved = host.LatestFrame!;
        var movedPlayer = moved.Actors.Single(actor => actor.Id == Ids.Player);
        Assert.True(movedPlayer.X > initialPlayer.X);

        Assert.True(host.TrySetClock(speed: 10));
        Assert.True(host.WaitForFrameAfter(moved.Sequence, TimeSpan.FromSeconds(5)));
        var clockChanged = host.LatestFrame!;
        Assert.Equal(10, clockChanged.Speed);
        Assert.True(host.WaitForFrameAfter(clockChanged.Sequence, TimeSpan.FromSeconds(5)));

        Assert.True(host.LatestFrame!.GameMinute > initial.GameMinute);

        Assert.True(host.TryRequestRadialMenu(1));
        var radial = ReadResponse<HostMenuResponse>(host, 1);
        Assert.NotNull(radial.Menu);
        Assert.Contains(radial.Menu.Entries, entry => entry.Op == "wait");

        var player = host.LatestFrame!.Actors.Single(actor => actor.Id == Ids.Player);
        var ray = new WorldRay(player.X, player.Y, player.H * 0.5 + 3, 0, 0, -1);
        Assert.True(host.TryRequestMenuAtRay(2, ray));
        var menu = ReadResponse<HostMenuResponse>(host, 2);
        Assert.IsType<ActorTarget>(menu.Hit!.Target);
        Assert.Contains(menu.Menu!.Entries, entry => entry.Op == "wait");

        Assert.True(host.TrySubmitAction(3, GameAction.Wait()));
        var action = ReadResponse<HostActionResponse>(host, 3);
        Assert.True(action.Result.Accepted, action.Result.Reason);
        Assert.NotNull(action.Result.Activity);
        Assert.True(SpinWait.SpinUntil(() => ReadAvailableNotice(host), TimeSpan.FromSeconds(5)));

        const string options = "{\"feature\":\"relief\",\"spawn_bay\":\"feature\",\"relief\":{\"amplitude\":4}}";
        Assert.True(host.TryNewGame(4, 11, "lab", options, paused: true));
        var newGame = ReadResponse<HostNewGameResponse>(host, 4);
        Assert.Equal("lab", newGame.Generator);
        Assert.True(SpinWait.SpinUntil(() => host.LatestFrame is { Generator: "lab", Paused: true } frame && frame.Seed == 11,
            TimeSpan.FromSeconds(5)));
        Assert.Contains("\"amplitude\":4", host.LatestFrame!.GenOptionsJson, StringComparison.Ordinal);
    }

    private static T ReadResponse<T>(SimulationHost host, int requestId) where T : HostResponse
    {
        HostResponse? response = null;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            while (host.TryReadResponse(out var next))
                if (next!.RequestId == requestId)
                {
                    response = next;
                    return true;
                }
            return false;
        }, TimeSpan.FromSeconds(5)));
        return Assert.IsType<T>(response);
    }

    private static bool ReadAvailableNotice(SimulationHost host)
    {
        while (host.TryReadResponse(out var response))
            if (response is HostActivityNotice { Op: "wait", Outcome: "completed" }) return true;
        return false;
    }
}
