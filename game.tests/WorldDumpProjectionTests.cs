using System.Collections.Immutable;
using EtherBound.Game.Spike;
using EtherBound.Host;
using EtherBound.Sim.Core;
using Xunit;

namespace EtherBound.Game.Tests;

public sealed class WorldDumpProjectionTests
{
    [Fact]
    public void Unchanged_host_chunks_reuse_their_render_projection()
    {
        var hostChunk = MakeChunk(1);
        var first = WorldDump.FromFrame(MakeFrame(hostChunk, 1));
        var second = WorldDump.FromFrame(MakeFrame(hostChunk, 2), first);
        Assert.Same(first.Chunks[(0, 0)], second.Chunks[(0, 0)]);
        Assert.Same(first.Levels[(0, 0)], second.Levels[(0, 0)]);

        var changed = WorldDump.FromFrame(MakeFrame(MakeChunk(2), 3), second);
        Assert.NotSame(second.Chunks[(0, 0)], changed.Chunks[(0, 0)]);
        Assert.NotSame(second.Levels[(0, 0)], changed.Levels[(0, 0)]);
    }

    private static WorldFrame MakeFrame(HostChunk chunk, long sequence) => new(sequence, 7, 0, 1, false, "test", 1, "{}",
        ImmutableArray<HostGenerator>.Empty,
        ImmutableArray.Create(new HostActor(Ids.Player, "human", null, 1, 1, 0, 0, null,
            ImmutableArray<HostCarriedObject>.Empty, 0)),
        ImmutableArray<HostMaterial>.Empty, ImmutableArray<HostObjectKind>.Empty,
        ImmutableArray.Create(chunk), 0);

    private static HostChunk MakeChunk(int revision)
    {
        var count = WorldDump.CellCount;
        var level = new HostLevel(0, ImmutableArray.CreateRange(new short[count]), ImmutableArray.CreateRange(new ushort[count]),
            ImmutableArray.CreateRange(new ushort[count]), ImmutableArray.CreateRange(new ushort[count]),
            ImmutableArray.CreateRange(new byte[count]), ImmutableArray.CreateRange(new byte[count]),
            ImmutableArray.CreateRange(new byte[count]), ImmutableArray.CreateRange(new ushort[count]));
        return new HostChunk(0, 0, revision, ImmutableArray.CreateRange(new short[count]),
            ImmutableArray.CreateRange(new ushort[count]), ImmutableArray.CreateRange(new byte[count]),
            ImmutableArray<HostStratum>.Empty, ImmutableArray.Create(level), ImmutableArray<HostObject>.Empty);
    }
}
