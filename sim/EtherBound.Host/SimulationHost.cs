using System.Diagnostics;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using EtherBound.Llm;
using EtherBound.Sim;
using EtherBound.Sim.Clock;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Minds;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;
using EtherBound.Sim.Events;

namespace EtherBound.Host;

/// <summary>Owns the only sim thread and publishes detached frames for the client to draw.</summary>
public sealed partial class SimulationHost : IDisposable
{
    /// <summary>WASD steps per second; the client paces its <c>Move</c> commands and interpolation by it.</summary>
    public const int MoveHz = 20;

    private abstract record Command(int RequestId = 0);
    private sealed record Move(double Dx, double Dy, double DeltaSeconds) : Command;
    private sealed record Clock(int? Speed, bool? Paused) : Command;
    private sealed record NewGame(long Seed, string Generator, string? OptionsJson, bool Paused, int Request = 0) : Command(Request);
    private sealed record SubmitAction(int Request, GameAction Action) : Command(Request);
    private sealed record MenuQuery(int Request, string ActorId, double X, double Y, int Z, int Radius) : Command(Request);
    private sealed record MenuRay(int Request, WorldRay Ray, int Radius) : Command(Request);
    private sealed record RadialMenu(int Request) : Command(Request);
    private sealed record PickRay(int Request, WorldRay Ray) : Command(Request);

    private readonly string _databasePath;
    private readonly long _seed;
    private readonly string _generator;
    private readonly int _chunkRadius;

    // The minimap's per-chunk cells and the last assembled image; both belong to the sim thread.
    public const int MinimapRadiusChunks = 4;
    private readonly Dictionary<(int Cx, int Cy), (int Revision, byte[] Cells)> _mapChunks = new();
    private MapSampler? _mapSampler;
    private HostMinimap? _minimap;
    private (int Cx, int Cy)? _streamingChunk;
    private bool _mapPending;

