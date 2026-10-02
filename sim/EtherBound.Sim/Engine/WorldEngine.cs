using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Db;
using EtherBound.Sim.Engine.Ops;
using EtherBound.Sim.Events;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;

namespace EtherBound.Sim.Engine;

/// <summary>What minds and the host may call on the engine (Dev-023 <c>EnginePort</c>).</summary>
public interface IEnginePort
{
    WorldGrid Grid { get; }

    WorldState GetState();

    WorldState GetTickState() => GetState();

    ActionResult Submit(string actorId, GameAction action, double deltaSeconds = 1.0 / 20);

    void SetGoal(string actorId, GoalSpot? goal, string reason);

    MenuPayload Menu(string actorId, double x, double y, int z, int radius = 0);

    /// <summary>A read: what within <paramref name="radiusM"/> of the actor could satisfy hunger or thirst.</summary>
    IReadOnlyList<Percept> Percepts(string actorId, int radiusM);
}

/// <summary>
/// The only component allowed to validate and commit world state changes (<c>engine/world.py</c>).
/// Every call runs on the sim thread; events are dispatched after the commit, outside the change.
/// </summary>
public sealed partial class WorldEngine : IEnginePort, IDisposable
{
    public const int ChunkRadius = 2;

    private readonly WorldStore _store = new();
    private readonly Database _db;
    private int _nextSeq = 1;
    private Dictionary<string, double> _loadCache = new(StringComparer.Ordinal);
    private Dictionary<string, IReadOnlyList<CarriedObject>> _carriedCache = new(StringComparer.Ordinal);
    private bool _dispatchingTick;
    private WorldState? _tickSnapshot;

    private readonly string _materialsToml;

    // Streaming worlds (Dev-010): the current world's chunk source, the session a setup call is
    // still building (its objects are not committed yet) and whether the stored chunks belong to a
    // world that this setup is wiping.
    private IChunkSource? _source;
    private Session? _setupSession;
    private bool _ignorePersisted;

    public WorldEngine(string databasePath = ":memory:", MaterialRegistry? registry = null, EventBus? bus = null,
        ObjectCatalog? catalog = null, string? materialsToml = null)
        : this(databasePath, () => LoadCatalogs(registry, catalog, materialsToml), bus)
    {
    }

    public WorldEngine(string databasePath, Func<WorldEngineCatalogs> loadCatalogs, EventBus? bus = null)
    {
        _db = new Database(databasePath);
        _store.Db = _db;
        WorldEngineCatalogs catalogs;
        try
        {
            _db.Load(_store);
            catalogs = loadCatalogs();
        }
        catch
        {
            _db.Dispose();
            throw;
        }
        _materialsToml = catalogs.MaterialsToml;
        Registry = catalogs.Registry;
        Catalog = catalogs.Catalog;
        Grid = new WorldGrid(registry: Registry, catalog: Catalog);
        Bus = bus ?? new EventBus();
    }

    /// <summary>Loads immutable engine catalogs without opening or changing a world database.</summary>
    public static WorldEngineCatalogs LoadCatalogs() => LoadCatalogs(null, null, null);

    private static WorldEngineCatalogs LoadCatalogs(MaterialRegistry? registry, ObjectCatalog? catalog, string? materialsToml)
    {
        var text = materialsToml ?? DataFiles.ReadText("materials.toml");
        var resolvedRegistry = registry ?? MaterialRegistry.FromText(text);
        return new WorldEngineCatalogs(text, resolvedRegistry, catalog ?? ObjectCatalog.Load(resolvedRegistry));
    }

    public MaterialRegistry Registry { get; private set; }
    public ObjectCatalog Catalog { get; }
    public WorldGrid Grid { get; private set; }
    public EventBus Bus { get; }

    /// <summary>The pure terrain sampler of a streaming world (the minimap reads it), or null for a bounded one.</summary>
    public IChunkSource? Source => _source;

    public void Dispose() => _db.Dispose();

    private Session NewSession() => new(_store, Grid);

    public double LoadKg(string actorId) => _loadCache.GetValueOrDefault(actorId, 0.0);

