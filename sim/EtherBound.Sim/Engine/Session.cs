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
    public int MaxEventSeq { get; set; }
    public Database? Db { get; set; }
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
    private bool _wipe;

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

    /// <summary>
    /// Drop actors, objects, walls, terrain and the event log (a new game or a regeneration).
    /// Everything the session adds afterwards is the whole new state.
    /// </summary>
    public void Wipe(bool actors, bool events)
    {
        _wipe = true;
        WipeActors = actors;
        WipeEvents = events;
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
            Events = _events.ToList(),
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
            if (changes.WipeActors) _store.Actors.Clear();
        }
        if (changes.Meta is not null) _store.Meta = changes.Meta.Clone();
        foreach (var id in changes.DeletedActors) _store.Actors.Remove(id);
        foreach (var row in changes.InsertedActors.Concat(changes.UpdatedActors)) _store.Actors[row.Id] = row.Clone();
        foreach (var id in changes.DeletedObjects) _store.Objects.Remove(id);
        foreach (var row in changes.InsertedObjects.Concat(changes.UpdatedObjects)) _store.Objects[row.Id] = row.Clone();
        foreach (var key in changes.DeletedWalls) _store.Walls.Remove(key);
        foreach (var (key, value) in changes.Walls) _store.Walls[key] = value;
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
}
