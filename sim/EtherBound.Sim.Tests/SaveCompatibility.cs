using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Tests;

/// <summary>
/// A save written by the Python server at <c>0008_extra</c> opens in the C# sim with no
/// migration, reads back the same state and log, and survives a write and a reopen.
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
        Assert.Equal(Database0008, Scalar("SELECT version_num FROM alembic_version"));
    }

    private const string Database0008 = "0008_extra";

    [Fact]
    public void A_write_round_trips_and_leaves_alembic_alone()
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
        Assert.Equal(Database0008, Scalar("SELECT version_num FROM alembic_version"));
        Assert.Equal("sim-0008", Scalar("SELECT version FROM sim_schema"));
    }
}
