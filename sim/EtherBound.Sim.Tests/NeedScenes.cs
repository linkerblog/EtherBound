using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Tests;

/// <summary>
/// Scene setup shared by the needs tests: put an actor, a clock and a few objects exactly where a rule
/// needs them. These write rows directly (as the other brain tests do), so a scene is never replayed.
/// </summary>
internal static class NeedScenes
{
    public static WorldEngine NewEngine(long seed = 0)
    {
        var engine = new WorldEngine();
        engine.EnsureWorld(seed);
        return engine;
    }

    public static void SetMinute(WorldEngine engine, int minute)
    {
        var session = engine.OpenSession();
        session.World.GameMinute = minute;
        session.Commit();
    }

    /// <summary>Overrides some levels at the current game minute; the rest are fully satisfied.</summary>
    public static void SetNeeds(WorldEngine engine, string actorId, double hunger = 1.0, double thirst = 1.0, double rest = 1.0)
    {
        var session = engine.OpenSession();
        var minute = session.World.GameMinute;
        session.GetActor(actorId)!.Needs = ActorNeeds.Full(minute)
            .With(NeedCatalog.Hunger, hunger, minute).With(NeedCatalog.Thirst, thirst, minute).With(NeedCatalog.Rest, rest, minute).ToJson();
        session.Commit();
    }

    public static void Teleport(WorldEngine engine, string actorId, int tileX, int tileY)
    {
        var session = engine.OpenSession();
        var actor = session.GetActor(actorId)!;
        var h = engine.Grid.StandingSurfaces(tileX, tileY)[0].H;
        (actor.X, actor.Y, actor.H, actor.Z) = (tileX + 0.5, tileY + 0.5, h, PyMath.FloorDiv(h, 6));
        actor.Activity = null;
        session.Commit();
    }

    /// <summary>Lays a stack on a tile's ground and returns its object id.</summary>
    public static int PlaceOnTile(WorldEngine engine, string kind, int tileX, int tileY, int quantity = 1)
    {
        var session = engine.OpenSession();
        var row = session.AddObject(new ObjectRow { Kind = kind, Loc = "tile", Quantity = quantity });
        ObjectHelpers.SetTile(row, tileX, tileY, engine.Grid.GroundAt(tileX, tileY)!.Value.GroundH);
        session.Commit();
        engine.Reindex();
        return row.Id;
    }

    /// <summary>Deletes every object of a kind, wherever it is, so a rule starts with no source in the world.</summary>
    public static void RemoveAll(WorldEngine engine, string kind)
    {
        var session = engine.OpenSession();
        foreach (var row in session.Objects().Where(o => o.Kind == kind)) session.DeleteObject(row);
        session.Commit();
        engine.Reindex();
    }

    public static ActorState Actor(WorldEngine engine, string id) => engine.GetState().Actors.First(a => a.Id == id);

    public static double Level(WorldEngine engine, string actorId, string need)
    {
        var state = engine.GetState();
        return state.Actors.First(a => a.Id == actorId).Needs!.Level(need, state.GameMinute);
    }

    public static List<EventRow> LoggedEvents(WorldEngine engine, string type, string actorId) =>
        engine.ReadEvents(0, 1_000_000, type).Where(e => e.ActorId == actorId).ToList();

    /// <summary>The first shallow-water tile in the test world's pond, scanning outward from its centre row.</summary>
    public static (int X, int Y) ShallowWater(WorldEngine engine)
    {
        for (var x = 196; x <= 215; x++)
            if (engine.Grid.GroundAt(x, 70) is { } ground && engine.Registry.Get(ground.SurfaceMat) is { Drinkable: true })
                return (x, 70);
        throw new InvalidOperationException("the test world has no shallow water on row 70");
    }
}
