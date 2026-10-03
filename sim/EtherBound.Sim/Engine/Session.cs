using EtherBound.Sim.Db;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Engine;

/// <summary>The committed state: what the database holds, kept in memory for the engine.</summary>
public sealed class WorldStore
{
    public WorldMetaRow? Meta { get; set; }
    public SortedDictionary<string, ActorRow> Actors { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<int, ObjectRow> Objects { get; } = new();
    public Dictionary<WallKey, double> Walls { get; } = new();
    public Dictionary<string, int> MaterialIds { get; } = new();
    public Dictionary<(string ActorId, string ActionKey), int> ActivityWork { get; } = new();
    public int MaxEventSeq { get; set; }

    /// <summary>Keys of the chunks the database holds; in a streaming world, the modified ones.</summary>
    public HashSet<(int, int)> PersistedChunks { get; } = new();
    public Database? Db { get; set; }

    // Derived from Actors and never saved: which committed actors stand on which tile, so a move reads
    // its neighbours instead of the whole crowd. Null until first asked for; whatever writes Actors outside
    // the helpers below (the database load) must call InvalidateActorIndex.
    private Dictionary<(int, int), List<string>>? _actorTiles;

    public void InvalidateActorIndex() => _actorTiles = null;

    internal IReadOnlyDictionary<(int, int), List<string>> ActorTiles()
    {
        if (_actorTiles is not null) return _actorTiles;
        var tiles = new Dictionary<(int, int), List<string>>();
        foreach (var row in Actors.Values) AddToTile(tiles, row);
        return _actorTiles = tiles;
    }

    internal void StoreActor(ActorRow row)
    {
        if (_actorTiles is not null && Actors.TryGetValue(row.Id, out var old)) RemoveFromTile(_actorTiles, old);
        Actors[row.Id] = row;
        if (_actorTiles is not null) AddToTile(_actorTiles, row);
    }

    internal void RemoveActor(string id)
    {
        if (_actorTiles is not null && Actors.TryGetValue(id, out var old)) RemoveFromTile(_actorTiles, old);
        Actors.Remove(id);
    }

    internal void ClearActors()
    {
        Actors.Clear();
        _actorTiles = null;
    }

    private static void AddToTile(Dictionary<(int, int), List<string>> tiles, ActorRow row)
    {
        var key = (row.TileX, row.TileY);
        if (!tiles.TryGetValue(key, out var ids)) tiles[key] = ids = new List<string>(2);
        ids.Add(row.Id);
    }

    private static void RemoveFromTile(Dictionary<(int, int), List<string>> tiles, ActorRow row)
    {
        var key = (row.TileX, row.TileY);
        if (!tiles.TryGetValue(key, out var ids)) return;
        ids.Remove(row.Id);
        if (ids.Count == 0) tiles.Remove(key);
    }
}

/// <summary>
/// A unit of work over <see cref="WorldStore"/>, like a SQLAlchemy session with autoflush: rows are
/// working copies, every query sees this session's own changes, and nothing reaches the store or
/// the database until <see cref="Commit"/>. Discarding the session discards the changes.
/// </summary>
public sealed class Session
{
    private readonly WorldStore _store;
    private WorldGrid _grid;
    private WorldMetaRow? _world;
    private readonly Dictionary<string, ActorRow> _actors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletedActors = new(StringComparer.Ordinal);
    private bool _allActorsLoaded;
    private readonly Dictionary<int, ObjectRow> _objects = new();
    private readonly HashSet<int> _deletedObjects = new();
    private bool _allObjectsLoaded;
    private readonly Dictionary<WallKey, double> _walls = new();
    private readonly HashSet<WallKey> _deletedWalls = new();
    private readonly HashSet<(int, int)> _dirtyChunks = new();
    private readonly HashSet<(int, int, int)> _dirtyLevels = new();
    private readonly List<EventRow> _events = new();
    private readonly List<InputToRecord> _inputs = new();
    private readonly Dictionary<(string ActorId, string ActionKey), int> _activityWork = new();
    private readonly HashSet<(string ActorId, string ActionKey)> _deletedActivityWork = new();
    private bool _wipe;
    private bool _wipeInputs;

    public Session(WorldStore store, WorldGrid grid)
    {
        _store = store;
        _grid = grid;
    }

    /// <summary>A regeneration builds a new grid; the pending terrain changes belong to it.</summary>
    public void UseGrid(WorldGrid grid) => _grid = grid;

