using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

public sealed class ActorMovementRules : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"etherbound-actor-movement-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    private WorldEngine Fresh()
    {
        var engine = new WorldEngine(_databasePath);
        engine.EnsureWorld(123);
        return engine;
    }

    private static ActorRow PositionActors(WorldEngine engine, double x, double y, int h,
        double otherX, double otherY, int otherH)
    {
        var session = engine.OpenSession();
        var actors = session.Actors();
        var player = session.GetActor(Ids.Player)!;
        (player.X, player.Y, player.H, player.Z) = (x, y, h, PyMath.FloorDiv(h, 6));
        var other = actors.First(actor => actor.Id != Ids.Player);
        (other.X, other.Y, other.H, other.Z) = (otherX, otherY, otherH, PyMath.FloorDiv(otherH, 6));

        var index = 0;
        foreach (var actor in actors.Where(actor => actor.Id != Ids.Player && actor.Id != other.Id))
            (actor.X, actor.Y) = (8 + index++ * 3, 8.5);
        session.Commit();
        return other;
    }

    [Fact]
    public void Same_level_actor_body_overlap_blocks_without_moving_either_actor()
    {
        using var engine = Fresh();
        var other = PositionActors(engine, 121.5, 128.5, 2, 122.0, 128.5, 2);

        var result = engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.05);

        Assert.True(result.Accepted);
        Assert.Equal((121.5, 128.5), (result.X, result.Y));
        Assert.Equal((122.0, 128.5, 2), Position(engine, other.Id));
        Assert.Empty(engine.ReadEvents(type: "actor.moved", actorId: Ids.Player));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void Actor_body_overlap_uses_the_standing_height_interval(int otherH, bool shouldBlock)
    {
        using var engine = Fresh();
        PositionActors(engine, 121.5, 128.5, 2, 122.0, 128.5, otherH);

        var result = engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.05);

        Assert.True(result.Accepted);
        if (shouldBlock) Assert.Equal(121.5, result.X, 6);
        else Assert.True(result.X > 121.5);
    }

    [Fact]
    public void Actor_collision_survives_a_save_restart()
    {
        using (var engine = Fresh())
            PositionActors(engine, 121.5, 128.5, 2, 122.0, 128.5, 2);

        SqliteConnection.ClearAllPools();
        using var reopened = Fresh();
        var result = reopened.Submit(Ids.Player, GameAction.Move(1, 0), 0.05);

        Assert.True(result.Accepted);
        Assert.Equal(121.5, result.X, 6);
        Assert.Empty(reopened.ReadEvents(type: "actor.moved", actorId: Ids.Player));
    }

    [Fact]
    public void An_actor_can_move_out_of_existing_overlap_without_worsening_it()
    {
        using var engine = Fresh();
        PositionActors(engine, 121.5, 128.5, 2, 121.8, 128.5, 2);

        var result = engine.Submit(Ids.Player, GameAction.Move(-1, 0), 0.05);

        Assert.True(result.Accepted);
        Assert.True(result.X < 121.5);
        Assert.True(Math.Abs(result.X - 121.8) > 0.3);
    }

    [Fact]
    public void Held_diagonal_at_test_spawn_keeps_clearance_without_lateral_zigzag()
    {
        using var engine = Fresh();
        var session = engine.OpenSession();
        var index = 0;
        foreach (var actor in session.Actors().Where(actor => actor.Id != Ids.Player))
            (actor.X, actor.Y) = (8 + index++ * 3, 8.5);
        session.Commit();

        var samples = new List<double>();
        for (var step = 0; step < 100; step++)
        {
            var result = engine.Submit(Ids.Player, GameAction.Move(1, 1), 0.05);
            Assert.True(result.Accepted);
            samples.Add(result.X);
        }

        Assert.All(samples.Zip(samples.Skip(1)), pair => Assert.True(pair.Second >= pair.First));
        Assert.InRange(samples[^1], 135.69, 135.71);
        Assert.True(engine.GetState().Actors.Single(actor => actor.Id == Ids.Player).Y > 142);
    }

    private static (double X, double Y, int H) Position(WorldEngine engine, string id)
    {
        var actor = engine.GetState().Actors.Single(actor => actor.Id == id);
        return (actor.X, actor.Y, actor.H);
    }
}