    private void RefreshLoad(Session session)
    {
        _loadCache = session.Actors().ToDictionary(a => a.Id, a => ObjectHelpers.ActorLoadKg(session, Catalog, a.Id), StringComparer.Ordinal);
        var carried = new Dictionary<string, List<CarriedObject>>(StringComparer.Ordinal);
        foreach (var obj in session.Objects())
        {
            if (obj.ActorId is not { } actorId) continue;
            if (!carried.TryGetValue(actorId, out var items)) carried[actorId] = items = new List<CarriedObject>();
            items.Add(new CarriedObject(obj.Id, obj.Kind, Catalog.Get(obj.Kind)?.Name ?? obj.Kind, obj.Quantity, obj.Slot ?? ""));
        }
        _carriedCache = carried.ToDictionary(p => p.Key, p => (IReadOnlyList<CarriedObject>)p.Value.AsReadOnly(), StringComparer.Ordinal);
    }

    private void LoadObjectIndex(Session session)
    {
        foreach (var (cx, cy) in Grid.Chunks.Keys) Grid.SetChunkObjects(cx, cy, ObjectHelpers.TileObjects(session, Catalog, cx, cy));
    }

    /// <summary>Stamps sequence numbers and the game minute; unlogged events use a number too.</summary>
    private int StampAndStore(Session session, WorldMetaRow world, List<SimEvent> events, int? firstSeq = null)
    {
        var next = firstSeq ?? _nextSeq;
        foreach (var e in events)
        {
            e.Seq = next++;
            e.GameMinute = world.GameMinute;
            if (e.Logged) session.AddEvent(new EventRow(e.Seq, e.GameMinute, e.Type, e.ActorId, (JsonObject)e.Data.DeepClone()));
        }
        return next;
    }

    private void Publish(Session session, WorldMetaRow world, List<SimEvent> events, int? firstSeq = null)
    {
        var next = StampAndStore(session, world, events, firstSeq);
        session.Commit();
        _tickSnapshot = null;
        _nextSeq = next;
        Bus.Enqueue(events);
    }

    public void EnsureWorld(long seed = 0)
    {
        var events = new List<SimEvent>();
        var session = NewSession();
        var creating = !session.HasWorld;
        if (creating)
        {
            session.CreateWorld(new WorldMetaRow { Seed = seed, GameMinute = 0, Speed = 1, Paused = false, GenVersion = 0, Generator = Generators.Default });
        }
        var world = session.World;
        if (_store.MaterialIds.Count > 0)
        {
            Registry = MaterialRegistry.FromText(_materialsToml, _store.MaterialIds);
            var missing = _store.MaterialIds.Keys.Where(k => Registry.Get(k) is null).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (missing.Count > 0) throw new InvalidOperationException($"saved materials missing from materials.toml: {string.Join(", ", missing)}");
            Grid.Registry = Registry;
        }
        _db.SyncMaterials(Registry, _store.MaterialIds);
        foreach (var material in Registry.Materials) _store.MaterialIds[material.Key] = material.Id;

        var (spec, options, fallback) = WorldSetup.ResolveGenerator(world);
        if (creating)
            session.RecordInput("ensure_world", Json.Obj(("seed", Json.Of(seed)), ("generator", Json.Of(spec.Key)),
                ("gen_version", Json.Of(spec.Version)), ("options", options.Dump())));
        var hasChunks = _db.HasChunks();
        // A streaming save is pinned to the version it was made with and stores only modified chunks, so
        // an empty chunk table is normal and never a reason to regenerate (and wipe the objects).
        var streaming = spec.Streaming && !fallback;
        var regenerate = fallback || (!streaming && (!hasChunks || world.GenVersion < spec.Version));
        _setupSession = session;
        try
        {
        if (regenerate)
        {
            var carried = WorldSetup.CarriedObjects(session);
            session.Wipe(actors: false, events: false);
            session.KeepObjects(carried);
            var generated = spec.Generate(world.Seed, options, Registry);
            _source = null;
            Grid = WorldSetup.CommitWorld(session, generated, Registry, Catalog);
            WorldSetup.CommitObjects(session, generated.Objects);
            world.GenVersion = generated.GenVersion;
            world.Generator = spec.Key;
            world.GenOptions = options.Dump();
            events.Add(SimEvent.WorldGenerated(world.Seed, generated.GenVersion, spec.Key, world.GenOptions));
        }
        else if (streaming)
        {
            _store.PersistedChunks.Clear();
            _store.PersistedChunks.UnionWith(_db.ChunkKeys());
            Grid = NewStreamingGrid(spec, options, world.Seed);
        }
        else
        {
            var (chunks, levels) = _db.LoadGrid();
            _source = null;
            Grid = new WorldGrid(chunks, levels, Registry, catalog: Catalog);
        }
        session.UseGrid(Grid);
        LoadObjectIndex(session);
        events.AddRange(WorldSetup.EnsureActors(session, Grid, Registry, spec, options, world.Seed));
        RefreshLoad(session);
        if (regenerate) events.Add(SimEvent.ClockChanged(world.Speed, world.Paused));
        _nextSeq = _store.MaxEventSeq + 1;
        Publish(session, world, events);
        }
        finally
        {
            _setupSession = null;
        }
    }

