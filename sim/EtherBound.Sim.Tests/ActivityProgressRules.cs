using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

public sealed class ActivityProgressRules : IDisposable
{
    private const int RoadX = 123;
    private const int TargetX = 124;
    private const int Y = 128;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"etherbound-work-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private WorldEngine NewEngine()
    {
        var engine = new WorldEngine(_path);
        engine.EnsureWorld(0);
        PlacePlayer(engine);
        return engine;
    }

    private static void PlacePlayer(WorldEngine engine)
    {
        var session = engine.OpenSession();
        foreach (var actor in session.Actors().Where(actor => actor.Id != Ids.Player))
        {
            actor.X = 250.5;
            actor.Y = 250.5;
            actor.H = 0;
            actor.Z = 0;
        }
        var player = session.GetActor(Ids.Player)!;
        player.X = RoadX + 0.5;
        player.Y = Y + 0.5;
        player.H = engine.Grid.GroundAt(RoadX, Y)!.Value.GroundH;
        player.Z = PyMath.FloorDiv(player.H, 6);
        session.Commit();
    }

    private static void MovePlayer(WorldEngine engine, double x, double y)
    {
        var session = engine.OpenSession();
        var player = session.GetActor(Ids.Player)!;
        player.X = x;
        player.Y = y;
        player.H = engine.Grid.GroundAt((int)x, (int)y)!.Value.GroundH;
        player.Z = PyMath.FloorDiv(player.H, 6);
        session.Commit();
    }

    private static List<EventRow> Events(WorldEngine engine) => engine.ReadEvents(0, 1000);

    private static void Ticks(WorldEngine engine, int count)
    {
        for (var i = 0; i < count; i++) engine.AdvanceTime();
    }

    [Fact]
    public void Interrupted_dig_work_resumes_after_save_restart_and_revalidation()
    {
        var dig = GameAction.On("dig", new TileTarget(TargetX, Y, 2));
        using (var engine = NewEngine())
        {
            var started = engine.Submit(Ids.Player, dig);
            Assert.True(started.Accepted);
            Assert.Equal(96, started.Activity!.EndsMinute - started.Activity.StartedMinute);
            Ticks(engine, 5);
            Assert.True(engine.Submit(Ids.Player, GameAction.Move(0, 1), 0.05).Accepted);
            Assert.Null(engine.GetState().Actors.Single(actor => actor.Id == Ids.Player).Activity);
            Assert.DoesNotContain(Events(engine), e => e.Type == "terrain.dug");
            MovePlayer(engine, 110.5, 120.5);
            Assert.False(engine.Submit(Ids.Player, dig).Accepted);
            Assert.DoesNotContain(Events(engine), e => e.Type == "terrain.dug");
        }

        SqliteConnection.ClearAllPools();
        using var resumed = new WorldEngine(_path);
        resumed.EnsureWorld(0);
        PlacePlayer(resumed);
        Assert.True(resumed.Submit(Ids.Player, dig).Accepted);
        var activity = resumed.GetState().Actors.Single(actor => actor.Id == Ids.Player).Activity!;
        Assert.Equal((0, 96), (activity.StartedMinute, activity.EndsMinute));
        Ticks(resumed, 90);
        Assert.DoesNotContain(Events(resumed), e => e.Type == "terrain.dug");
        Ticks(resumed, 1);
        Assert.Single(Events(resumed), e => e.Type == "terrain.dug");
    }
}
