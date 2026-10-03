using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Sim.Tests;

/// <summary>
/// A move must cost what its neighbourhood costs, not what the crowd costs (Fix20). Counted in bytes the move
/// allocates, not in time, so a loaded machine cannot fail it: copying every actor row on each submit made a
/// move allocate about 26 KB per actor (1.5 MB at 50 actors, 5.3 MB at 200); it is now flat at about 50 KB.
/// </summary>
public sealed class CrowdCostRules
{
    private static long BytesPerMove(int crowd)
    {
        using var engine = CrowdReplayRules.SeededCrowd(crowd);
        engine.SetClock(speed: 10);
        // The first moves pay for JIT and caches; Niko walks east and west so he never leaves the bay.
        for (var i = 0; i < 20; i++) engine.Submit(Ids.Player, GameAction.Move(i % 2 == 0 ? 1 : -1, 0), 0.05);
        const int Moves = 40;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Moves; i++) engine.Submit(Ids.Player, GameAction.Move(i % 2 == 0 ? 1 : -1, 0), 0.05);
        return (GC.GetAllocatedBytesForCurrentThread() - before) / Moves;
    }

    [Fact]
    public void A_move_allocates_the_same_in_a_crowd_of_two_hundred_as_in_a_crowd_of_fifty()
    {
        var small = BytesPerMove(50);
        var large = BytesPerMove(200);

        Assert.True(large < small * 1.5, $"a move allocates {small} bytes among 50 actors and {large} among 200");
    }
}