    /// <summary>
    /// A grid whose chunks are built on demand (Dev-010). A pristine chunk is a cache, not state, so
    /// loading one writes nothing: no row, no event, no dirty mark. Only a chunk a mutation changed is
    /// stored, and the stored row wins over the generator from then on.
    /// </summary>
    private WorldGrid NewStreamingGrid(GeneratorSpec spec, IGeneratorOptions options, long seed)
    {
        var source = _source = spec.Stream!(seed, options, Registry);
        return new WorldGrid(null, null, Registry, (cx, cy) => LoadStreamedChunk(source, cx, cy), Catalog, IndexChunkObjects);
    }

    private (Chunk, IEnumerable<ChunkLevel>)? LoadStreamedChunk(IChunkSource source, int cx, int cy)
    {
        if (!_ignorePersisted && _store.PersistedChunks.Contains((cx, cy)) && _db.ReadChunk(cx, cy) is { } saved)
            return (saved.Chunk, saved.Levels);
        return source.Generate(cx, cy) is { } generated ? (generated.Chunk, generated.Levels) : null;
    }

    /// <summary>Tile objects of a chunk being loaded: the setup session's while one is building, else the committed rows.</summary>
    private IEnumerable<TileObject> IndexChunkObjects(int cx, int cy) =>
        ObjectHelpers.TileObjects(_setupSession is { } session ? session.Objects() : _store.Objects.Values, Catalog, cx, cy);

    /// <summary>
    /// The ground of a tile for the minimap, without loading anything pristine: a loaded chunk answers
    /// from itself, so a dug hole or a built floor shows, and an unloaded pristine one from the pure
    /// source. Null where nothing exists (outside a bounded world).
    /// </summary>
    public TerrainCell? SurfaceAt(int x, int y)
    {
        var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(x, y);
        if (Grid.Chunks.TryGetValue((cx, cy), out var chunk))
            return new TerrainCell(chunk.GroundH[Chunk.Index(lx, ly)], chunk.SurfaceMat[Chunk.Index(lx, ly)]);
        if (_source is not null && !_store.PersistedChunks.Contains((cx, cy))) return _source.Sample(x, y);
        return Grid.GroundAt(x, y) is { } ground ? new TerrainCell(ground.GroundH, ground.SurfaceMat) : null;
    }

    /// <summary>The revision that tells the minimap a chunk changed: a loaded or stored chunk's own, else 0 (pristine).</summary>
    public int MapRevision(int cx, int cy)
    {
        if (Grid.Chunks.TryGetValue((cx, cy), out var chunk)) return chunk.Revision;
        return _store.PersistedChunks.Contains((cx, cy)) ? Grid.Chunk(cx, cy)?.Revision ?? 0 : 0;
    }

    /// <summary>Chunks farther than this many chunks from every actor are forgotten (and rebuilt if read again).</summary>
    public const int EvictRadius = 4;

    /// <summary>
    /// Drops loaded chunks away from every actor. Nothing is lost: each commit already stored what a
    /// mutation changed, and a pristine chunk regenerates identically. Called between operations, when no
    /// session holds uncommitted changes.
    /// </summary>
    public int EvictFarChunks(int keepRadius = EvictRadius)
    {
        if (!Grid.Streaming) return 0;
        var keep = new HashSet<(int, int)>();
        foreach (var actor in _store.Actors.Values)
        {
            int ax = PyMath.FloorDiv(actor.TileX, ChunkConst.Size), ay = PyMath.FloorDiv(actor.TileY, ChunkConst.Size);
            for (var dy = -keepRadius; dy <= keepRadius; dy++)
            for (var dx = -keepRadius; dx <= keepRadius; dx++) keep.Add((ax + dx, ay + dy));
        }
        var victims = Grid.Chunks.Keys.Where(key => !keep.Contains(key)).ToList();
        foreach (var (cx, cy) in victims) Grid.Evict(cx, cy);
        return victims.Count;
    }