    // Colouring a whole window on every chunk crossing would be a hitch, so after the first window the work
    // is spread over a few publishes: unfinished chunks draw dark until their turn.
    private const int MapChunksPerPublish = 9;
    private readonly Func<WorldEngineCatalogs> _loadCatalogs;
    private readonly LlmRuntime? _llm;
    private readonly Channel<Command> _commands = Channel.CreateBounded<Command>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly Channel<HostResponse> _responses = Channel.CreateUnbounded<HostResponse>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true,
    });
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _frameChanged = new(false);
    private readonly Thread _thread;
    private WorldFrame? _latestFrame;
    private Exception? _fault;
    private int _disposed;
    private readonly long _startedAt;
    private double _firstFrameMilliseconds, _engineInitializationMilliseconds, _worldSetupMilliseconds,
        _framePreparationMilliseconds, _snapshotBuildMilliseconds;
    private long _frameSequence;
    private long _movesApplied;

    public SimulationHost(string databasePath = ":memory:", long seed = 7, string generator = "test", int chunkRadius = 2,
        Func<WorldEngineCatalogs>? loadCatalogs = null, LlmRuntime? llm = null)
    {
        if (chunkRadius < 0) throw new ArgumentOutOfRangeException(nameof(chunkRadius));
        _startedAt = Stopwatch.GetTimestamp();
        _loadCatalogs = loadCatalogs ?? WorldEngine.LoadCatalogs;
        _llm = llm;
        _databasePath = databasePath;
        _seed = seed;
        _generator = generator;
        _chunkRadius = chunkRadius;
        _thread = new Thread(Run) { IsBackground = true, Name = "EtherBound simulation" };
        _thread.Start();
    }

    public WorldFrame? LatestFrame => Volatile.Read(ref _latestFrame);
    public Exception? Fault => Volatile.Read(ref _fault);
    public double FirstFrameMilliseconds => Volatile.Read(ref _firstFrameMilliseconds);
    public double EngineInitializationMilliseconds => Volatile.Read(ref _engineInitializationMilliseconds);
    public double WorldSetupMilliseconds => Volatile.Read(ref _worldSetupMilliseconds);
    public double FramePreparationMilliseconds => Volatile.Read(ref _framePreparationMilliseconds);
    public double SnapshotBuildMilliseconds => Volatile.Read(ref _snapshotBuildMilliseconds);

    internal int WorkerThreadId => _thread.ManagedThreadId;

    public bool WaitUntilReady(TimeSpan timeout)
    {
        if (!_ready.Wait(timeout)) return false;
        if (Fault is { } fault) throw new InvalidOperationException("simulation host failed to initialize", fault);
        return LatestFrame is not null;
    }

    public bool TryMove(double dx, double dy, double deltaSeconds = 1.0 / MoveHz)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || dx is < -1 or > 1 || dy is < -1 or > 1 ||
            !double.IsFinite(deltaSeconds) || deltaSeconds is <= 0 or > 1) return false;
        return Enqueue(new Move(dx, dy, deltaSeconds));
    }

    public bool TrySetClock(int? speed = null, bool? paused = null)
    {
        if (speed is not null and not (1 or 3 or 10) || (speed is null && paused is null)) return false;
        return Enqueue(new Clock(speed, paused));
    }

    public bool TryNewGame(long seed, string generator = "test") =>
        !string.IsNullOrWhiteSpace(generator) && Enqueue(new NewGame(seed, generator, null, false));

    public bool TryNewGame(int requestId, long seed, string generator = "test", string? optionsJson = null, bool paused = false) =>
        requestId > 0 && !string.IsNullOrWhiteSpace(generator) && Enqueue(new NewGame(seed, generator, optionsJson, paused, requestId));

    public bool TrySubmitAction(int requestId, GameAction action) =>
        requestId > 0 && Enqueue(new SubmitAction(requestId, action));

    public bool TryRequestMenu(int requestId, double x, double y, int z, int radius = 0, string actorId = Ids.Player) =>
        requestId > 0 && radius is >= 0 and <= 1 && Enqueue(new MenuQuery(requestId, actorId, x, y, z, radius));

    public bool TryRequestMenuAtRay(int requestId, WorldRay ray, int radius = 0) =>
        requestId > 0 && radius is >= 0 and <= 1 && Enqueue(new MenuRay(requestId, ray, radius));

    public bool TryRequestRadialMenu(int requestId) => requestId > 0 && Enqueue(new RadialMenu(requestId));

    public bool TryPick(int requestId, WorldRay ray) => requestId > 0 && Enqueue(new PickRay(requestId, ray));

    public bool TryReadResponse(out HostResponse? response)
    {
        if (_responses.Reader.TryRead(out var next))
        {
            response = next;
            return true;
        }
        response = null;
        return false;
    }

    internal bool WaitForFrameAfter(long sequence, TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            _frameChanged.Reset();
            if (LatestFrame is { } frame && frame.Sequence > sequence) return true;
            var remainingTicks = deadline - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0 || !_frameChanged.Wait(TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency)))
                return LatestFrame is { } latest && latest.Sequence > sequence;
        }
    }

    private bool Enqueue(Command command)
    {
        if (Volatile.Read(ref _disposed) != 0 || Fault is not null || !_commands.Writer.TryWrite(command)) return false;
        _wake.Set();
        return true;
    }

    private void Run()
    {
        WorldEngine? engine = null;
        try
        {
            var engineStarted = Stopwatch.GetTimestamp();
            var existed = _databasePath != ":memory:" && File.Exists(_databasePath);
            engine = new WorldEngine(_databasePath, _loadCatalogs);
            var engineReady = Stopwatch.GetTimestamp();
            WorldState state;
            if (existed)
            {
                engine.EnsureWorld(_seed);
                state = engine.GetState();
            }
            else
            {
                state = engine.NewGame(_seed, _generator);
            }
            var worldReady = Stopwatch.GetTimestamp();
            new ExtrasBrain(engine).Attach(engine.Bus);
            engine.Bus.Subscribe("*", e =>
            {
                // Model decisions are log entries, not things Niko did: they never reach the action feed.
                if (!e.Logged || e.ActorId != Ids.Player || e.Type.StartsWith("llm.", StringComparison.Ordinal)) return;
                _pendingEvents.Add(new HostSimulationEvent(e.Seq, e.GameMinute, e.Type, e.ActorId, e.Data.ToJsonString()));
            }, "host.player-events", Phase.Replication);
            StartLlm(engine);

            var clock = new SimClock();
            clock.Load(state.Speed, state.Paused);
            var materials = engine.Registry.Materials.Select(m => new HostMaterial(m.Id, m.Key, m.Name, m.Color, m.Liquid)).ToArray();
            var objectKinds = engine.Catalog.Kinds.Select(k => new HostObjectKind(k.Key, k.Name, k.Material, k.Height, k.Solid, k.Surface)).ToArray();
            var generators = Generators.All.Values.Select(spec => new HostGenerator(spec.Key, spec.Name, spec.Version,
                ImmutableArray.CreateRange(spec.Fields.Select(field => new HostOptionField(field.Path, field.Label, field.Kind,
                    field.Default?.ToJsonString(), field.Minimum, field.Maximum, field.Step,
                    ImmutableArray.CreateRange(field.Choices ?? Array.Empty<string>()), field.Group))),
                 ImmutableArray.CreateRange(spec.Bays.Select(bay => new HostGeneratorBay(bay.Key, bay.X, bay.Y, bay.Width, bay.Height))))).ToArray();
            var chunks = new Dictionary<(int Cx, int Cy), HostChunk>();
            var beforePublish = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.StartNew();
            var last = elapsed.Elapsed.TotalSeconds;
            Volatile.Write(ref _engineInitializationMilliseconds,
                Stopwatch.GetElapsedTime(engineStarted, engineReady).TotalMilliseconds);
            Volatile.Write(ref _worldSetupMilliseconds,
                Stopwatch.GetElapsedTime(engineReady, worldReady).TotalMilliseconds);
            Volatile.Write(ref _framePreparationMilliseconds,
                Stopwatch.GetElapsedTime(worldReady, beforePublish).TotalMilliseconds);
            Publish(engine, state, materials, objectKinds, generators, chunks);
            _ready.Set();

            while (Volatile.Read(ref _disposed) == 0)
            {
                var now = elapsed.Elapsed.TotalSeconds;
                var due = clock.Advance(now - last);
                last = now;
                var changed = false;
                for (var i = 0; i < due; i++)
                {
                    state = engine.AdvanceTime();
                    QueueEvents();
                    changed = true;
                }

                while (_commands.Reader.TryRead(out var command))
                {
                    try
                    {
                        switch (command)
                        {
                            case Move move:
                                engine.Submit(Ids.Player, GameAction.Move(move.Dx, move.Dy), move.DeltaSeconds);
                                QueueEvents();
                                _movesApplied++;
                                changed = true;
                                break;
                            case Clock setClock:
                                state = engine.SetClock(setClock.Paused, setClock.Speed);
                                clock.Load(state.Speed, state.Paused);
                                QueueEvents();
                                changed = true;
                                break;
                            case NewGame start:
                                var options = start.OptionsJson is null ? null : JsonNode.Parse(start.OptionsJson) as JsonObject
                                    ?? throw new ArgumentException("generator options must be a JSON object");
                                state = engine.NewGame(start.Seed, start.Generator, options, start.Paused);
                                clock.Load(state.Speed, state.Paused);
                                chunks.Clear();
                                _mapChunks.Clear();
                                _minimap = null;
                                _streamingChunk = null;
                                ResetLlmForNewWorld();
                                var newGameEvents = TakeEvents();
                                if (start.RequestId > 0)
                                    _responses.Writer.TryWrite(new HostNewGameResponse(start.RequestId, start.Seed, state.Generator, newGameEvents));
                                else if (!newGameEvents.IsEmpty)
                                    _responses.Writer.TryWrite(new HostEventsResponse(0, newGameEvents));
                                changed = true;
                                break;
                            case SubmitAction action:
                                var result = engine.Submit(Ids.Player, action.Action);
                                NoteInspection(result);
                                _responses.Writer.TryWrite(new HostActionResponse(action.RequestId,
                                    HostActionResult.Copy(action.RequestId, result), TakeEvents()));
                                changed = true;
                                break;
                            case MenuQuery menu:
                                _responses.Writer.TryWrite(new HostMenuResponse(menu.RequestId,
                                    HostMenuPayload.Copy(engine.Menu(menu.ActorId, menu.X, menu.Y, menu.Z, menu.Radius)), null));
                                break;
                            case RadialMenu radial:
                                var player = engine.GetState().Actors.First(actor => actor.Id == Ids.Player);
                                _responses.Writer.TryWrite(new HostMenuResponse(radial.RequestId,
                                    HostMenuPayload.Copy(engine.Menu(Ids.Player, player.X, player.Y, player.Z, 1)), null));
                                break;
                            case PickRay pick:
                                _responses.Writer.TryWrite(new HostPickResponse(pick.RequestId, ToHostPick(engine.Pick(pick.Ray))));
                                break;
                            case InterpretText ask:
                                HandleInterpretText(engine, ask);
                                break;
                            case InterpretFailed failed:
                                HandleInterpretFailed(engine, failed);
                                break;
                            case InterpretDone done:
                                changed |= HandleInterpretDone(engine, done);
                                break;
                            case NarrationDelta delta:
                                _responses.Writer.TryWrite(new HostNarrationDelta(delta.Request, delta.Text));
                                break;
                            case NarrationRestart restart:
                                _responses.Writer.TryWrite(new HostNarrationRestart(restart.Request));
                                break;
                            case NarrationDone narrated:
                                HandleNarrationDone(engine, narrated);
                                break;
                            case NarrationHistoryQuery history:
                                HandleNarrationHistory(engine, history);
                                break;
                            case MenuRay menuRay:
                                var hit = engine.Pick(menuRay.Ray);
                                var payload = hit is null ? null : engine.Menu(Ids.Player,
                                    hit.TileX + 0.5, hit.TileY + 0.5,
                                    PyMath.FloorDiv(hit.TileH, ChunkConst.LevelH), menuRay.Radius);
                                _responses.Writer.TryWrite(new HostMenuResponse(menuRay.RequestId,
                                    payload is null ? null : HostMenuPayload.Copy(payload), ToHostPick(hit)));
                                break;
                        }
                    }
                    catch (Exception error)
                    {
                        if (command.RequestId == 0) throw;
                        _responses.Writer.TryWrite(new HostErrorResponse(command.RequestId, error.Message));
                    }
                }

                MaybeNarrate(engine);
                if (changed || _mapPending) Publish(engine, engine.GetState(), materials, objectKinds, generators, chunks);
                MaintainStreaming(engine);
                if (Volatile.Read(ref _disposed) == 0)
                    _wake.WaitOne(_mapPending ? 1 : Math.Clamp((int)Math.Ceiling(clock.UntilNextTick * 1000), 1, 1000));
            }
        }
        catch (Exception error)
        {
            Volatile.Write(ref _fault, error);
        }
        finally
        {
            engine?.Dispose();
            _commands.Writer.TryComplete();
            _responses.Writer.TryComplete();
            _ready.Set();
            _frameChanged.Set();
        }
    }

    private void Publish(WorldEngine engine, WorldState state, HostMaterial[] materials, HostObjectKind[] objectKinds,
        HostGenerator[] generators, Dictionary<(int Cx, int Cy), HostChunk> chunks)
    {
        var started = Stopwatch.GetTimestamp();
        var player = state.Actors.First(actor => actor.Id == Ids.Player);
        var cx = (int)Math.Floor(player.X / ChunkConst.Size);
        var cy = (int)Math.Floor(player.Y / ChunkConst.Size);
        var payloads = engine.ChunksNear(cx, cy, _chunkRadius);
        var current = new HashSet<(int Cx, int Cy)>();
        foreach (var payload in payloads)
        {
            var key = (payload.Cx, payload.Cy);
            current.Add(key);
            if (!chunks.TryGetValue(key, out var cached) || cached.Revision != payload.Revision)
                chunks[key] = HostChunk.Copy(payload);
        }
        foreach (var key in chunks.Keys.Where(key => !current.Contains(key)).ToArray()) chunks.Remove(key);

        var frame = new WorldFrame(
            Interlocked.Increment(ref _frameSequence),
            state.Seed,
            state.GameMinute,
            state.Speed,
            state.Paused,
            state.Generator,
            state.GenVersion,
            state.GenOptions.ToJsonString(),
            ImmutableArray.CreateRange(generators),
            ImmutableArray.CreateRange(state.Actors.Select(a => new HostActor(a.Id, a.Kind, a.Name, a.X, a.Y, a.Z, a.H,
                a.Activity is { } activity ? new HostActivity(activity.Op, activity.StartedMinute, activity.EndsMinute) : null,
                ImmutableArray.CreateRange(a.Carried.Select(item => new HostCarriedObject(item.Id, item.Kind, item.Name, item.Quantity, item.Slot))),
                a.LoadKg,
                a.Needs is { } needs ? new HostNeeds(needs.Level(NeedCatalog.Hunger, state.GameMinute),
                    needs.Level(NeedCatalog.Thirst, state.GameMinute), needs.Level(NeedCatalog.Rest, state.GameMinute)) : null))),
            System.Collections.Immutable.ImmutableArray.CreateRange(materials),
            System.Collections.Immutable.ImmutableArray.CreateRange(objectKinds),
            System.Collections.Immutable.ImmutableArray.CreateRange(payloads.Select(p => chunks[(p.Cx, p.Cy)])),
            _movesApplied,
            BuildMinimap(engine, cx, cy));
        if (frame.Sequence == 1)
        {
            Volatile.Write(ref _snapshotBuildMilliseconds, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Volatile.Write(ref _firstFrameMilliseconds, Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds);
        }
        Volatile.Write(ref _latestFrame, frame);
        _frameChanged.Set();
    }

    /// <summary>
    /// Keeps a streaming world's memory and latency in check from the sim thread: it forgets chunks far from
    /// every actor when Niko changes chunk, and loads one chunk of the next ring per turn so a crossing finds it ready.
    /// Both are pure cache work, so neither touches state or the replay.
    /// </summary>
    private void MaintainStreaming(WorldEngine engine)
    {
        if (!engine.Grid.Streaming) return;
        var player = engine.OpenSession().GetActor(Ids.Player);
        if (player is null) return;
        var at = (Cx: PyMath.FloorDiv(player.TileX, ChunkConst.Size), Cy: PyMath.FloorDiv(player.TileY, ChunkConst.Size));
        if (_streamingChunk != at)
        {
            _streamingChunk = at;
            engine.EvictFarChunks();
        }
        engine.PrefetchChunk(at.Cx, at.Cy, _chunkRadius + 1);
    }

    private HostMinimap BuildMinimap(WorldEngine engine, int cx, int cy)
    {
        _mapSampler ??= new MapSampler(engine.Registry);
        var side = 2 * MinimapRadiusChunks + 1;
        int originCx = cx - MinimapRadiusChunks, originCy = cy - MinimapRadiusChunks;
        var changed = _minimap is null || _minimap.OriginCx != originCx || _minimap.OriginCy != originCy;
        var window = new List<(int Cx, int Cy)>();
        for (var dy = 0; dy < side; dy++)
        for (var dx = 0; dx < side; dx++) window.Add((originCx + dx, originCy + dy));
        // Nearest to Niko first, so the part of the map he is looking at fills before the edges.
        window.Sort((a, b) => Math.Max(Math.Abs(a.Cx - cx), Math.Abs(a.Cy - cy)).CompareTo(Math.Max(Math.Abs(b.Cx - cx), Math.Abs(b.Cy - cy))));
        var computed = 0;
        // A new world's first window is built whole, so no stray frame follows the first one; later changes
        // (a chunk crossing brings 9 or 17 new chunks) are paced.
        var budget = _minimap is null ? int.MaxValue : MapChunksPerPublish;
        _mapPending = false;
        foreach (var key in window)
        {
            var revision = engine.MapRevision(key.Cx, key.Cy);
            if (_mapChunks.TryGetValue(key, out var cached) && cached.Revision == revision) continue;
            if (computed >= budget)
            {
                _mapPending = true;
                continue;
            }
            _mapChunks[key] = (revision, _mapSampler.Chunk(engine.SurfaceAt, key.Cx, key.Cy));
            computed++;
            changed = true;
        }
        var inWindow = window.ToHashSet();
        foreach (var key in _mapChunks.Keys.Where(key => !inWindow.Contains(key)).ToArray()) _mapChunks.Remove(key);
        if (!changed) return _minimap!;

        const int cell = MapSampler.CellsPerChunk;
        var width = side * cell;
        var rgb = new byte[width * width * MapSampler.BytesPerCell];
        for (var dy = 0; dy < side; dy++)
        for (var dx = 0; dx < side; dx++)
        {
            if (!_mapChunks.TryGetValue((originCx + dx, originCy + dy), out var entry)) continue;
            var cells = entry.Cells;
            for (var row = 0; row < cell; row++)
                Buffer.BlockCopy(cells, row * cell * MapSampler.BytesPerCell, rgb,
                    ((dy * cell + row) * width + dx * cell) * MapSampler.BytesPerCell, cell * MapSampler.BytesPerCell);
        }
        return _minimap = new HostMinimap(originCx, originCy, side, cell, MapSampler.TilesPerCell,
            System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(rgb));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CancelLlmWork();
        _commands.Writer.TryComplete();
        _wake.Set();
        if (!ReferenceEquals(Thread.CurrentThread, _thread)) _thread.Join();
        _wake.Dispose();
        _frameChanged.Dispose();
        _ready.Dispose();
    }

    private static HostPick? ToHostPick(WorldPickHit? hit) => hit is null ? null :
        new HostPick(hit.Target, hit.TileX, hit.TileY, hit.TileH, hit.HitX, hit.HitY, hit.HitHeight);
}
