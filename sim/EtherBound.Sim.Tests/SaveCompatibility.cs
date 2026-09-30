using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

/// <summary>
/// A save written by the Python server at <c>0008_extra</c> opens in the C# sim, is upgraded in
/// place to <c>0010_replay_work</c> by the migration runner, reads back the same state and log, and
/// survives a write and a reopen.
/// </summary>
public sealed class SaveCompatibility : IDisposable
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"etherbound-save-{Environment.ProcessId}-{Guid.NewGuid():N}.db");

    public SaveCompatibility() => File.Copy(Path.Combine(Fixtures, "python-0008.db"), _path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private static JsonNode Expected() => JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "python-0008.json")))!;

    private static void AssertSameState(JsonNode expected, JsonObject actual)
    {
        foreach (var key in new[] { "world", "actors", "objects", "chunk_revisions", "blocks_sha256", "wall_integrity" })
            Assert.True(Json.Same(expected[key], actual[key]), $"{key}\nexpected: {expected[key]?.ToJsonString()}\nactual:   {actual[key]?.ToJsonString()}");
    }

    private string Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private List<(int Cx, int Cy, int Z, int Cell, string Slot, double Joules)> WallSlotRows() =>
        Query("SELECT cx, cy, z, cell_index, slot, joules FROM wall_slot ORDER BY cx, cy, z, cell_index, slot");

    private List<(int Cx, int Cy, int Z, int Cell, string Slot, double Joules)> Query(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var r = command.ExecuteReader();
        var rows = new List<(int, int, int, int, string, double)>();
        while (r.Read()) rows.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetString(4), r.GetDouble(5)));
        return rows;
    }

    /// <summary>The `0008` rows the fixture had, as `(cx, cy, z, cell, slot, joules)`.</summary>
    private static List<(int Cx, int Cy, int Z, int Cell, string Slot, double Joules)> FixtureRows() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "python-0008.json")))!["state"]!["wall_integrity"]!.AsArray()
            .Select(row => row!.AsArray())
            .Select(row => (row[0]!.GetValue<int>(), row[1]!.GetValue<int>(), row[2]!.GetValue<int>(), row[3]!.GetValue<int>(),
                row[4]!.GetValue<string>(), row[5]!.GetValue<double>()))
            .ToList();

    [Fact]
    public void Python_save_opens_with_the_same_state_and_log()
    {
        var expected = Expected();
        using var engine = new WorldEngine(_path);
        engine.EnsureWorld(0);
        AssertSameState(expected["state"]!, StateDump.Of(engine));
        var events = engine.ReadEvents(0, 100000);
        var goldenEvents = expected["events"]!.AsArray();
        Assert.Equal(goldenEvents.Count, events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var ours = Json.Obj(("seq", e.Seq), ("game_minute", e.GameMinute), ("type", e.Type), ("actor_id", e.ActorId), ("data", e.Data.DeepClone()));
            Assert.True(Json.Same(goldenEvents[i], ours), $"event {i}");
        }
        Assert.Equal(DatabaseHead, Scalar("SELECT version_num FROM alembic_version"));
    }

    private const string DatabaseHead = "0010_replay_work";

    [Fact]
    public void A_0008_save_is_upgraded_in_place_and_keeps_its_wall_rows()
    {
        Assert.Equal(Database0008, Database0008FixtureVersion());
        using (var engine = new WorldEngine(_path))
        {
            engine.EnsureWorld(0);
        }
        Assert.Equal(DatabaseHead, Scalar("SELECT version_num FROM alembic_version"));
        Assert.Equal("sim-0010", Scalar("SELECT version FROM sim_schema"));
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'input_journal'"));
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'activity_work'"));
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM pragma_table_info('chunk_level') WHERE name = 'slot_mask'"));
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM pragma_table_info('chunk_level') WHERE name = 'slot_mat'"));
        // Every wall_integrity row became a wall_slot row and the old table is gone.
        Assert.Equal(FixtureRows(), WallSlotRows());
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'wall_integrity'"));
    }

    /// <summary>
    /// A save an older build already left at <c>0009_walls</c> must resume at the next step, not replay
    /// the wall migration: replaying it would add <c>slot_mask</c> twice and refuse to open.
    /// </summary>
    [Fact]
    public void A_save_already_at_0009_resumes_at_0010_without_replaying_the_walls_migration()
    {
        ApplyWallsMigration();
        Assert.Equal(Database0009, Scalar("SELECT version_num FROM alembic_version"));
        using (var engine = new WorldEngine(_path))
        {
            engine.EnsureWorld(0);
        }
        Assert.Equal(DatabaseHead, Scalar("SELECT version_num FROM alembic_version"));
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM pragma_table_info('chunk_level') WHERE name = 'slot_mask'"));
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM pragma_table_info('chunk_level') WHERE name = 'slot_mat'"));
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'input_journal'"));
        Assert.Equal(FixtureRows(), WallSlotRows());
    }

    /// <summary>Moves the fixture copy to <c>0009_walls</c>, the state an older build left behind.</summary>
    private void ApplyWallsMigration()
    {
        using var stream = typeof(EtherBound.Sim.Db.Database).Assembly
            .GetManifestResourceStream("EtherBound.Sim.Db.Schema0009_walls.sql")!;
        using var reader = new StreamReader(stream);
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
    }

    private const string Database0009 = "0009_walls";
    private const string Database0008 = "0008_extra";

    /// <summary>Reads the pristine fixture's version, before any other test upgrades the copy.</summary>
    private static string Database0008FixtureVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etherbound-fixture-{Guid.NewGuid():N}.db");
        try
        {
            File.Copy(Path.Combine(Fixtures, "python-0008.db"), path);
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT version_num FROM alembic_version";
            return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void An_unknown_version_is_refused_instead_of_guessed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etherbound-future-{Guid.NewGuid():N}.db");
        try
        {
            File.Copy(Path.Combine(Fixtures, "python-0008.db"), path);
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE alembic_version SET version_num = '0010_future'";
                command.ExecuteNonQuery();
            }
            var error = Assert.Throws<InvalidOperationException>(() => new WorldEngine(path));
            Assert.Contains("0010_future", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void A_file_save_runs_in_wal_and_leaves_no_sidecar_after_dispose()
    {
        using (var engine = new WorldEngine(_path))
        {
            engine.EnsureWorld(0);
            Assert.True(engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.05).Accepted);
            Assert.True(File.Exists(_path + "-wal"));
        }
        Assert.False(File.Exists(_path + "-wal"));
        Assert.False(File.Exists(_path + "-shm"));
        Assert.Equal("wal", Scalar("PRAGMA journal_mode"));
    }

    [Fact]
    public void An_open_save_commits_without_a_full_sync()
    {
        using var db = new EtherBound.Sim.Db.Database(_path);
        Assert.Equal("wal", db.PragmaValue("journal_mode"));
        Assert.Equal("1", db.PragmaValue("synchronous"));
    }

    [Fact]
    public void A_memory_world_keeps_its_memory_journal()
    {
        using var db = new EtherBound.Sim.Db.Database(":memory:");
        Assert.Equal("memory", db.PragmaValue("journal_mode"));
    }

    [Fact]
    public void A_write_round_trips_and_stays_at_the_head()
    {
        JsonObject before;
        using (var engine = new WorldEngine(_path))
        {
            engine.EnsureWorld(0);
            Assert.True(engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.5).Accepted);
            engine.AdvanceTime();
            before = StateDump.Of(engine);
        }
        SqliteConnection.ClearAllPools();
        using (var reopened = new WorldEngine(_path))
        {
            reopened.EnsureWorld(0);
            AssertSameState(before, StateDump.Of(reopened));
        }
        Assert.Equal(DatabaseHead, Scalar("SELECT version_num FROM alembic_version"));
        Assert.Equal("sim-0010", Scalar("SELECT version FROM sim_schema"));
    }
}
