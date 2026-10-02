using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

/// <summary>
/// A streaming world builds chunks when something reads them and stores only what a mutation changed
/// (Dev-010): a read writes nothing, a modified chunk survives eviction and a restart, and a save is
/// never regenerated.
/// </summary>
public sealed class ChunkStreamingRules : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"etherbound-stream-{Guid.NewGuid():N}.db");

    /// <summary>Niko's spawn kit (6 objects) plus each Extra's worn backpack and its apples (2 rows, Dev-011 D6).</summary>
    private static readonly int StreamingObjects = 6 + 2 * Population.ExtraCount;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
        File.Delete(_path + "-wal");
        File.Delete(_path + "-shm");
    }

    private WorldEngine NewEngine(long seed = 7)
    {
        var engine = new WorldEngine(_path);
        engine.NewGame(seed, "infinite");
        return engine;
    }

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private (long Chunks, long Levels, long Objects, long Events, long Inputs) Rows() => (Scalar("SELECT COUNT(*) FROM chunk"),
        Scalar("SELECT COUNT(*) FROM chunk_level"), Scalar("SELECT COUNT(*) FROM object"), Scalar("SELECT COUNT(*) FROM event"),
        Scalar("SELECT COALESCE(SUM(repeat_count), 0) FROM input_journal"));

    private static ActorRow Niko(WorldEngine engine) => engine.OpenSession().GetActor(Ids.Player)!;

    private static (int Cx, int Cy) ChunkOf(ActorRow actor) =>
        (PyMath.FloorDiv(actor.TileX, ChunkConst.Size), PyMath.FloorDiv(actor.TileY, ChunkConst.Size));

    private static void Dig(WorldEngine engine)
    {
        var niko = Niko(engine);
        var result = engine.Submit(Ids.Player, GameAction.On("dig", new TileTarget(niko.TileX, niko.TileY, niko.H)));
        Assert.True(result.Accepted, result.Reason);
        for (var i = 0; i < result.Activity!.EndsMinute - result.Activity.StartedMinute; i++) engine.AdvanceTime();
    }

    [Fact]
    public void A_new_streaming_game_stores_no_chunk_and_gives_niko_the_spawn_kit()
    {
        using var engine = NewEngine();
        Assert.True(engine.Grid.Streaming);
        Assert.NotNull(engine.Source);
        var rows = Rows();
        Assert.Equal((0, 0), (rows.Chunks, rows.Levels));
        Assert.Equal(StreamingObjects, rows.Objects);
        var niko = Niko(engine);
        Assert.Contains(engine.Grid.StandingSurfaces(niko.TileX, niko.TileY), s => s.H == niko.H);
        // The chunk Niko stands in is loaded and its kit is indexed for menus and the frame.
        var (cx, cy) = ChunkOf(niko);
        Assert.NotEmpty(engine.Grid.ObjectsAtChunk(cx, cy).Concat(engine.Grid.ObjectsAtChunk(cx + 1, cy)).Concat(engine.Grid.ObjectsAtChunk(cx - 1, cy))
            .Concat(engine.Grid.ObjectsAtChunk(cx, cy + 1)).Concat(engine.Grid.ObjectsAtChunk(cx, cy - 1)));
    }

    [Fact]
    public void Reads_over_never_seen_ground_write_no_row_event_or_journal_entry()
    {
        using var engine = NewEngine();
        var before = Rows();
        var niko = Niko(engine);
        var loaded = engine.Grid.Chunks.Count;

        engine.Menu(Ids.Player, niko.X + 400.5, niko.Y - 300.5, 0, 1);
        engine.ChunksNear(ChunkOf(niko).Cx + 30, ChunkOf(niko).Cy - 30, 2);
        engine.Pick(new WorldRay(niko.X + 500, niko.Y + 500, 10, 0.3, 0.2, -0.5));
        var start = new Spot(niko.TileX, niko.TileY, niko.H);
        Nav.FindPath(engine.Grid, start, new Spot(niko.TileX + 20, niko.TileY, niko.H), 500);
        engine.Grid.StandingSurfaces(niko.TileX - 900, niko.TileY + 900);

        Assert.True(engine.Grid.Chunks.Count > loaded);
        Assert.Equal(before, Rows());
    }

    [Fact]
    public void A_dig_persists_survives_eviction_and_reloads_after_a_restart()
    {
        int x, y, dug, material;
        (int Cx, int Cy) key;
        using (var engine = NewEngine())
        {
            var niko = Niko(engine);
            (x, y, key) = (niko.TileX, niko.TileY, ChunkOf(niko));
            var revision = engine.Grid.Chunk(key.Cx, key.Cy)!.Revision;
            Dig(engine);
            var ground = engine.Grid.GroundAt(x, y)!.Value;
            (dug, material) = (ground.Dug, ground.SurfaceMat);
            Assert.Equal(1, dug);
            Assert.Equal(revision + 1, engine.Grid.Chunk(key.Cx, key.Cy)!.Revision);
            Assert.Equal(1, Rows().Chunks);

            // Evicted entirely, the chunk comes back from its stored row, not from the generator.
            Assert.True(engine.EvictFarChunks(-1) > 0);
            Assert.Empty(engine.Grid.Chunks);
            Assert.Equal((dug, material), (engine.Grid.GroundAt(x, y)!.Value.Dug, engine.Grid.GroundAt(x, y)!.Value.SurfaceMat));
            Assert.Equal(revision + 1, engine.Grid.Chunk(key.Cx, key.Cy)!.Revision);
        }
        SqliteConnection.ClearAllPools();
        using var reopened = new WorldEngine(_path);
        reopened.EnsureWorld(0);
        Assert.Equal((dug, material), (reopened.Grid.GroundAt(x, y)!.Value.Dug, reopened.Grid.GroundAt(x, y)!.Value.SurfaceMat));
        Assert.Single(reopened.Grid.Chunks.Values, c => c.Revision > 0);
    }

    [Fact]
    public void A_save_is_never_regenerated_and_keeps_its_objects_and_its_version()
    {
        long events;
        using (var first = NewEngine())
        {
            Assert.Equal(StreamingObjects, Rows().Objects);
            events = Rows().Events;
        }
        SqliteConnection.ClearAllPools();
        for (var open = 0; open < 2; open++)
        {
            using var engine = new WorldEngine(_path);
            engine.EnsureWorld(0);
            var world = engine.OpenSession().World;
            Assert.Equal(("infinite", EndlessWorldVersion), (world.Generator, world.GenVersion));
            Assert.Equal(StreamingObjects, Rows().Objects);
            Assert.Equal(events, Rows().Events);
            Assert.Empty(engine.ReadEvents(0, 50, "world.generated").Skip(1));
            Assert.True(engine.Grid.Streaming);
        }
    }

    private const int EndlessWorldVersion = World.Gen.EndlessWorld.GenVersion;

    [Fact]
    public void A_chunk_is_the_same_whatever_order_the_player_explored_in()
    {
        using var a = NewEngine();
        var keys = new[] { (3, -2), (-5, 8), (0, 0), (40, 40), (-1, -1) };
        foreach (var (cx, cy) in keys) a.Grid.Chunk(cx, cy);
        var forward = keys.ToDictionary(k => k, k => a.Grid.Chunk(k.Item1, k.Item2)!.GroundBlob);

        using var b = new WorldEngine();
        b.NewGame(7, "infinite");
        foreach (var (cx, cy) in keys.Reverse()) b.Grid.Chunk(cx, cy);
        foreach (var key in keys) Assert.Equal(forward[key], b.Grid.Chunk(key.Item1, key.Item2)!.GroundBlob);
    }

    [Fact]
    public void Eviction_keeps_memory_bounded_and_never_drops_what_is_near_an_actor()
    {
        using var engine = NewEngine();
        for (var i = 0; i < 400; i++) engine.Grid.Chunk(50 + i % 20, -60 + i / 20);
        var niko = Niko(engine);
        var (cx, cy) = ChunkOf(niko);
        engine.Grid.Chunk(cx, cy);
        Assert.True(engine.Grid.Chunks.Count >= 400);

        var dropped = engine.EvictFarChunks();
        var side = 2 * WorldEngine.EvictRadius + 1;
        Assert.True(dropped >= 400);
        Assert.True(engine.Grid.Chunks.Count <= side * side * engine.OpenSession().Actors().Count);
        Assert.NotNull(engine.Grid.Chunk(cx, cy));
        Assert.True(engine.Grid.Chunks.ContainsKey((cx, cy)));
        // Evicting again with nothing far away drops nothing.
        Assert.Equal(0, engine.EvictFarChunks());
    }

    [Fact]
    public void Prefetch_loads_one_chunk_nearest_first_until_the_ring_is_full()
    {
        using var engine = NewEngine();
        var (cx, cy) = ChunkOf(Niko(engine));
        engine.EvictFarChunks(-1);
        var steps = 0;
        while (engine.PrefetchChunk(cx, cy, 3)) steps++;
        Assert.Equal(49, steps);
        Assert.Equal(49, engine.Grid.Chunks.Count);
        Assert.False(engine.PrefetchChunk(cx, cy, 3));
    }

    [Fact]
    public void Replaying_the_journal_rebuilds_the_same_modified_world()
    {
        IReadOnlyList<InputJournalEntry> journal;
        (double X, double Y, int H) niko;
        List<(int, int, int)> modified;
        JsonObject state;
        using (var original = NewEngine())
        {
            original.SetClock(paused: false);
            for (var i = 0; i < 6; i++) Assert.True(original.Submit(Ids.Player, GameAction.Move(1, 0), 1.0).Accepted);
            Dig(original);
            for (var i = 0; i < 40; i++) original.Submit(Ids.Player, GameAction.Move(0, -1), 1.0);
            journal = original.ReadInputJournal();
            var row = Niko(original);
            niko = (row.X, row.Y, row.H);
            modified = original.Grid.Chunks.Values.Where(c => c.Revision > 0).Select(c => (c.Cx, c.Cy, c.Revision)).OrderBy(c => c).ToList();
            state = StateDump.Of(original);
        }
        Assert.NotEmpty(modified);

        using var replay = new WorldEngine();
        InputReplay.Replay(replay, journal);
        var replayed = Niko(replay);
        Assert.Equal(niko, (replayed.X, replayed.Y, replayed.H));
        Assert.Equal(modified, replay.Grid.Chunks.Values.Where(c => c.Revision > 0).Select(c => (c.Cx, c.Cy, c.Revision)).OrderBy(c => c).ToList());
        Assert.Equal("infinite", replay.OpenSession().World.Generator);
        Assert.True(Json.Same(state, StateDump.Of(replay)));
        Assert.True(Json.Same(ReplayCheck.Essential(state), ReplayCheck.Essential(StateDump.Of(replay))));
    }

    [Fact]
    public void A_new_game_over_an_old_streaming_save_does_not_inherit_its_modified_chunks()
    {
        int x, y;
        using (var engine = NewEngine())
        {
            var niko = Niko(engine);
            (x, y) = (niko.TileX, niko.TileY);
            Dig(engine);
            Assert.Equal(1, Rows().Chunks);
            engine.NewGame(7, "infinite");
            Assert.Equal(0, Rows().Chunks);
            Assert.Equal(0, engine.Grid.GroundAt(x, y)!.Value.Dug);
            Assert.Equal(StreamingObjects, Rows().Objects);
        }
    }

    [Fact]
    public void A_bounded_generator_still_loads_its_whole_world()
    {
        using var engine = new WorldEngine(_path);
        engine.NewGame(7, "test");
        Assert.False(engine.Grid.Streaming);
        Assert.Null(engine.Source);
        Assert.Equal(64, engine.Grid.Chunks.Count);
        Assert.Equal(64, Rows().Chunks);
        Assert.Equal(0, engine.EvictFarChunks(-1));
        Assert.Equal(64, engine.Grid.Chunks.Count);
    }
}
