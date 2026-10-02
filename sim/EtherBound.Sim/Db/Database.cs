using System.Globalization;
using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using Microsoft.Data.Sqlite;

namespace EtherBound.Sim.Db;

/// <summary>
/// The SQLite save, same schema as the Python server at <c>0008_extra</c> (Dev-025 [Sec. 1]) plus the
/// sim's own migrations. A new save is created from the exact Alembic DDL with
/// <c>alembic_version</c> set, so both runtimes open it; an older save is upgraded in place by the
/// stepwise runner, one transaction per step. The sim also records its own <c>sim_schema</c> row.
/// </summary>
public sealed class Database : IDisposable
{
    public const string AlembicHead = "0012_actor_needs";
    public const string SimSchema = "sim-0012";

    /// <summary>
    /// The sim's migration chain, oldest first: each entry moves a save one step up and is the whole
    /// body of one transaction, so an interrupted upgrade leaves the save on the previous version
    /// rather than half-migrated.
    /// </summary>
    private static readonly (string Version, string Resource)[] Migrations =
    {
        ("0009_walls", "EtherBound.Sim.Db.Schema0009_walls.sql"),
        ("0010_replay_work", "EtherBound.Sim.Db.Schema0010_replay_work.sql"),
        ("0011_llm_call", "EtherBound.Sim.Db.Schema0011_llm_call.sql"),
        ("0012_actor_needs", "EtherBound.Sim.Db.Schema0012_actor_needs.sql"),
    };

    /// <summary>Every version the runner knows how to move through, oldest first.</summary>
    private static readonly string[] Known = new[] { "0008_extra" }.Concat(Migrations.Select(m => m.Version)).ToArray();

    private readonly SqliteConnection _connection;
    private SqliteTransaction? _writeBatch;
    private long _savepointId;

    public Database(string path)
    {
        var memory = path == ":memory:";
        // Unpooled, so Dispose really closes the file and SQLite folds the WAL back into the save.
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
            Pooling = memory,
        };
        _connection = new SqliteConnection(builder.ToString());
        try
        {
            _connection.Open();
            // Every WASD step is one commit; the default journal fsyncs each one (4.6 ms p50, Fix19 F5).
            // WAL with NORMAL fsyncs only at checkpoints: a power cut may drop the last moments, never corrupt.
            if (!memory) Pragma("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
            EnsureSchema();
        }
        catch
        {
            // A refused schema would otherwise leave the file locked for the whole process.
            _connection.Dispose();
            throw;
        }
    }

    private void Pragma(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal string PragmaValue(string name)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"PRAGMA {name}";
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
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
                version.Parameters.AddWithValue("$v", "0008_extra");
                version.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        Migrate();
        using var sim = Command("CREATE TABLE IF NOT EXISTS sim_schema (id INTEGER NOT NULL PRIMARY KEY, version VARCHAR(32) NOT NULL);" +
            $"INSERT OR REPLACE INTO sim_schema (id, version) VALUES (1, '{SimSchema}');");
        sim.ExecuteNonQuery();
    }

    private string CurrentVersion()
    {
        using var head = Command("SELECT version_num FROM alembic_version");
        return head.ExecuteScalar() as string ?? "";
    }

