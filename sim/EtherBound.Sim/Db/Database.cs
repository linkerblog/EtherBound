using System.Globalization;
using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Db;

/// <summary>
/// The SQLite save, same schema as the Python server at <c>0008_extra</c> (Dev-025 [Sec. 1]). A new
/// save is created from the exact Alembic DDL with <c>alembic_version</c> set, so both runtimes
/// open it; an existing save must already be at <c>0008_extra</c>. The sim records its own
/// <c>sim_schema</c> row and never touches <c>alembic_version</c> until cut-over.
/// </summary>
public sealed class Database : IDisposable
{
    public const string AlembicHead = "0008_extra";
    public const string SimSchema = "sim-0008";

    private readonly SqliteConnection _connection;
    private SqliteTransaction? _writeBatch;
    private long _savepointId;

    public Database(string path)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = path == ":memory:" ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate };
        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();
        EnsureSchema();
    }

    public void Dispose() => _connection.Dispose();

    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction ?? _writeBatch;
        return command;
    }

    internal void BeginWriteBatch()
    {
        if (_writeBatch is not null) throw new InvalidOperationException("a write batch is already active");
        _writeBatch = _connection.BeginTransaction();
    }

    internal void EndWriteBatch()
    {
        var batch = _writeBatch ?? throw new InvalidOperationException("no write batch is active");
        _writeBatch = null;
        try
        {
            batch.Commit();
        }
        finally
        {
            batch.Dispose();
        }
    }

    private bool TableExists(string name)
    {
        using var command = Command("SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name");
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private void EnsureSchema()
    {
        if (!TableExists("alembic_version"))
        {
            using var stream = typeof(Database).Assembly.GetManifestResourceStream("EtherBound.Sim.Db.Schema0008.sql")!;
            using var reader = new StreamReader(stream);
            using var transaction = _connection.BeginTransaction();
            using (var create = Command(reader.ReadToEnd(), transaction)) create.ExecuteNonQuery();
            using (var version = Command("INSERT INTO alembic_version (version_num) VALUES ($v)", transaction))
            {
                version.Parameters.AddWithValue("$v", AlembicHead);
                version.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        using (var head = Command("SELECT version_num FROM alembic_version"))
        {
            var found = head.ExecuteScalar() as string;
            if (found != AlembicHead)
                throw new InvalidOperationException($"save is at {found ?? "no version"}; open it once with the Python server to reach {AlembicHead}");
        }
        using var sim = Command("CREATE TABLE IF NOT EXISTS sim_schema (id INTEGER NOT NULL PRIMARY KEY, version VARCHAR(32) NOT NULL);" +
            $"INSERT OR IGNORE INTO sim_schema (id, version) VALUES (1, '{SimSchema}');");
        sim.ExecuteNonQuery();
    }

    // --- load --------------------------------------------------------------------------------

    public void Load(WorldStore store)
    {
        using (var command = Command("SELECT seed, game_minute, speed, paused, gen_version, generator, gen_options FROM world_meta WHERE id = 1"))
        using (var r = command.ExecuteReader())
            if (r.Read())
                store.Meta = new WorldMetaRow
                {
                    Seed = r.GetInt64(0), GameMinute = r.GetInt32(1), Speed = r.GetInt32(2), Paused = r.GetBoolean(3),
                    GenVersion = r.GetInt32(4), Generator = r.GetString(5), GenOptions = Json.Parse(r.GetString(6)),
                };
        using (var command = Command("SELECT key, id FROM material"))
        using (var r = command.ExecuteReader())
            while (r.Read()) store.MaterialIds[r.GetString(0)] = r.GetInt32(1);
        using (var command = Command("SELECT id, kind, name, x, y, z, h, mass_kg, activity, mind FROM actor"))
        using (var r = command.ExecuteReader())
            while (r.Read())
            {
                var row = new ActorRow
                {
                    Id = r.GetString(0), Kind = r.GetString(1), Name = r.IsDBNull(2) ? null : r.GetString(2),
                    X = r.GetDouble(3), Y = r.GetDouble(4), Z = r.GetInt32(5), H = r.GetInt32(6), MassKg = r.GetDouble(7),
                    Activity = JsonColumn(r, 8), Mind = JsonColumn(r, 9),
                };
                store.Actors[row.Id] = row;
            }
        using (var command = Command("SELECT id, kind, loc, x, y, h, cx, cy, container_id, actor_id, slot, quantity, state, integrity, owner FROM object"))
        using (var r = command.ExecuteReader())
            while (r.Read())
            {
                var row = new ObjectRow
                {
                    Id = r.GetInt32(0), Kind = r.GetString(1), Loc = r.GetString(2), X = NullInt(r, 3), Y = NullInt(r, 4),
                    H = NullInt(r, 5), Cx = NullInt(r, 6), Cy = NullInt(r, 7), ContainerId = NullInt(r, 8),
                    ActorId = r.IsDBNull(9) ? null : r.GetString(9), Slot = r.IsDBNull(10) ? null : r.GetString(10),
                    Quantity = r.GetInt32(11), State = JsonColumn(r, 12) ?? new JsonObject(),
                    Integrity = r.IsDBNull(13) ? null : r.GetDouble(13), Owner = r.IsDBNull(14) ? null : r.GetString(14),
                };
                store.Objects[row.Id] = row;
            }
        using (var command = Command("SELECT cx, cy, z, cell_index, edge, integrity FROM wall_integrity"))
        using (var r = command.ExecuteReader())
            while (r.Read())
                store.Walls[new WallKey(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetString(4))] = r.GetDouble(5);
        using (var command = Command("SELECT COALESCE(MAX(seq), 0) FROM event"))
            store.MaxEventSeq = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary><c>world_setup.load_grid</c>: chunks by (cx, cy), levels by (cx, cy, z).</summary>
    public (List<Chunk> Chunks, List<ChunkLevel> Levels) LoadGrid()
    {
        var chunks = new List<Chunk>();
        using (var command = Command("SELECT cx, cy, ground_h, surface_mat, strata, revision, gen_version, dug FROM chunk ORDER BY cx, cy"))
        using (var r = command.ExecuteReader())
            while (r.Read())
            {
                var strata = JsonNode.Parse(r.GetString(4))!.AsArray()
                    .Select(layer => new Stratum(Json.ToInt(layer![0]!), layer[1]!.GetValue<string>())).ToList();
                chunks.Add(Chunk.FromBlobs(r.GetInt32(0), r.GetInt32(1), Blob(r, 2)!, Blob(r, 3)!, strata, r.GetInt32(5),
                    r.GetInt32(6), Blob(r, 7)));
            }
        var levels = new List<ChunkLevel>();
        using (var command = Command("SELECT cx, cy, z, floor_h, floor_mat, wall_n, wall_w, edge_flags, flags FROM chunk_level ORDER BY cx, cy, z"))
        using (var r = command.ExecuteReader())
            while (r.Read())
                levels.Add(ChunkLevel.FromBlobs(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), Blob(r, 3)!, Blob(r, 4)!, Blob(r, 5)!,
                    Blob(r, 6)!, Blob(r, 7)!, Blob(r, 8)!));
        return (chunks, levels);
    }

    public bool HasChunks()
    {
        using var command = Command("SELECT 1 FROM chunk LIMIT 1");
        return command.ExecuteScalar() is not null;
    }

    public List<EventRow> ReadEvents(int afterSeq, int limit, string? type, string? actorId)
    {
        var sql = "SELECT seq, game_minute, type, actor_id, data FROM event WHERE seq > $after";
        if (type is not null) sql += " AND type = $type";
        if (actorId is not null) sql += " AND actor_id = $actor";
        using var command = Command(sql + " ORDER BY seq LIMIT $limit");
        command.Parameters.AddWithValue("$after", afterSeq);
        command.Parameters.AddWithValue("$limit", limit);
        if (type is not null) command.Parameters.AddWithValue("$type", type);
        if (actorId is not null) command.Parameters.AddWithValue("$actor", actorId);
        var rows = new List<EventRow>();
        using var r = command.ExecuteReader();
        while (r.Read())
            rows.Add(new EventRow(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), Json.Parse(r.GetString(4))));
        return rows;
    }

    /// <summary>Material rows as <c>sync_materials</c> leaves them: new keys inserted, resistance refreshed.</summary>
    public void SyncMaterials(MaterialRegistry registry, IReadOnlyDictionary<string, int> existing)
    {
        using var transaction = _connection.BeginTransaction();
        foreach (var material in registry.Materials)
        {
            if (existing.ContainsKey(material.Key))
            {
                using var update = Command("UPDATE material SET resistance = $r WHERE key = $k", transaction);
                update.Parameters.AddWithValue("$r", material.Resistance);
                update.Parameters.AddWithValue("$k", material.Key);
                update.ExecuteNonQuery();
                continue;
            }
            using var insert = Command("INSERT INTO material (id, key, name, color, walkable, walk_cost, solid, blocks_sight, " +
                "diggable, dig_cost, flammable, density, resistance, liquid, tags) VALUES ($id, $key, $name, $color, $walkable, " +
                "$walk_cost, $solid, $blocks_sight, $diggable, $dig_cost, $flammable, $density, $resistance, $liquid, $tags)", transaction);
            var p = insert.Parameters;
            p.AddWithValue("$id", material.Id);
            p.AddWithValue("$key", material.Key);
            p.AddWithValue("$name", material.Name);
            p.AddWithValue("$color", material.Color);
            p.AddWithValue("$walkable", material.Walkable);
            p.AddWithValue("$walk_cost", material.WalkCost);
            p.AddWithValue("$solid", material.Solid);
            p.AddWithValue("$blocks_sight", material.BlocksSight);
            p.AddWithValue("$diggable", material.Diggable);
            p.AddWithValue("$dig_cost", material.DigCost);
            p.AddWithValue("$flammable", material.Flammable);
            p.AddWithValue("$density", material.Density);
            p.AddWithValue("$resistance", material.Resistance);
            p.AddWithValue("$liquid", material.Liquid);
            p.AddWithValue("$tags", new JsonArray(material.Tags.Select(t => (JsonNode)JsonValue.Create(t)).ToArray()).ToJsonString());
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    // --- write -------------------------------------------------------------------------------

    public void Write(Changes changes)
    {
        if (_writeBatch is { } batch)
        {
            var savepoint = $"eb_write_{++_savepointId}";
            Exec(batch, $"SAVEPOINT {savepoint}");
            try
            {
                WriteChanges(batch, changes);
                Exec(batch, $"RELEASE SAVEPOINT {savepoint}");
            }
            catch
            {
                Exec(batch, $"ROLLBACK TO SAVEPOINT {savepoint}");
                Exec(batch, $"RELEASE SAVEPOINT {savepoint}");
                throw;
            }
            return;
        }

        using var transaction = _connection.BeginTransaction();
        WriteChanges(transaction, changes);
        transaction.Commit();
    }

    private void WriteChanges(SqliteTransaction t, Changes changes)
    {
        if (changes.Wipe)
        {
            Exec(t, "DELETE FROM wall_integrity");
            Exec(t, "DELETE FROM chunk_level");
            Exec(t, "DELETE FROM chunk");
            Exec(t, "DELETE FROM object");
            if (changes.WipeActors) Exec(t, "DELETE FROM actor");
            if (changes.WipeEvents) Exec(t, "DELETE FROM event");
        }
        if (changes.Meta is { } meta)
        {
            using var command = Command("INSERT OR REPLACE INTO world_meta (id, seed, game_minute, speed, paused, gen_version, generator, gen_options) " +
                "VALUES (1, $seed, $minute, $speed, $paused, $gen_version, $generator, $options)", t);
            var p = command.Parameters;
            p.AddWithValue("$seed", meta.Seed);
            p.AddWithValue("$minute", meta.GameMinute);
            p.AddWithValue("$speed", meta.Speed);
            p.AddWithValue("$paused", meta.Paused);
            p.AddWithValue("$gen_version", meta.GenVersion);
            p.AddWithValue("$generator", meta.Generator);
            p.AddWithValue("$options", meta.GenOptions.ToJsonString());
            command.ExecuteNonQuery();
        }
        foreach (var id in changes.DeletedObjects) Exec(t, "DELETE FROM object WHERE id = $id", ("$id", id));
        foreach (var id in changes.DeletedActors) Exec(t, "DELETE FROM actor WHERE id = $id", ("$id", id));
        foreach (var row in changes.InsertedActors.Concat(changes.UpdatedActors)) WriteActor(t, row);
        foreach (var row in changes.InsertedObjects.Concat(changes.UpdatedObjects).OrderBy(o => o.Id)) WriteObject(t, row);
        foreach (var key in changes.DeletedWalls)
            Exec(t, "DELETE FROM wall_integrity WHERE cx = $cx AND cy = $cy AND z = $z AND cell_index = $i AND edge = $e",
                ("$cx", key.Cx), ("$cy", key.Cy), ("$z", key.Z), ("$i", key.CellIndex), ("$e", key.Edge));
        foreach (var (key, value) in changes.Walls)
            Exec(t, "INSERT OR REPLACE INTO wall_integrity (cx, cy, z, cell_index, edge, integrity) VALUES ($cx, $cy, $z, $i, $e, $v)",
                ("$cx", key.Cx), ("$cy", key.Cy), ("$z", key.Z), ("$i", key.CellIndex), ("$e", key.Edge), ("$v", value));
        foreach (var chunk in changes.Chunks)
            Exec(t, "INSERT OR REPLACE INTO chunk (cx, cy, ground_h, surface_mat, strata, revision, gen_version, dug) " +
                "VALUES ($cx, $cy, $g, $s, $strata, $rev, $gv, $dug)",
                ("$cx", chunk.Cx), ("$cy", chunk.Cy), ("$g", chunk.GroundBlob), ("$s", chunk.SurfaceBlob), ("$strata", chunk.StrataJson()),
                ("$rev", chunk.Revision), ("$gv", chunk.GenVersion), ("$dug", chunk.DugBlob));
        foreach (var level in changes.Levels)
            Exec(t, "INSERT OR REPLACE INTO chunk_level (cx, cy, z, floor_h, floor_mat, wall_n, wall_w, edge_flags, flags) " +
                "VALUES ($cx, $cy, $z, $f, $fm, $wn, $ww, $ef, $fl)",
                ("$cx", level.Cx), ("$cy", level.Cy), ("$z", level.Z), ("$f", level.FloorBlob), ("$fm", level.FloorMatBlob),
                ("$wn", level.WallNBlob), ("$ww", level.WallWBlob), ("$ef", level.EdgeFlagsBlob), ("$fl", level.FlagsBlob));
        foreach (var e in changes.Events)
            Exec(t, "INSERT INTO event (seq, game_minute, type, actor_id, data) VALUES ($seq, $minute, $type, $actor, $data)",
                ("$seq", e.Seq), ("$minute", e.GameMinute), ("$type", e.Type), ("$actor", e.ActorId), ("$data", e.Data.ToJsonString()));
    }

    private void WriteActor(SqliteTransaction t, ActorRow a) =>
        Exec(t, "INSERT OR REPLACE INTO actor (id, kind, x, y, z, h, activity, mass_kg, name, mind) " +
            "VALUES ($id, $kind, $x, $y, $z, $h, $activity, $mass, $name, $mind)",
            ("$id", a.Id), ("$kind", a.Kind), ("$x", a.X), ("$y", a.Y), ("$z", a.Z), ("$h", a.H),
            ("$activity", a.Activity?.ToJsonString()), ("$mass", a.MassKg), ("$name", a.Name), ("$mind", a.Mind?.ToJsonString()));

    private void WriteObject(SqliteTransaction t, ObjectRow o) =>
        Exec(t, "INSERT OR REPLACE INTO object (id, kind, loc, x, y, h, cx, cy, container_id, actor_id, slot, quantity, state, integrity, owner) " +
            "VALUES ($id, $kind, $loc, $x, $y, $h, $cx, $cy, $container, $actor, $slot, $q, $state, $integrity, $owner)",
            ("$id", o.Id), ("$kind", o.Kind), ("$loc", o.Loc), ("$x", o.X), ("$y", o.Y), ("$h", o.H), ("$cx", o.Cx), ("$cy", o.Cy),
            ("$container", o.ContainerId), ("$actor", o.ActorId), ("$slot", o.Slot), ("$q", o.Quantity),
            ("$state", o.State.ToJsonString()), ("$integrity", o.Integrity), ("$owner", o.Owner));

    private void Exec(SqliteTransaction t, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, t);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static int? NullInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private static byte[]? Blob(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : (byte[])r.GetValue(i);

    private static JsonObject? JsonColumn(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : JsonNode.Parse(r.GetString(i)) as JsonObject;
}
