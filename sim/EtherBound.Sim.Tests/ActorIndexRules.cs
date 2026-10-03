using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

/// <summary>
/// <c>Session.ActorsNear</c> against the all-actor filter it replaced (Fix20): the same actors in the same
/// order for any square, whatever the session has moved, added or deleted and whatever has been committed.
/// </summary>
public sealed class ActorIndexRules : IDisposable
{
    private const int Crowd = 150;
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"etherbound-actor-index-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    private static List<(double X, double Y, double R)> Queries(int count)
    {
        var rng = new Random(11);
        var queries = new List<(double, double, double)>();
        // Mostly the bay the crowd stands in, some squares off it and a few wide ones that take the scan path.
        for (var i = 0; i < count; i++)
            queries.Add((rng.NextDouble() * 140, rng.NextDouble() * 140, i % 17 == 0 ? 5 + rng.NextDouble() * 60 : rng.NextDouble() * 4));
        queries.Add((0, 0, 0));
        queries.Add((-3.5, 130.2, 2));
        return queries;
    }

    private static IEnumerable<string> Brute(Session session, (double X, double Y, double R) q)
    {
        int x0 = (int)Math.Floor(q.X - q.R), x1 = (int)Math.Floor(q.X + q.R);
        int y0 = (int)Math.Floor(q.Y - q.R), y1 = (int)Math.Floor(q.Y + q.R);
        return session.Actors().Where(a => a.TileX >= x0 && a.TileX <= x1 && a.TileY >= y0 && a.TileY <= y1).Select(a => a.Id);
    }

    // The narrow squares come first: a wide one makes a session load the whole crowd, and from then on that
    // session answers from its own rows and no longer touches the index this test is about.
    private static void AssertSame(Session indexed, Session reference)
    {
        var queries = Queries(300);
        // Random squares rarely land on a particular actor, so also ask at every actor's own spot.
        queries.AddRange(reference.Actors().SelectMany(a => new[] { (a.X, a.Y, 0.0), (a.X, a.Y, 1.5) }));
        foreach (var q in queries.OrderBy(q => q.R))
            Assert.Equal(Brute(reference, q).ToList(), indexed.ActorsNear(q.X, q.Y, q.R).Select(a => a.Id).ToList());
    }

    private static List<string> Ids(WorldEngine engine) => engine.OpenSession().Actors().Select(a => a.Id).ToList();

    // Moves a few actors a long way and a few a little, deletes some and adds one, the same way twice. It asks
    // for rows by id, never for the whole crowd, so the session stays on the index path.
    private static void Shuffle(Session session, IReadOnlyList<string> ids)
    {
        var rng = new Random(5);
        foreach (var id in ids.Where((_, i) => i % 7 == 0))
        {
            var row = session.GetActor(id)!;
            if (rng.Next(2) == 0) (row.X, row.Y) = (4 + rng.NextDouble() * 120, 4 + rng.NextDouble() * 120);
            else (row.X, row.Y) = (row.X + 0.9, row.Y - 0.9);
        }
        foreach (var id in ids.Where((_, i) => i % 11 == 5))
            session.DeleteActor(session.GetActor(id)!);
        session.AddActor(new ActorRow { Id = "zz-new", Kind = "extra", Name = "zz-new", X = 60.5, Y = 60.5, H = 0, Z = 0 });
    }

    [Fact]
    public void A_committed_crowd_answers_like_the_filter()
    {
        using var engine = CrowdReplayRules.SeededCrowd(Crowd);
        using var other = CrowdReplayRules.SeededCrowd(Crowd);
        AssertSame(engine.OpenSession(), other.OpenSession());
    }

    [Fact]
    public void A_session_sees_its_own_moves_additions_and_deletions_before_they_commit()
    {
        using var engine = CrowdReplayRules.SeededCrowd(Crowd);
        var indexed = engine.OpenSession();
        var reference = engine.OpenSession();
        var ids = Ids(engine);
        Shuffle(indexed, ids);
        Shuffle(reference, ids);

        AssertSame(indexed, reference);
        Assert.Contains(indexed.ActorsNear(60.5, 60.5, 0), a => a.Id == "zz-new");
    }

