using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

/// <summary>Movement, restart and new-game rules (ported from <c>test_engine.py</c>).</summary>
public sealed class EngineRules : IDisposable
{
    private static readonly MaterialRegistry Registry = MaterialRegistry.Load();
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"etherbound-engine-{Guid.NewGuid():N}.db");

    private WorldEngine Fresh()
    {
        var engine = new WorldEngine(_path);
        engine.EnsureWorld(123);
        return engine;
    }

    private WorldEngine Restart(long seed = 123)
    {
        SqliteConnection.ClearAllPools();
        var restarted = new WorldEngine(_path);
        restarted.EnsureWorld(seed);
        return restarted;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    /// <summary>Teleport Niko for a test: the engine loads chunks eagerly, so this is read-only setup.</summary>
    private static void PlaceActor(WorldEngine engine, double x, double y)
    {
        int tx = (int)x, ty = (int)y;
        var surface = engine.Grid.StandingSurfaces(tx, ty).OrderByDescending(s => s.H).First();
        var session = engine.OpenSession();
        var actor = session.GetActor(Ids.Player)!;
        (actor.X, actor.Y) = (x, y);
        (actor.H, actor.Z) = (surface.H, PyMath.FloorDiv(surface.H, 6));
        session.Commit();
    }

    [Fact]
    public void Move_uses_the_action_api_and_tracks_height()
    {
        using var engine = Fresh();
        var result = engine.Submit(Ids.Player, GameAction.Move(1, 0), 2);
        Assert.True(result.Accepted);
        Assert.True(result.X > 0.5);
        Assert.True(result.H >= 0);
    }

    [Fact]
    public void Climbing_the_building_stairs_raises_h_and_z()
    {
        using var engine = Fresh();
        PlaceActor(engine, 137.5, 150.5);
        var result = engine.Submit(Ids.Player, GameAction.Move(1, 0), 10);
        Assert.True(result.Accepted);
        Assert.Equal(18, result.H);
        Assert.Equal(3, result.Z);
    }

    private static WorldGrid FlatGrid() => new(new[] { Chunk.Flat(0, 0, 4, Registry["grass"].Id) }, registry: Registry);

    private static WorldGrid SlopedGrid()
    {
        var heights = Enumerable.Range(0, ChunkConst.CellCount).Select(i => (short)Math.Min(4 + i % 32, 10)).ToArray();
        return new WorldGrid(new[] { new Chunk(0, 0, heights, Enumerable.Repeat((ushort)Registry["grass"].Id, ChunkConst.CellCount).ToArray()) }, registry: Registry);
    }

    [Fact]
    public void Uphill_walking_covers_less_distance_than_flat()
    {
        var (flatX, _, _) = Movement.MoveInWorld(0.5, 16.5, 4, 1, 0, 8.0, FlatGrid());
        var (uphillX, _, uphillH) = Movement.MoveInWorld(0.5, 16.5, 4, 1, 0, 8.0, SlopedGrid());
        Assert.True(flatX - 0.5 > uphillX - 0.5);
        Assert.True(uphillH > 4);
    }

    [Fact]
    public void Ten_half_metre_slope_steps_are_walkable_both_ways()
    {
        var heights = Enumerable.Range(0, ChunkConst.CellCount).Select(i => (short)Math.Min(i % 32, 10)).ToArray();
        var grid = new WorldGrid(new[] { new Chunk(0, 0, heights, Enumerable.Repeat((ushort)Registry["grass"].Id, ChunkConst.CellCount).ToArray()) }, registry: Registry);
        foreach (var offset in new[] { 0.2, 0.5, 0.8 })
        {
            var (uphillX, _, uphillH) = Movement.MoveInWorld(offset, 16.5, 0, 1, 0, 17, grid);
            Assert.Equal(10, uphillH);
            var (_, _, downhillH) = Movement.MoveInWorld(uphillX, 16.5, uphillH, -1, 0, 20, grid);
            Assert.Equal(0, downhillH);
        }
    }

    [Fact]
    public void Sand_grass_and_asphalt_have_distinct_movement_distances()
    {
        double DistanceOn(string material)
        {
            var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 0, Registry[material].Id) }, registry: Registry);
            var (x, _, _) = Movement.MoveInWorld(0.5, 16.5, 0, 1, 0, 8, grid);
            return x - 0.5;
        }
        var (grass, sand, asphalt) = (DistanceOn("grass"), DistanceOn("sand"), DistanceOn("asphalt"));
        Assert.True(sand < grass && grass < asphalt);
    }

    [Fact]
    public void A_body_stops_three_tenths_of_a_metre_from_a_wall()
    {
        var grid = new WorldGrid(new[] { Chunk.Flat(0, 0, 0, Registry["grass"].Id) }, registry: Registry);
        var level = ChunkLevel.Empty(0, 0, 0);
        var wallW = new ushort[ChunkConst.CellCount];
        wallW[1] = (ushort)Registry["brick"].Id;
        grid.AddLevel(level.With(wallW: wallW));
        var (x, _, _) = Movement.MoveInWorld(0.5, 0.5, 0, 1, 0, 1, grid);
        Assert.Equal(0.7, x, 3);
    }

    [Fact]
    public void A_paused_clock_rejects_movement()
    {
        using var engine = Fresh();
        engine.SetClock(paused: true);
        var result = engine.Submit(Ids.Player, GameAction.Move(1, 0), 1);
        Assert.False(result.Accepted);
        Assert.Equal("paused", result.Reason);
    }

    [Fact]
    public void The_game_minute_persists_for_a_restarted_engine()
    {
        using var engine = Fresh();
        engine.AdvanceTime();
        Assert.Equal(1, engine.GetState().GameMinute);
        using var restarted = Restart();
        Assert.Equal(1, restarted.GetState().GameMinute);
    }

    [Fact]
    public void New_game_wipes_and_regenerates()
    {
        using var engine = Fresh();
        var state = engine.NewGame(999);
        Assert.Equal(999, state.Seed);
        Assert.NotEmpty(engine.Grid.Chunks);
        var niko = state.Actors.First(a => a.Id == Ids.Player);
        var surface = Movement.NearestSurface(engine.Grid, niko.X, niko.Y, niko.H);
        Assert.NotNull(surface);
        Assert.Equal(niko.H, surface!.Value.H);
    }

    [Fact]
    public void New_game_stores_the_generator_and_its_options_and_survives_a_downgrade()
    {
        using var engine = Fresh();
        var options = Json.Obj(("feature", "relief"), ("relief", Json.Obj(("amplitude", 20))));
        var state = engine.NewGame(5, "lab", options);
        Assert.Equal(("lab", Lab.GenVersion), (state.Generator, state.GenVersion));
        Assert.Equal("relief", state.GenOptions["feature"]!.GetValue<string>());
        Assert.Equal(20, Json.ToInt(state.GenOptions["relief"]!["amplitude"]!));
        var niko = state.Actors.First(a => a.Id == Ids.Player);
        Assert.Equal((61.5, 41.5, 2), (niko.X, niko.Y, niko.H));
        var generated = engine.ReadEvents(0, 50, "world.generated");
        var payload = generated[^1].Data;
        Assert.Equal("lab", payload["generator"]!.GetValue<string>());
        Assert.Equal("relief", payload["options"]!["feature"]!.GetValue<string>());

        using (var restarted = Restart())
        {
            Assert.Equal("lab", restarted.GetState().Generator);
            Assert.Equal(20, Json.ToInt(restarted.GetState().GenOptions["relief"]!["amplitude"]!));
        }
        // Lowering the stored version regenerates with the stored options, not the defaults.
        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE world_meta SET gen_version = 0";
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        using var regenerated = Restart();
        Assert.Equal("lab", regenerated.GetState().Generator);
        Assert.Equal(20, Json.ToInt(regenerated.GetState().GenOptions["relief"]!["amplitude"]!));
        var hills = Enumerable.Range(44, 36).SelectMany(x => Enumerable.Range(44, 36).Select(y => regenerated.Grid.GroundAt(x, y)));
        Assert.Contains(hills, tile => tile is not null && tile.Value.GroundH != 2);
    }

    [Fact]
    public void An_unknown_generator_falls_back_to_test()
    {
        using (var engine = Fresh())
        {
            var session = engine.OpenSession();
            var world = session.World;
            world.Generator = "nope";
            world.GenVersion = 1;
            session.Commit();
        }
        SqliteConnection.ClearAllPools();
        using var restarted = Restart();
        var state = restarted.GetState();
        Assert.Equal("test", state.Generator);
        Assert.True(state.GenVersion >= 5);
    }

    private static ActorState Niko(WorldEngine engine) => engine.GetState().Actors.First(a => a.Id == Ids.Player);

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void A_loaded_actor_with_a_stale_h_snaps_to_its_surface(int offset)
    {
        using var engine = Fresh();
        var before = Niko(engine);
        var session = engine.OpenSession();
        var actor = session.GetActor(Ids.Player)!;
        actor.H = before.H + offset;
        actor.Z = PyMath.FloorDiv(actor.H, 6);
        session.Commit();
        var stored = engine.ReadEvents(0, 500).Count;

        using var restarted = Restart();
        var after = Niko(restarted);
        Assert.Equal((before.X, before.Y, before.H, before.Z), (after.X, after.Y, after.H, after.Z));
        Assert.Equal(stored, restarted.ReadEvents(0, 500).Count);

        ActionResult? result = null;
        for (var i = 0; i < 20; i++) result = restarted.Submit(Ids.Player, GameAction.Move(1, 0));
        Assert.True(result!.Accepted);
        var moves = restarted.ReadEvents(0, 500, "actor.moved", Ids.Player);
        Assert.NotEmpty(moves);
        Assert.Equal(((int)before.X, (int)before.Y, before.H), (moves[0].Data["from_tile"]!.Int("x"), moves[0].Data["from_tile"]!.Int("y"), moves[0].Data["from_tile"]!.Int("h")));
    }

    [Fact]
    public void A_loaded_actor_with_no_surface_nearby_is_relocated()
    {
        using var engine = Fresh();
        var spawn = Niko(engine);
        PlaceActor(engine, spawn.X + 8, spawn.Y);
        var session = engine.OpenSession();
        var actor = session.GetActor(Ids.Player)!;
        actor.H += 3;
        actor.Z = PyMath.FloorDiv(actor.H, 6);
        session.Commit();

        using var restarted = Restart();
        var after = Niko(restarted);
        Assert.Equal((spawn.X, spawn.Y, spawn.H), (after.X, after.Y, after.H));
        var spawned = restarted.ReadEvents(0, 500, "actor.spawned", Ids.Player);
        Assert.Equal("relocated", spawned[^1].Data["reason"]!.GetValue<string>());
    }
}