    public WorldMetaRow World => _world ??= (_store.Meta ?? throw new InvalidOperationException("world has not been initialized")).Clone();

    public bool HasWorld => _world is not null || _store.Meta is not null;

    public void CreateWorld(WorldMetaRow meta) => _world = meta;

    // --- actors ------------------------------------------------------------------------------

    public ActorRow? GetActor(string id)
    {
        if (_deletedActors.Contains(id)) return null;
        if (_actors.TryGetValue(id, out var row)) return row;
        if (!_wipe && _store.Actors.TryGetValue(id, out var stored)) return _actors[id] = stored.Clone();
        return null;
    }

    /// <summary>Every actor, in id order (SQLite's binary collation).</summary>
    public IReadOnlyList<ActorRow> Actors()
    {
        if (!_allActorsLoaded)
        {
            if (!_wipe)
                foreach (var (id, stored) in _store.Actors)
                    if (!_actors.ContainsKey(id) && !_deletedActors.Contains(id)) _actors[id] = stored.Clone();
            _allActorsLoaded = true;
        }
        return _actors.Values.Where(a => !_deletedActors.Contains(a.Id)).OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The actors standing on any tile of the square that covers <paramref name="radius"/> metres around
    /// (<paramref name="x"/>, <paramref name="y"/>), in id order, as this session sees them: its own moves,
    /// additions and deletions count. Only these rows are copied, so a session that moves one actor no longer
    /// copies the crowd. The square is a superset of the circle; callers apply their own test.
    /// </summary>
    public IReadOnlyList<ActorRow> ActorsNear(double x, double y, double radius)
    {
        int x0 = PyMath.Floor(x - radius), x1 = PyMath.Floor(x + radius);
        int y0 = PyMath.Floor(y - radius), y1 = PyMath.Floor(y + radius);
        bool In(ActorRow a) => a.TileX >= x0 && a.TileX <= x1 && a.TileY >= y0 && a.TileY <= y1;

        // A wide square would visit more tiles than there are actors, and a session that already holds
        // the whole crowd has nothing left to look up.
        var area = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
        if (_allActorsLoaded || area > _store.Actors.Count)
            return Actors().Where(In).ToList();

        var found = new SortedDictionary<string, ActorRow>(StringComparer.Ordinal);
        if (!_wipe)
        {
            var tiles = _store.ActorTiles();
            for (var tx = x0; tx <= x1; tx++)
                for (var ty = y0; ty <= y1; ty++)
                    if (tiles.TryGetValue((tx, ty), out var ids))
                        foreach (var id in ids)
                            // The working copy decides: a row this session moved away is not here any more.
                            if (GetActor(id) is { } row && In(row)) found[id] = row;
        }
        // Rows the session moved onto the square or added belong to it even though the store never saw them there.
        foreach (var row in _actors.Values)
            if (!_deletedActors.Contains(row.Id) && In(row)) found[row.Id] = row;
        return found.Values.ToList();
    }

    /// <summary>The actors standing on one tile, in id order (see <see cref="ActorsNear"/>).</summary>
    public IReadOnlyList<ActorRow> ActorsOn(int x, int y) => ActorsNear(x + 0.5, y + 0.5, 0);

    public void DeleteActor(ActorRow row) => _deletedActors.Add(row.Id);

    public void AddActor(ActorRow row)
    {
        _deletedActors.Remove(row.Id);
        _actors[row.Id] = row;
    }

    // --- objects -----------------------------------------------------------------------------

    public ObjectRow? GetObject(int id)
    {
        if (_deletedObjects.Contains(id)) return null;
        if (_objects.TryGetValue(id, out var row)) return row;
        if (!_wipe && _store.Objects.TryGetValue(id, out var stored)) return _objects[id] = stored.Clone();
        return null;
    }

    /// <summary>Every object, in id order.</summary>
    public IReadOnlyList<ObjectRow> Objects()
    {
        if (!_allObjectsLoaded)
        {
            if (!_wipe)
                foreach (var (id, stored) in _store.Objects)
                    if (!_objects.ContainsKey(id) && !_deletedObjects.Contains(id)) _objects[id] = stored.Clone();
            _allObjectsLoaded = true;
        }
        return _objects.Values.Where(o => !_deletedObjects.Contains(o.Id)).OrderBy(o => o.Id).ToList();
    }

    /// <summary>Inserts a row with SQLite's rowid rule: one more than the largest id in the table now.</summary>
    public ObjectRow AddObject(ObjectRow row)
    {
        var existing = Objects();
        row.Id = existing.Count == 0 ? 1 : existing[^1].Id + 1;
        _objects[row.Id] = row;
        return row;
    }

    public void DeleteObject(ObjectRow row) => _deletedObjects.Add(row.Id);

    // --- walls, terrain, events --------------------------------------------------------------

    public double? GetWall(WallKey key)
    {
        if (_deletedWalls.Contains(key)) return null;
        if (_walls.TryGetValue(key, out var value)) return value;
        return !_wipe && _store.Walls.TryGetValue(key, out var stored) ? stored : null;
    }

    public void SetWall(WallKey key, double integrity)
    {
        _deletedWalls.Remove(key);
        _walls[key] = integrity;
    }

    public void DeleteWall(WallKey key)
    {
        _walls.Remove(key);
        _deletedWalls.Add(key);
    }

    public void MarkChunk(int cx, int cy) => _dirtyChunks.Add((cx, cy));

    public void MarkLevel(int cx, int cy, int z) => _dirtyLevels.Add((cx, cy, z));

    public void AddEvent(EventRow row) => _events.Add(row);

    public void RecordInput(string kind, System.Text.Json.Nodes.JsonObject payload) =>
        _inputs.Add(new InputToRecord(kind, (System.Text.Json.Nodes.JsonObject)payload.DeepClone()));

    public int ActivityWorkMinutes(string actorId, string actionKey)
    {
        var key = (actorId, actionKey);
        if (_activityWork.TryGetValue(key, out var minutes)) return minutes;
        if (_deletedActivityWork.Contains(key) || (_wipe && WipeActors)) return 0;
        return _store.ActivityWork.GetValueOrDefault(key);
    }

    public void SetActivityWorkMinutes(string actorId, string actionKey, int minutes)
    {
        var key = (actorId, actionKey);
        _deletedActivityWork.Remove(key);
        if (minutes <= 0)
        {
            _activityWork.Remove(key);
            _deletedActivityWork.Add(key);
        }
        else
        {
            _activityWork[key] = minutes;
        }
    }

    /// <summary>
    /// Drop actors, objects, walls, terrain and the event log (a new game or a regeneration).
    /// Everything the session adds afterwards is the whole new state.
    /// </summary>
    public void Wipe(bool actors, bool events, bool inputs = false)
    {
        _wipe = true;
        WipeActors = actors;
        WipeEvents = events;
        _wipeInputs = inputs;
        if (actors)
        {
            _activityWork.Clear();
            _deletedActivityWork.Clear();
        }
        _objects.Clear();
        _deletedObjects.Clear();
        _allObjectsLoaded = true;
        _walls.Clear();
        _deletedWalls.Clear();
        if (actors)
        {
            _actors.Clear();
            _deletedActors.Clear();
            _allActorsLoaded = true;
        }
        else
        {
            // Actors survive a regeneration: keep them loaded, since the wipe hides the store.
            foreach (var (id, stored) in _store.Actors)
                if (!_actors.ContainsKey(id)) _actors[id] = stored.Clone();
            _allActorsLoaded = true;
        }
    }

    public bool WipeActors { get; private set; }
    public bool WipeEvents { get; private set; }

    /// <summary>Keeps these objects through a wipe (held and worn ones with their contents).</summary>
    public void KeepObjects(IEnumerable<ObjectRow> rows)
    {
        foreach (var row in rows) _objects[row.Id] = row.Clone();
    }

    public void Commit()
    {
        var changes = new Changes
        {
            Wipe = _wipe,
            WipeActors = WipeActors,
            WipeEvents = WipeEvents,
            WipeActivityWork = _wipe && WipeActors,
            WipeInputs = _wipeInputs,
            Events = _events.ToList(),
            Inputs = _inputs.ToList(),
        };
        if (_world is not null && (_store.Meta is null || !_world.SameAs(_store.Meta))) changes.Meta = _world;

        foreach (var id in _deletedActors)
            if (_store.Actors.ContainsKey(id) && !(_wipe && WipeActors)) changes.DeletedActors.Add(id);
        foreach (var (id, row) in _actors)
        {
            if (_deletedActors.Contains(id)) continue;
            var known = !(_wipe && WipeActors) && _store.Actors.TryGetValue(id, out var stored) ? stored : null;
            if (known is null) changes.InsertedActors.Add(row);
            else if (!row.SameAs(known)) changes.UpdatedActors.Add(row);
        }

        foreach (var id in _deletedObjects)
            if (!_wipe && _store.Objects.ContainsKey(id)) changes.DeletedObjects.Add(id);
        foreach (var (id, row) in _objects)
        {
            if (_deletedObjects.Contains(id)) continue;
            var known = !_wipe && _store.Objects.TryGetValue(id, out var stored) ? stored : null;
            if (known is null) changes.InsertedObjects.Add(row);
            else if (!row.SameAs(known)) changes.UpdatedObjects.Add(row);
        }

        foreach (var key in _deletedWalls)
            if (!_wipe && _store.Walls.ContainsKey(key)) changes.DeletedWalls.Add(key);
        foreach (var (key, value) in _walls) changes.Walls[key] = value;

        foreach (var key in _deletedActivityWork)
            if (_store.ActivityWork.ContainsKey(key)) changes.DeletedActivityWork.Add(key);
        foreach (var (key, minutes) in _activityWork)
            if (!_deletedActivityWork.Contains(key) && (changes.WipeActivityWork || _store.ActivityWork.GetValueOrDefault(key) != minutes))
                changes.ActivityWork.Add(new ActivityWorkRow(key.ActorId, key.ActionKey, minutes));

        foreach (var key in _dirtyChunks) if (_grid.Chunks.TryGetValue(key, out var chunk)) changes.Chunks.Add(chunk);
        foreach (var key in _dirtyLevels) if (_grid.Levels.TryGetValue(key, out var level)) changes.Levels.Add(level);

        _store.Db?.Write(changes);
        Apply(changes);
    }

    private void Apply(Changes changes)
    {
        if (changes.Wipe)
        {
            _store.Objects.Clear();
            _store.Walls.Clear();
            if (changes.WipeActors) _store.ClearActors();
        }
        if (changes.WipeActivityWork) _store.ActivityWork.Clear();
        if (changes.Wipe) _store.PersistedChunks.Clear();
        foreach (var chunk in changes.Chunks) _store.PersistedChunks.Add((chunk.Cx, chunk.Cy));
        if (changes.Meta is not null) _store.Meta = changes.Meta.Clone();
        foreach (var id in changes.DeletedActors) _store.RemoveActor(id);
        foreach (var row in changes.InsertedActors.Concat(changes.UpdatedActors)) _store.StoreActor(row.Clone());
        foreach (var id in changes.DeletedObjects) _store.Objects.Remove(id);
        foreach (var row in changes.InsertedObjects.Concat(changes.UpdatedObjects)) _store.Objects[row.Id] = row.Clone();
        foreach (var key in changes.DeletedWalls) _store.Walls.Remove(key);
        foreach (var (key, value) in changes.Walls) _store.Walls[key] = value;
        foreach (var key in changes.DeletedActivityWork) _store.ActivityWork.Remove(key);
        foreach (var row in changes.ActivityWork)
            _store.ActivityWork[(row.ActorId, row.ActionKey)] = row.ProgressMinutes;
        if (changes.WipeEvents) _store.MaxEventSeq = 0;
        foreach (var e in changes.Events) _store.MaxEventSeq = Math.Max(_store.MaxEventSeq, e.Seq);
    }
}

/// <summary>What one commit writes: the diff between a session and the store.</summary>
public sealed class Changes
{
    public bool Wipe { get; init; }
    public bool WipeActors { get; init; }
    public bool WipeEvents { get; init; }
    public bool WipeActivityWork { get; init; }
    public bool WipeInputs { get; init; }
    public WorldMetaRow? Meta { get; set; }
    public List<string> DeletedActors { get; } = new();
    public List<ActorRow> InsertedActors { get; } = new();
    public List<ActorRow> UpdatedActors { get; } = new();
    public List<int> DeletedObjects { get; } = new();
    public List<ObjectRow> InsertedObjects { get; } = new();
    public List<ObjectRow> UpdatedObjects { get; } = new();
    public List<WallKey> DeletedWalls { get; } = new();
    public Dictionary<WallKey, double> Walls { get; } = new();
    public List<Chunk> Chunks { get; } = new();
    public List<ChunkLevel> Levels { get; } = new();
    public List<EventRow> Events { get; init; } = new();
    public List<InputToRecord> Inputs { get; init; } = new();
    public List<(string ActorId, string ActionKey)> DeletedActivityWork { get; } = new();
    public List<ActivityWorkRow> ActivityWork { get; } = new();
}
