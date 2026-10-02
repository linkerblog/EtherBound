using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Llm.Bench;

public static class BenchWorld
{
    /// <summary>
    /// The bench needs objects to talk about, so Niko is placed beside the test world's shovel and
    /// backpack by editing his row directly, the way tests and tooling may (never a handler).
    /// </summary>
    public static void StandBesideTheObjects(WorldEngine engine)
    {
        var session = engine.OpenSession();
        var niko = session.GetActor(Ids.Player)!;
        niko.X = 123.5;
        niko.Y = 126.5;
        // Standing on the ground of that tile, or every reach check would read a stale height.
        var ground = engine.Grid.StandingSurfaces(123, 126).OrderBy(surface => surface.H).First();
        niko.H = ground.H;
        niko.Z = ground.Z;
        session.Commit();
        engine.Reindex();
    }
}