    /// <summary>Loads the next missing chunk within <paramref name="radius"/> of (cx, cy), nearest first; false when all are loaded.</summary>
    public bool PrefetchChunk(int cx, int cy, int radius)
    {
        if (!Grid.Streaming) return false;
        for (var ring = 0; ring <= radius; ring++)
        for (var dy = -ring; dy <= ring; dy++)
        for (var dx = -ring; dx <= ring; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring || Grid.Chunks.ContainsKey((cx + dx, cy + dy))) continue;
            if (Grid.Chunk(cx + dx, cy + dy) is not null) return true;
        }
        return false;
    }

    public WorldState NewGame(long seed, string generator = Generators.Default, JsonObject? options = null, bool paused = false)
    {
        var spec = Generators.Get(generator) ?? throw new ArgumentException($"unknown generator: {generator}");
        var resolved = spec.Parse(options ?? new JsonObject());
        var session = NewSession();
        var world = session.HasWorld ? session.World : new WorldMetaRow();
        if (!session.HasWorld) session.CreateWorld(world);
        _setupSession = session;
        _ignorePersisted = true;
        try
        {
        session.Wipe(actors: true, events: true, inputs: true);
        session.RecordInput("new_game", Json.Obj(("seed", Json.Of(seed)), ("generator", Json.Of(spec.Key)),
            ("gen_version", Json.Of(spec.Version)), ("options", resolved.Dump()), ("paused", Json.Of(paused))));
        world.Seed = seed;
        world.GameMinute = 0;
        world.Speed = 1;
        world.Paused = paused;
        var generated = spec.Generate(seed, resolved, Registry);
        if (spec.Streaming)
        {
            Grid = NewStreamingGrid(spec, resolved, seed);
        }
        else
        {
            _source = null;
            Grid = WorldSetup.CommitWorld(session, generated, Registry, Catalog);
        }
        session.UseGrid(Grid);
        WorldSetup.CommitObjects(session, generated.Objects);
        world.GenVersion = generated.GenVersion;
        world.Generator = spec.Key;
        world.GenOptions = resolved.Dump();
        LoadObjectIndex(session);
        var events = new List<SimEvent> { SimEvent.WorldGenerated(seed, world.GenVersion, spec.Key, world.GenOptions) };
        events.AddRange(WorldSetup.EnsureActors(session, Grid, Registry, spec, resolved, seed));
        RefreshLoad(session);
        events.Add(SimEvent.ClockChanged(world.Speed, world.Paused));
        Publish(session, world, events, firstSeq: 1);
        }
        finally
        {
            _setupSession = null;
            _ignorePersisted = false;
        }
        var state = GetState();
        Bus.Drain();
        return state;
    }

    private IReadOnlyList<CarriedObject> Carried(string actorId) =>
        _carriedCache.GetValueOrDefault(actorId) ?? Array.Empty<CarriedObject>();

    // One result builder (Dev-023 F6): every answer carries the actor's position and load.
    private ActionResult Result(Session session, ActorRow actor, GameAction action, bool accepted, string? reason = null,
        string? text = null, ActivityState? activity = null, IReadOnlyList<PhysicsPosition>? trajectory = null) =>
        new(accepted, actor.Id, action, actor.X, actor.Y, actor.Z, actor.H, reason, text, activity, Carried(actor.Id),
            LoadKg(actor.Id), trajectory ?? Array.Empty<PhysicsPosition>());

    public ActionResult Submit(string actorId, GameAction action, double deltaSeconds = 1.0 / 20)
    {
        var session = NewSession();
        var world = session.World;
        var actor = session.GetActor(actorId) ?? throw new KeyNotFoundException($"unknown actor: {actorId}");
        session.RecordInput("submit", Json.Obj(("actor_id", Json.Of(actorId)), ("action", action.ToJson()),
            ("delta_seconds", InputReplay.EncodeDelta(deltaSeconds))));
        if (world.Paused)
        {
            session.Commit();
            return Result(session, actor, action, false, "paused");
        }
        if (action.Op == "move" && action.Dx == 0 && action.Dy == 0)
            // Releasing WASD sends a zero vector; it is not an action, so it must not interrupt an
            // activity that was chosen a moment earlier.
        {
            session.Commit();
            return Result(session, actor, action, true, activity: ActivityState.From(actor.Activity));
        }
        var handler = OpCatalog.HandlerFor(action.Op);
        var ctx = new ActionContext(session, world, actor, Grid, deltaSeconds, LoadKg(actorId));
        var reason = handler.Validate(ctx, action);
        // A rejection changes nothing, a running activity included.
        if (reason is not null)
        {
            session.Commit();
            return Result(session, actor, action, false, reason, activity: ActivityState.From(actor.Activity));
        }

        var events = new List<SimEvent>();
        if (ActivityState.From(actor.Activity) is { } running)
        {
            ActivityWork.SaveOnInterrupt(session, actor, running, world.GameMinute);
            actor.Activity = null;
            events.Add(SimEvent.ActivityFinished(actor.Id, running.Op, "interrupted", action.Op));
        }
        string? text = null;
        ActivityState? activity = null;
        IReadOnlyList<PhysicsPosition> trajectory = Array.Empty<PhysicsPosition>();
        var duration = handler.Duration(ctx, action);
        var workKey = WorkKey(action);
        if (duration == 0)
        {
            if (handler.RetainsWorkProgress) session.SetActivityWorkMinutes(actor.Id, workKey, 0);
            var resolution = handler.Resolve(ctx, action);
            events.AddRange(resolution.Events);
            text = resolution.Text;
            trajectory = resolution.Trajectory ?? new List<PhysicsPosition>();
        }
        else
        {
            var progress = handler.RetainsWorkProgress ? session.ActivityWorkMinutes(actor.Id, workKey) : 0;
            if (progress >= duration)
            {
                if (handler.RetainsWorkProgress) session.SetActivityWorkMinutes(actor.Id, workKey, 0);
                var resolution = handler.Complete(ctx, action);
                events.AddRange(resolution);
                events.Add(SimEvent.ActivityFinished(actor.Id, action.Op, "completed"));
            }
            else
            {
                activity = new ActivityState(action.Op, action.ToJson(), world.GameMinute - progress,
                    world.GameMinute + duration - progress);
                actor.Activity = activity.ToJson();
                var target = action.Op == "move" ? new JsonObject() : action.Target!.ToJson();
                events.Add(SimEvent.ActivityStarted(actor.Id, action.Op, target, activity.EndsMinute));
            }
        }
        Publish(session, world, events);
        if (handler.ChangesLoad) RefreshLoad(session);
        var result = Result(session, actor, action, true, text: text, activity: activity, trajectory: trajectory);
        Bus.Drain();
        return result;
    }

    /// <summary>Commit an Extra's intention. The brain proposes; the engine writes and logs it.</summary>
    public void SetGoal(string actorId, GoalSpot? goal, string reason)
    {
        var session = NewSession();
        var world = session.World;
        var actor = session.GetActor(actorId) ?? throw new KeyNotFoundException($"unknown actor: {actorId}");
        if (actor.Id == Ids.Player) throw new ArgumentException("the player is not driven by a goal");
        if (goal is not null && !Grid.StandingSurfaces(goal.X, goal.Y).Any(s => s.H == goal.H))
            throw new ArgumentException("goal tile has no standing surface");
        var mind = actor.Mind is { } stored ? Mind.Parse(stored) : new Mind(new TilePos(actor.TileX, actor.TileY, actor.H));
        actor.Mind = (mind with { Goal = goal }).ToJson();
        session.RecordInput("set_goal", Json.Obj(("actor_id", Json.Of(actorId)), ("goal", goal?.ToJson()),
            ("reason", Json.Of(reason))));
        Publish(session, world, new List<SimEvent> { SimEvent.ActorGoalSet(actorId, goal?.ToJson(), reason) });
        Bus.Drain();
    }

    public WorldState AdvanceTime()
    {
        _db.BeginWriteBatch();
        _dispatchingTick = true;
        try
        {
            var session = NewSession();
            var world = session.World;
            session.RecordInput("advance_time", new JsonObject());
            if (!world.Paused)
            {
                world.GameMinute += 1;
                // Completions come before clock.ticked, in actor-id order: replay needs it.
                var events = CompleteActivities(session, world, out var changesLoad);
                events.Add(SimEvent.ClockTicked());
                Publish(session, world, events);
                if (changesLoad) RefreshLoad(session);
            }
            else
            {
                session.Commit();
            }
            var state = GetState();
            _tickSnapshot = state;
            Bus.Drain();
            return state;
        }
        finally
        {
            _tickSnapshot = null;
            _dispatchingTick = false;
            _db.EndWriteBatch();
        }
    }

    private List<SimEvent> CompleteActivities(Session session, WorldMetaRow world, out bool changesLoad)
    {
        changesLoad = false;
        var events = new List<SimEvent>();
        foreach (var actor in session.Actors())
        {
            var running = ActivityState.From(actor.Activity);
            if (running is null || running.EndsMinute > world.GameMinute) continue;
            actor.Activity = null;
            var action = GameAction.Parse(running.Action);
            var handler = OpCatalog.HandlerFor(action.Op);
            if (handler.RetainsWorkProgress) session.SetActivityWorkMinutes(actor.Id, WorkKey(action), 0);
            var ctx = new ActionContext(session, world, actor, Grid, 0, LoadKg(actor.Id));
            // The world may have changed since the start; effects apply only if still valid.
            var reason = handler.Validate(ctx, action);
            if (reason is not null)
            {
                events.Add(SimEvent.ActivityFinished(actor.Id, running.Op, "failed", reason));
                continue;
            }
            events.AddRange(handler.Complete(ctx, action));
            events.Add(SimEvent.ActivityFinished(actor.Id, running.Op, "completed"));
            changesLoad |= handler.ChangesLoad;
        }
        return events;
    }

    public WorldState SetClock(bool? paused = null, int? speed = null)
    {
        var session = NewSession();
        var world = session.World;
        session.RecordInput("set_clock", Json.Obj(("paused", Json.Of(paused)), ("speed", Json.Of(speed))));
        var (oldSpeed, oldPaused) = (world.Speed, world.Paused);
        if (paused is not null) world.Paused = paused.Value;
        if (speed is not null)
        {
            if (speed is not (1 or 3 or 10)) throw new ArgumentException("speed must be 1, 3, or 10");
            world.Speed = speed.Value;
        }
        if ((world.Speed, world.Paused) != (oldSpeed, oldPaused))
            Publish(session, world, new List<SimEvent> { SimEvent.ClockChanged(world.Speed, world.Paused) });
        else
            session.Commit();
        var state = GetState();
        Bus.Drain();
        return state;
    }

    public IReadOnlyList<InputJournalEntry> ReadInputJournal() => _db.ReadInputJournal();

    private static string WorkKey(GameAction action) => action.ToJson().ToJsonString();

    public MenuPayload Menu(string actorId, double x, double y, int z, int radius = 0) =>
        Engine.Menu.Build(NewSession(), Grid, Registry, LoadKg(actorId), actorId, x, y, z, radius);

    public WorldPickHit? Pick(WorldRay ray, double maxDistance = 96)
    {
        if (!double.IsFinite(ray.X) || !double.IsFinite(ray.Y) || !double.IsFinite(ray.Height) ||
            !double.IsFinite(ray.Dx) || !double.IsFinite(ray.Dy) || !double.IsFinite(ray.DHeight))
            throw new ArgumentException("pick ray values must be finite", nameof(ray));
        if (!double.IsFinite(maxDistance) || maxDistance is <= 0 or > 256) throw new ArgumentOutOfRangeException(nameof(maxDistance));
        var length = Math.Sqrt(ray.Dx * ray.Dx + ray.Dy * ray.Dy + ray.DHeight * ray.DHeight);
        if (!double.IsFinite(length) || length < 1e-9) throw new ArgumentException("pick ray direction must be non-zero", nameof(ray));
        var dx = ray.Dx / length;
        var dy = ray.Dy / length;
        var dh = ray.DHeight / length;
        var actorBuckets = NewSession().Actors().OrderBy(a => a.Id, StringComparer.Ordinal)
            .GroupBy(a => (a.TileX, a.TileY)).ToDictionary(g => g.Key, g => g.ToArray());
        var surfaceCache = new Dictionary<(int X, int Y), List<(int H, Target Target)>>();

        List<(int H, Target Target)> Surfaces(int x, int y)
        {
            if (surfaceCache.TryGetValue((x, y), out var cached)) return cached;
            var byHeight = new Dictionary<int, Target>();
            if (Grid.GroundAt(x, y) is { } ground && !Grid.IsVoid(x, y, ground.GroundH))
                byHeight[ground.GroundH] = new TileTarget(x, y, ground.GroundH);
            var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(x, y);
            var index = Chunk.Index(lx, ly);
            foreach (var level in Grid.Levels.Values.Where(l => l.Cx == cx && l.Cy == cy))
                if (level.FloorH[index] != ChunkConst.NoFloor)
                    byHeight[level.FloorH[index]] = new TileTarget(x, y, level.FloorH[index]);
            foreach (var obj in Grid.ObjectsAt(x, y))
                if (Grid.KindOf(obj) is { Surface: true } kind)
                    byHeight[obj.H + kind.Height] = new ObjectTarget(obj.Id);
            cached = byHeight.Select(p => (p.Key, p.Value)).OrderByDescending(p => p.Key)
                .Select(p => (p.Key, p.Value)).ToList();
            surfaceCache[(x, y)] = cached;
            return cached;
        }

        var previousX = PyMath.Floor(ray.X);
        var previousY = PyMath.Floor(ray.Y);
        var previousPx = ray.X;
        var previousPy = ray.Y;
        var previousHeight = ray.Height;
        var previousLoaded = Grid.GroundAt(previousX, previousY) is not null;
        const double step = 0.05;
        for (double distance = 0; distance <= maxDistance; distance += step)
        {
            var px = ray.X + dx * distance;
            var py = ray.Y + dy * distance;
            var height = ray.Height + dh * distance;
            var x = PyMath.Floor(px);
            var y = PyMath.Floor(py);
            var halfHeight = PyMath.Floor(height * 2);
            if (Grid.GroundAt(x, y) is null)
            {
                previousX = x;
                previousY = y;
                previousPx = px;
                previousPy = py;
                previousHeight = height;
                previousLoaded = false;
                continue;
            }

            if (previousLoaded && (x, y) != (previousX, previousY) && Math.Abs(x - previousX) + Math.Abs(y - previousY) == 1)
            {
                var z = PyMath.FloorDiv(halfHeight, ChunkConst.LevelH);
                if (Grid.WallBetween(previousX, previousY, x, y, halfHeight))
                {
                    var verticalEdge = x != previousX;
                    var forward = verticalEdge ? x > previousX : y > previousY;
                    var direction = verticalEdge ? "west" : "north";
                    var (edgeX, edgeY) = forward ? (x, y) : (previousX, previousY);
                    var target = new EdgeTarget(edgeX, edgeY, z, direction);
                    return new WorldPickHit(target, edgeX, edgeY, halfHeight, px, py, height);
                }
                // A bare edge is addressable so a wall can be built where there is none, but only in
                // a band that already carries a level: otherwise a near-horizontal ray would stop at
                // every tile seam and never reach anything.
                var vertical = x != previousX;
                var ahead = vertical ? x > previousX : y > previousY;
                var (seamX, seamY) = ahead ? (x, y) : (previousX, previousY);
                var (seamCx, seamCy, _, _) = WorldGrid.ChunkCoords(seamX, seamY);
                if (Grid.Level(seamCx, seamCy, z) is not null)
                {
                    var seam = new EdgeTarget(seamX, seamY, z, vertical ? "west" : "north");
                    return new WorldPickHit(seam, seamX, seamY, halfHeight, px, py, height);
                }
            }

            if (previousLoaded && (x, y) == (previousX, previousY))
            {
                var crossedV = previousPx < x + 0.5 && px >= x + 0.5 || previousPx > x + 0.5 && px <= x + 0.5;
                var crossedH = previousPy < y + 0.5 && py >= y + 0.5 || previousPy > y + 0.5 && py <= y + 0.5;
                var slot = crossedV ? ChunkConst.SlotHalfV : crossedH ? ChunkConst.SlotHalfH : (byte)0;
                var direction = crossedV ? WallSlots.HalfV : WallSlots.HalfH;
                if (slot != 0 && Grid.InteriorWallLevelAtHeight(x, y, slot, halfHeight) is { } interiorZ)
                {
                    var target = new EdgeTarget(x, y, interiorZ, direction);
                    return new WorldPickHit(target, x, y, halfHeight, px, py, height);
                }
            }

            if (actorBuckets.TryGetValue((x, y), out var actors))
                foreach (var actor in actors)
                {
                    var ax = actor.X;
                    var ay = actor.Y;
                    var dxActor = px - ax;
                    var dyActor = py - ay;
                    var baseHeight = actor.H * 0.5;
                    if (dxActor * dxActor + dyActor * dyActor <= 0.3 * 0.3 && height >= baseHeight && height <= baseHeight + 1.7)
                    {
                        var target = new ActorTarget(actor.Id);
                        return new WorldPickHit(target, x, y, actor.H, px, py, height);
                    }
                }

            foreach (var obj in Grid.ObjectsAt(x, y))
                if (Grid.KindOf(obj) is { Solid: true } kind && obj.H < halfHeight && halfHeight <= obj.H + kind.Height)
                {
                    var target = new ObjectTarget(obj.Id);
                    return new WorldPickHit(target, x, y, obj.H, px, py, height);
                }

            if (dh < 0 && height <= previousHeight)
                foreach (var surface in Surfaces(x, y))
                {
                    var surfaceHeight = surface.H * 0.5;
                    if (previousHeight >= surfaceHeight && height <= surfaceHeight)
                        return new WorldPickHit(surface.Target, x, y, surface.H, px, py, surfaceHeight);
                }

            if (Grid.SolidAt(x, y, halfHeight))
            {
                var surface = Surfaces(x, y).FirstOrDefault(s => s.H >= halfHeight);
                var target = surface.Target ?? new TileTarget(x, y, halfHeight);
                var targetH = surface.Target is null ? halfHeight : surface.H;
                return new WorldPickHit(target, x, y, targetH, px, py, height);
            }

            previousX = x;
            previousY = y;
            previousPx = px;
            previousPy = py;
            previousHeight = height;
            previousLoaded = true;
        }
        return null;
    }

    public WorldState GetState()
    {
        var session = NewSession();
        var world = session.World;
        var actors = session.Actors().Select(a => new ActorState(a.Id, a.Kind, a.X, a.Y, a.Z, a.H, ActivityState.From(a.Activity),
            Carried(a.Id), LoadKg(a.Id), a.Name, a.Mind is { } m ? Mind.Parse(m) : null, ActorNeeds.Parse(a.Needs))).ToList();
        return new WorldState(world.Seed, world.GameMinute, world.Speed, world.Paused, actors, world.GenVersion, world.Generator,
            (JsonObject)world.GenOptions.DeepClone());
    }

    public WorldState GetTickState() => _dispatchingTick ? _tickSnapshot ??= GetState() : GetState();

    public (int GameMinute, int Speed, bool Paused) ClockState()
    {
        var world = NewSession().World;
        return (world.GameMinute, world.Speed, world.Paused);
    }

    public List<EventRow> ReadEvents(int afterSeq = 0, int limit = 100, string? type = null, string? actorId = null) =>
        _db.ReadEvents(afterSeq, limit, type, actorId);

    public ChunkPayload? ChunkPayloadAt(int cx, int cy) => ChunkPayload.Of(Grid, cx, cy);

    public List<ChunkPayload> ChunksNear(int cx, int cy, int radius = ChunkRadius)
    {
        var result = new List<ChunkPayload>();
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
            if (ChunkPayload.Of(Grid, cx + dx, cy + dy) is { } payload) result.Add(payload);
        return result;
    }

    /// <summary>Partly damaged wall edges, as stored.</summary>
    public IReadOnlyDictionary<WallKey, double> WallRows() => _store.Walls;

    /// <summary>Test and tooling access to the committed state; handlers never use it.</summary>
    public Session OpenSession() => NewSession();

    /// <summary>After a test edits rows directly, rebuild the tile-object index and the load cache.</summary>
    public void Reindex()
    {
        var session = NewSession();
        LoadObjectIndex(session);
        RefreshLoad(session);
    }
}