    /// <summary>
    /// Applies every pending step in order from the save's own <c>alembic_version</c> and then
    /// writes the head (Dev-036 [Sec. 1]). A version the runner does not know is refused, since
    /// guessing would either lose data or read a schema the code does not expect.
    /// </summary>
    private void Migrate()
    {
        var found = CurrentVersion();
        var from = Array.IndexOf(Known, found);
        if (from < 0)
            throw new InvalidOperationException($"save is at {(found.Length == 0 ? "no version" : found)}; the runner only knows {string.Join(" -> ", Known)}");
        for (var step = from; step < Known.Length - 1; step++)
        {
            using var stream = typeof(Database).Assembly.GetManifestResourceStream(Migrations[step].Resource)
                ?? throw new InvalidOperationException($"embedded migration {Migrations[step].Resource} is missing");
            using var reader = new StreamReader(stream);
            using var transaction = _connection.BeginTransaction();
            using (var apply = Command(reader.ReadToEnd(), transaction)) apply.ExecuteNonQuery();
            // The step's own body already writes the version; this covers a body that does not.
            using (var version = Command("UPDATE alembic_version SET version_num = $v WHERE version_num <> $v", transaction))
            {
                version.Parameters.AddWithValue("$v", Migrations[step].Version);
                version.ExecuteNonQuery();
            }
            transaction.Commit();
        }
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
        using (var command = Command("SELECT id, kind, name, x, y, z, h, mass_kg, activity, mind, needs FROM actor"))
        using (var r = command.ExecuteReader())
            while (r.Read())
            {
                var row = new ActorRow
                {
                    Id = r.GetString(0), Kind = r.GetString(1), Name = r.IsDBNull(2) ? null : r.GetString(2),
                    X = r.GetDouble(3), Y = r.GetDouble(4), Z = r.GetInt32(5), H = r.GetInt32(6), MassKg = r.GetDouble(7),
                    Activity = JsonColumn(r, 8), Mind = JsonColumn(r, 9), Needs = JsonColumn(r, 10),
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
        using (var command = Command("SELECT cx, cy, z, cell_index, slot, joules FROM wall_slot"))
        using (var r = command.ExecuteReader())
            while (r.Read())
                store.Walls[new WallKey(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetString(4))] = r.GetDouble(5);
        using (var command = Command("SELECT COALESCE(MAX(seq), 0) FROM event"))
            store.MaxEventSeq = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        using (var command = Command("SELECT actor_id, action_key, progress_minutes FROM activity_work ORDER BY actor_id, action_key"))
        using (var r = command.ExecuteReader())
            while (r.Read()) store.ActivityWork[(r.GetString(0), r.GetString(1))] = r.GetInt32(2);
    }

    public IReadOnlyList<InputJournalEntry> ReadInputJournal()
    {
        var rows = new List<InputJournalEntry>();
        using var command = Command("SELECT seq, kind, payload, repeat_count FROM input_journal ORDER BY seq");
        using var r = command.ExecuteReader();
        while (r.Read()) rows.Add(new InputJournalEntry(r.GetInt64(0), r.GetString(1), Json.Parse(r.GetString(2)), r.GetInt32(3)));
        return rows;
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
        using (var command = Command("SELECT cx, cy, z, floor_h, floor_mat, wall_n, wall_w, edge_flags, flags, slot_mask, slot_mat FROM chunk_level ORDER BY cx, cy, z"))
        using (var r = command.ExecuteReader())
            while (r.Read())
                levels.Add(ChunkLevel.FromBlobs(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), Blob(r, 3)!, Blob(r, 4)!, Blob(r, 5)!,
                    Blob(r, 6)!, Blob(r, 7)!, Blob(r, 8)!, Blob(r, 9), Blob(r, 10)));
        return (chunks, levels);
    }

    /// <summary>Keys of every stored chunk: for a streaming world, exactly the modified ones.</summary>
    public HashSet<(int, int)> ChunkKeys()
    {
        var keys = new HashSet<(int, int)>();
        using var command = Command("SELECT cx, cy FROM chunk");
        using var r = command.ExecuteReader();
        while (r.Read()) keys.Add((r.GetInt32(0), r.GetInt32(1)));
        return keys;
    }

    /// <summary>One stored chunk with its levels, for a streaming world's loader.</summary>
    public (Chunk Chunk, List<ChunkLevel> Levels)? ReadChunk(int cx, int cy)
    {
        Chunk? chunk = null;
        using (var command = Command("SELECT ground_h, surface_mat, strata, revision, gen_version, dug FROM chunk WHERE cx = $cx AND cy = $cy"))
        {
            command.Parameters.AddWithValue("$cx", cx);
            command.Parameters.AddWithValue("$cy", cy);
            using var r = command.ExecuteReader();
            if (r.Read())
            {
                var strata = JsonNode.Parse(r.GetString(2))!.AsArray()
                    .Select(layer => new Stratum(Json.ToInt(layer![0]!), layer[1]!.GetValue<string>())).ToList();
                chunk = Chunk.FromBlobs(cx, cy, Blob(r, 0)!, Blob(r, 1)!, strata, r.GetInt32(3), r.GetInt32(4), Blob(r, 5));
            }
        }
        if (chunk is null) return null;
        var levels = new List<ChunkLevel>();
        using (var command = Command("SELECT z, floor_h, floor_mat, wall_n, wall_w, edge_flags, flags, slot_mask, slot_mat FROM chunk_level WHERE cx = $cx AND cy = $cy ORDER BY z"))
        {
            command.Parameters.AddWithValue("$cx", cx);
            command.Parameters.AddWithValue("$cy", cy);
            using var r = command.ExecuteReader();
            while (r.Read())
                levels.Add(ChunkLevel.FromBlobs(cx, cy, r.GetInt32(0), Blob(r, 1)!, Blob(r, 2)!, Blob(r, 3)!, Blob(r, 4)!,
                    Blob(r, 5)!, Blob(r, 6)!, Blob(r, 7), Blob(r, 8)));
        }
        return (chunk, levels);
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

    public List<EventRow> ReadLastEvents(string type, int limit)
    {
        using var command = Command("SELECT seq, game_minute, type, actor_id, data FROM event WHERE type = $type ORDER BY seq DESC LIMIT $limit");
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$limit", limit);
        var rows = new List<EventRow>();
        using var r = command.ExecuteReader();
        while (r.Read())
            rows.Add(new EventRow(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), Json.Parse(r.GetString(4))));
        rows.Reverse();
        return rows;
    }

    // --- llm_call (Dev-008): a billing and debugging log, never gameplay state -----------------

    public void InsertLlmCall(LlmCallRow row)
    {
        using var command = Command("INSERT INTO llm_call (game_minute, role, model, tokens_in, tokens_out, cost_usd, outcome, error, " +
            "prompt, response) VALUES ($minute, $role, $model, $in, $out, $cost, $outcome, $error, $prompt, $response)");
        var p = command.Parameters;
        p.AddWithValue("$minute", row.GameMinute);
        p.AddWithValue("$role", row.Role);
        p.AddWithValue("$model", row.Model);
        p.AddWithValue("$in", row.TokensIn);
        p.AddWithValue("$out", row.TokensOut);
        p.AddWithValue("$cost", row.CostUsd is { } cost ? cost : DBNull.Value);
        p.AddWithValue("$outcome", row.Outcome);
        p.AddWithValue("$error", row.Error is { } error ? error : DBNull.Value);
        p.AddWithValue("$prompt", row.Prompt);
        p.AddWithValue("$response", row.Response);
        command.ExecuteNonQuery();
    }

    /// <summary>The newest rows, newest first.</summary>
    public List<LlmCallRow> ReadLlmCalls(int limit, string? role = null)
    {
        using var command = Command("SELECT id, game_minute, role, model, tokens_in, tokens_out, cost_usd, outcome, error, prompt, response " +
            $"FROM llm_call {(role is null ? "" : "WHERE role = $role ")}ORDER BY id DESC LIMIT $limit");
        command.Parameters.AddWithValue("$limit", limit);
        if (role is not null) command.Parameters.AddWithValue("$role", role);
        var rows = new List<LlmCallRow>();
        using var r = command.ExecuteReader();
        while (r.Read())
            rows.Add(new LlmCallRow(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetDouble(6), r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetString(9), r.GetString(10)));
        return rows;
    }

    /// <summary>
    /// Keeps the log from growing without bound while event retention is open: full text for the newest
    /// <paramref name="keepFull"/> rows, text cut to <paramref name="cutTo"/> characters beyond that, and
    /// no rows older than the newest <paramref name="keepRows"/>.
    /// </summary>
    public void PruneLlmCalls(int keepFull, int keepRows, int cutTo)
    {
        using var transaction = _connection.BeginTransaction();
        using (var cut = Command("UPDATE llm_call SET prompt = substr(prompt, 1, $cut), response = substr(response, 1, $cut) " +
            "WHERE id <= (SELECT COALESCE(MAX(id), 0) FROM llm_call) - $full AND (length(prompt) > $cut OR length(response) > $cut)", transaction))
        {
            cut.Parameters.AddWithValue("$cut", cutTo);
            cut.Parameters.AddWithValue("$full", keepFull);
            cut.ExecuteNonQuery();
        }
        using (var drop = Command("DELETE FROM llm_call WHERE id <= (SELECT COALESCE(MAX(id), 0) FROM llm_call) - $rows", transaction))
        {
            drop.Parameters.AddWithValue("$rows", keepRows);
            drop.ExecuteNonQuery();
        }
        transaction.Commit();
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
            Exec(t, "DELETE FROM wall_slot");
            Exec(t, "DELETE FROM chunk_level");
            Exec(t, "DELETE FROM chunk");
            Exec(t, "DELETE FROM object");
            if (changes.WipeActors) Exec(t, "DELETE FROM actor");
            if (changes.WipeEvents) Exec(t, "DELETE FROM event");
        }
        if (changes.WipeActivityWork) Exec(t, "DELETE FROM activity_work");
        if (changes.WipeInputs) Exec(t, "DELETE FROM input_journal");
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
        foreach (var id in changes.DeletedActors) Exec(t, "DELETE FROM activity_work WHERE actor_id = $id", ("$id", id));
        foreach (var row in changes.InsertedActors.Concat(changes.UpdatedActors)) WriteActor(t, row);
        foreach (var row in changes.InsertedObjects.Concat(changes.UpdatedObjects).OrderBy(o => o.Id)) WriteObject(t, row);
        foreach (var key in changes.DeletedWalls)
            Exec(t, "DELETE FROM wall_slot WHERE cx = $cx AND cy = $cy AND z = $z AND cell_index = $i AND slot = $s",
                ("$cx", key.Cx), ("$cy", key.Cy), ("$z", key.Z), ("$i", key.CellIndex), ("$s", key.Slot));
        foreach (var (key, value) in changes.Walls)
            Exec(t, "INSERT OR REPLACE INTO wall_slot (cx, cy, z, cell_index, slot, joules) VALUES ($cx, $cy, $z, $i, $s, $v)",
                ("$cx", key.Cx), ("$cy", key.Cy), ("$z", key.Z), ("$i", key.CellIndex), ("$s", key.Slot), ("$v", value));
        foreach (var key in changes.DeletedActivityWork)
            Exec(t, "DELETE FROM activity_work WHERE actor_id = $actor AND action_key = $action",
                ("$actor", key.ActorId), ("$action", key.ActionKey));
        foreach (var row in changes.ActivityWork)
            Exec(t, "INSERT OR REPLACE INTO activity_work (actor_id, action_key, progress_minutes) VALUES ($actor, $action, $minutes)",
                ("$actor", row.ActorId), ("$action", row.ActionKey), ("$minutes", row.ProgressMinutes));
        foreach (var input in changes.Inputs)
        {
            var payload = input.Payload.ToJsonString();
            using var last = Command("SELECT seq, kind, payload FROM input_journal ORDER BY seq DESC LIMIT 1", t);
            using var r = last.ExecuteReader();
            if (r.Read() && r.GetString(1) == input.Kind && r.GetString(2) == payload)
            {
                var sequence = r.GetInt64(0);
                r.Close();
                Exec(t, "UPDATE input_journal SET repeat_count = repeat_count + 1 WHERE seq = $seq", ("$seq", sequence));
            }
            else
            {
                r.Close();
                Exec(t, "INSERT INTO input_journal (seq, kind, payload) VALUES ((SELECT COALESCE(MAX(seq), 0) + 1 FROM input_journal), $kind, $payload)",
                    ("$kind", input.Kind), ("$payload", payload));
            }
        }
        foreach (var chunk in changes.Chunks)
            Exec(t, "INSERT OR REPLACE INTO chunk (cx, cy, ground_h, surface_mat, strata, revision, gen_version, dug) " +
                "VALUES ($cx, $cy, $g, $s, $strata, $rev, $gv, $dug)",
                ("$cx", chunk.Cx), ("$cy", chunk.Cy), ("$g", chunk.GroundBlob), ("$s", chunk.SurfaceBlob), ("$strata", chunk.StrataJson()),
                ("$rev", chunk.Revision), ("$gv", chunk.GenVersion), ("$dug", chunk.DugBlob));
        foreach (var level in changes.Levels)
            Exec(t, "INSERT OR REPLACE INTO chunk_level (cx, cy, z, floor_h, floor_mat, wall_n, wall_w, edge_flags, flags, slot_mask, slot_mat) " +
                "VALUES ($cx, $cy, $z, $f, $fm, $wn, $ww, $ef, $fl, $sm, $smat)",
                ("$cx", level.Cx), ("$cy", level.Cy), ("$z", level.Z), ("$f", level.FloorBlob), ("$fm", level.FloorMatBlob),
                ("$wn", level.WallNBlob), ("$ww", level.WallWBlob), ("$ef", level.EdgeFlagsBlob), ("$fl", level.FlagsBlob),
                ("$sm", level.SlotMaskBlob), ("$smat", level.SlotMatBlob));
        foreach (var e in changes.Events)
            Exec(t, "INSERT INTO event (seq, game_minute, type, actor_id, data) VALUES ($seq, $minute, $type, $actor, $data)",
                ("$seq", e.Seq), ("$minute", e.GameMinute), ("$type", e.Type), ("$actor", e.ActorId), ("$data", e.Data.ToJsonString()));
    }

    private void WriteActor(SqliteTransaction t, ActorRow a) =>
        Exec(t, "INSERT OR REPLACE INTO actor (id, kind, x, y, z, h, activity, mass_kg, name, mind, needs) " +
            "VALUES ($id, $kind, $x, $y, $z, $h, $activity, $mass, $name, $mind, $needs)",
            ("$id", a.Id), ("$kind", a.Kind), ("$x", a.X), ("$y", a.Y), ("$z", a.Z), ("$h", a.H),
            ("$activity", a.Activity?.ToJsonString()), ("$mass", a.MassKg), ("$name", a.Name), ("$mind", a.Mind?.ToJsonString()),
            ("$needs", a.Needs?.ToJsonString()));

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
