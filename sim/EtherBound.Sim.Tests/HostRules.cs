using EtherBound.Host;
using EtherBound.Sim.Core;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Tests;

public sealed class HostRules
{
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
    }
}