    [Fact]
    public void A_commit_moves_the_index_with_the_rows_and_a_discarded_session_leaves_it_alone()
    {
        using var engine = CrowdReplayRules.SeededCrowd(Crowd);
        var ids = Ids(engine);
        var discarded = engine.OpenSession();
        Shuffle(discarded, ids);
        _ = discarded.ActorsNear(60, 60, 3);

        using var untouched = CrowdReplayRules.SeededCrowd(Crowd);
        AssertSame(engine.OpenSession(), untouched.OpenSession());

        var committed = engine.OpenSession();
        Shuffle(committed, ids);
        committed.Commit();
        var reference = untouched.OpenSession();
        Shuffle(reference, ids);
        reference.Commit();
        AssertSame(engine.OpenSession(), untouched.OpenSession());
    }

    private static ActorRow OnGround(WorldEngine engine, string id, int x, int y)
    {
        var ground = engine.Grid.GroundAt(x, y) ?? throw new InvalidOperationException($"no ground at {x}, {y}");
        return new ActorRow { Id = id, Kind = "extra", Name = id, X = x + 0.5, Y = y + 0.5, H = ground.GroundH, Z = PyMath.FloorDiv(ground.GroundH, 6) };
    }

    // Two neighbouring tiles an actor can stand on, so the engine's settling of a reloaded save leaves them be.
    private static int StandingPair(WorldEngine engine)
    {
        for (var x = 8; x < 120; x++)
            if (Enumerable.Range(0, 2).All(dx => engine.Grid.GroundAt(x + dx, 60) is { } g &&
                    engine.Grid.StandingSurfaces(x + dx, 60).Any(s => s.H == g.GroundH))) return x;
        throw new InvalidOperationException("no two standing tiles in a row");
    }

    [Fact]
    public void A_new_game_forgets_the_old_crowd_and_a_reloaded_save_rebuilds_the_index()
    {
        int x;
        using (var engine = new WorldEngine(_databasePath))
        {
            engine.NewGame(7, "lab");
            x = StandingPair(engine);
            var session = engine.OpenSession();
            session.AddActor(OnGround(engine, "kept-1", x, 60));
            session.AddActor(OnGround(engine, "kept-2", x + 1, 60));
            session.Commit();
            Assert.Equal(new[] { "kept-1", "kept-2" }, engine.OpenSession().ActorsNear(x + 1, 60.5, 1).Select(a => a.Id));
        }

        using var reloaded = new WorldEngine(_databasePath);
        reloaded.EnsureWorld(7);
        Assert.Equal(new[] { "kept-1", "kept-2" }, reloaded.OpenSession().ActorsNear(x + 1, 60.5, 1).Select(a => a.Id));

        reloaded.NewGame(8, "lab");
        Assert.DoesNotContain(reloaded.OpenSession().ActorsNear(x + 1, 60.5, 5), a => a.Id.StartsWith("kept-", StringComparison.Ordinal));
    }

    [Fact]
    public void An_actor_saved_off_every_surface_is_found_at_spawn_after_a_reload_and_not_where_it_was()
    {
        using (var engine = new WorldEngine(_databasePath))
        {
            engine.NewGame(7, "lab");
            var session = engine.OpenSession();
            session.AddActor(new ActorRow { Id = "lost-1", Kind = "extra", Name = "lost", X = 400.5, Y = 400.5, H = 0, Z = 0 });
            session.Commit();
            Assert.Single(engine.OpenSession().ActorsNear(400.5, 400.5, 0));
        }

        using var reloaded = new WorldEngine(_databasePath);
        reloaded.EnsureWorld(7);
        var now = reloaded.OpenSession().GetActor("lost-1")!;
        Assert.NotEqual(400, now.TileX);
        Assert.Empty(reloaded.OpenSession().ActorsNear(400.5, 400.5, 1));
        Assert.Contains(reloaded.OpenSession().ActorsNear(now.X, now.Y, 0), a => a.Id == "lost-1");
    }
}
