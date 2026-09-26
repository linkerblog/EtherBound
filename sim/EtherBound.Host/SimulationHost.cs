using System.Diagnostics;
using System.Threading.Channels;
using EtherBound.Sim.Clock;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Minds;
using EtherBound.Sim.World;

namespace EtherBound.Host;

/// <summary>Owns the only sim thread and publishes detached frames for the client to draw.</summary>
public sealed class SimulationHost : IDisposable
{
    private abstract record Command;
    private sealed record Move(double Dx, double Dy, double DeltaSeconds) : Command;
    private sealed record Clock(int? Speed, bool? Paused) : Command;
    private sealed record NewGame(long Seed, string Generator) : Command;

    private readonly string _databasePath;
    private readonly long _seed;
    private readonly string _generator;
    private readonly int _chunkRadius;
    private readonly Channel<Command> _commands = Channel.CreateBounded<Command>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
    });
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _frameChanged = new(false);
    private readonly Thread _thread;
    private WorldFrame? _latestFrame;
    private Exception? _fault;
    private int _disposed;
    private long _frameSequence;

    public SimulationHost(string databasePath = ":memory:", long seed = 7, string generator = "test", int chunkRadius = 2)
    {
        if (chunkRadius < 0) throw new ArgumentOutOfRangeException(nameof(chunkRadius));
        _databasePath = databasePath;
        _seed = seed;
        _generator = generator;
        _chunkRadius = chunkRadius;
        _thread = new Thread(Run) { IsBackground = true, Name = "EtherBound simulation" };
        _thread.Start();
    }

    public WorldFrame? LatestFrame => Volatile.Read(ref _latestFrame);
    public Exception? Fault => Volatile.Read(ref _fault);

    internal int WorkerThreadId => _thread.ManagedThreadId;

    public bool WaitUntilReady(TimeSpan timeout)
    {
        if (!_ready.Wait(timeout)) return false;
        if (Fault is { } fault) throw new InvalidOperationException("simulation host failed to initialize", fault);
        return LatestFrame is not null;
    }

    public bool TryMove(double dx, double dy, double deltaSeconds = 1.0 / 20)
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
        !string.IsNullOrWhiteSpace(generator) && Enqueue(new NewGame(seed, generator));

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
            var existed = _databasePath != ":memory:" && File.Exists(_databasePath);
            engine = new WorldEngine(_databasePath);
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
            new ExtrasBrain(engine).Attach(engine.Bus);

            var clock = new SimClock();
            clock.Load(state.Speed, state.Paused);
            var materials = engine.Registry.Materials.Select(m => new HostMaterial(m.Id, m.Key, m.Name, m.Color, m.Liquid)).ToArray();
            var objectKinds = engine.Catalog.Kinds.Select(k => new HostObjectKind(k.Key, k.Name, k.Material, k.Height, k.Solid, k.Surface)).ToArray();
            var chunks = new Dictionary<(int Cx, int Cy), HostChunk>();
            var elapsed = Stopwatch.StartNew();
            var last = elapsed.Elapsed.TotalSeconds;
            Publish(engine, state, materials, objectKinds, chunks);
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
                    changed = true;
                }

                while (_commands.Reader.TryRead(out var command))
                {
                    switch (command)
                    {
                        case Move move:
                            engine.Submit(Ids.Player, GameAction.Move(move.Dx, move.Dy), move.DeltaSeconds);
                            break;
                        case Clock setClock:
                            state = engine.SetClock(setClock.Paused, setClock.Speed);
                            clock.Load(state.Speed, state.Paused);
                            break;
                        case NewGame start:
                            state = engine.NewGame(start.Seed, start.Generator);
                            clock.Load(state.Speed, state.Paused);
                            chunks.Clear();
                            break;
                    }
                    changed = true;
                }

                if (changed) Publish(engine, engine.GetState(), materials, objectKinds, chunks);
                if (Volatile.Read(ref _disposed) == 0)
                    _wake.WaitOne(Math.Clamp((int)Math.Ceiling(clock.UntilNextTick * 1000), 1, 1000));
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
            _ready.Set();
            _frameChanged.Set();
        }
    }

    private void Publish(WorldEngine engine, WorldState state, HostMaterial[] materials, HostObjectKind[] objectKinds,
        Dictionary<(int Cx, int Cy), HostChunk> chunks)
    {
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
            System.Collections.Immutable.ImmutableArray.CreateRange(state.Actors.Select(a => new HostActor(
                a.Id, a.Kind, a.Name, a.X, a.Y, a.Z, a.H, a.Activity?.Op, a.LoadKg))),
            System.Collections.Immutable.ImmutableArray.CreateRange(materials),
            System.Collections.Immutable.ImmutableArray.CreateRange(objectKinds),
            System.Collections.Immutable.ImmutableArray.CreateRange(payloads.Select(p => chunks[(p.Cx, p.Cy)])));
        Volatile.Write(ref _latestFrame, frame);
        _frameChanged.Set();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _commands.Writer.TryComplete();
        _wake.Set();
        if (!ReferenceEquals(Thread.CurrentThread, _thread)) _thread.Join();
        _wake.Dispose();
        _frameChanged.Dispose();
        _ready.Dispose();
    }
}
