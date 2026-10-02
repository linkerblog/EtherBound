// EtherBound.Bench measures the sim on the lab map (Dev-025 [Sec. 3.3]): milliseconds per
// game-minute tick at x10 and per movement step, with 1,000, 5,000 and 10,000 Extras.
using System.Diagnostics;
using EtherBound.Sim;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Minds;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;

const int WarmupTicks = 5;
const int MeasuredTicks = 100;
int[] populations = { 1_000, 5_000, 10_000 };
bool runBrain = !args.Contains("--without-brain", StringComparer.Ordinal);
bool traceTicks = args.Contains("--trace", StringComparer.Ordinal);
// Dev-011: how the crowd's needs are seeded. `--no-needs` leaves the legacy rows (the old baseline), the
// default is the start a new game gives, and `--needy` makes everyone hungry so each idle tick reads a Menu.
var needsMode = args.Contains("--no-needs", StringComparer.Ordinal) ? "none" : args.Contains("--needy", StringComparer.Ordinal) ? "needy" : "start";
var extrasAt = Array.IndexOf(args, "--extras");
if (extrasAt >= 0 && extrasAt + 1 < args.Length) populations = new[] { int.Parse(args[extrasAt + 1], System.Globalization.CultureInfo.InvariantCulture) };

if (args.Contains("chunkgen", StringComparer.Ordinal)) return ChunkGenBench();

Console.WriteLine($"EtherBound.Bench — {Environment.ProcessorCount} logical cores, .NET {Environment.Version}");
Console.WriteLine(runBrain ? "  ExtrasBrain enabled" : "  ExtrasBrain disabled");
Console.WriteLine($"  needs: {needsMode}");
Console.WriteLine($"{"Extras",8} {"tick avg ms",12} {"tick p95 ms",12} {"move avg ms",12}");

foreach (var count in populations)
{
    using var engine = new WorldEngine();
    engine.NewGame(7, "lab");
    SeedExtras(engine, count, needsMode);
    if (runBrain) new ExtrasBrain(engine).Attach(engine.Bus);
    engine.SetClock(speed: 10);

    for (var i = 0; i < WarmupTicks; i++) engine.AdvanceTime();

    var tickTimes = new List<double>(MeasuredTicks);
    var diagnostics = new List<(int Tick, double Ms, int Gen0, int Gen1, int Gen2, long Bytes)>();
    var watch = new Stopwatch();
    for (var i = 0; i < MeasuredTicks; i++)
    {
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var allocated = traceTicks ? GC.GetAllocatedBytesForCurrentThread() : 0;
        watch.Restart();
        engine.AdvanceTime();
        watch.Stop();
        tickTimes.Add(watch.Elapsed.TotalMilliseconds);
        if (traceTicks)
            diagnostics.Add((i + 1, watch.Elapsed.TotalMilliseconds, GC.CollectionCount(0) - gen0,
                GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2, GC.GetAllocatedBytesForCurrentThread() - allocated));
    }
    tickTimes.Sort();
    var avg = tickTimes.Average();
    var p95 = tickTimes[(int)Math.Ceiling(tickTimes.Count * 0.95) - 1];

    if (traceTicks)
        foreach (var tick in diagnostics.OrderByDescending(t => t.Ms).Take(10))
            Console.WriteLine($"  tick {tick.Tick,3}: {tick.Ms,8:F3} ms; GC {tick.Gen0}/{tick.Gen1}/{tick.Gen2}; {tick.Bytes,10} bytes");

    var moveWatch = Stopwatch.StartNew();
    var result = engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.05);
    moveWatch.Stop();
    Console.WriteLine($"{count,8} {avg,12:F3} {p95,12:F3} {moveWatch.Elapsed.TotalMilliseconds,12:F3}");
    if (!result.Accepted) Console.WriteLine("  (move step was rejected; the bench spot may be blocked)");
}

return 0;

// The lab's open bay gives room for a synthetic crowd without touching the population algorithm's
// spacing search, which is not built for thousands of Extras.
static void SeedExtras(WorldEngine engine, int count, string needsMode)
{
    var session = engine.OpenSession();
    var rng = new Random(7);
    var placed = 0;
    var attempts = 0;
    while (placed < count && attempts < count * 4)
    {
        attempts++;
        var x = 4 + rng.Next(120);
        var y = 4 + rng.Next(120);
        if (engine.Grid.GroundAt(x, y) is not { } ground) continue;
        if (!engine.Grid.StandingSurfaces(x, y).Any(s => s.H == ground.GroundH)) continue;
        var id = $"bench-{placed:00000}";
        session.AddActor(new ActorRow
        {
            Id = id, Kind = "extra", Name = id, X = x + 0.5, Y = y + 0.5, H = ground.GroundH,
            Z = PyMath.FloorDiv(ground.GroundH, 6),
            Mind = new Mind(new TilePos(x, y, ground.GroundH)).ToJson(),
            Needs = needsMode switch
            {
                "none" => null,
                "needy" => ActorNeeds.Full(0).With(NeedCatalog.Hunger, 0.3, 0).ToJson(),
                _ => WorldSetup.StartingNeeds(7, id, 0).ToJson(),
            },
        });
        placed++;
    }
    session.Commit();
    engine.Reindex();
    Console.WriteLine($"  seeded {placed}/{count} Extras in {attempts} attempts");
}

// Dev-010: what streaming an endless world costs on the sim thread, in milliseconds, for the budgets in
// `docs/PENDING.md`: one pristine chunk, a first frame's 5x5 chunks, a chunk crossing's new chunks, and the
// minimap's colouring of one chunk and of one crossing's new row.
static int ChunkGenBench()
{
    Console.WriteLine($"EtherBound.Bench chunkgen — {Environment.ProcessorCount} logical cores, .NET {Environment.Version}");
    var registry = MaterialRegistry.Load();
    var spec = Generators.All["infinite"];
    var source = spec.Stream!(7, spec.Defaults, registry);
    for (var i = 0; i < 50; i++) source.Generate(i, -i);

    var times = new List<double>();
    for (var i = 0; i < 400; i++)
    {
        var started = Stopwatch.GetTimestamp();
        source.Generate(100 + i % 20, -100 - i / 20);
        times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    times.Sort();
    Console.WriteLine($"  generate one chunk:        avg {times.Average():F3} ms, p95 {times[(int)(times.Count * 0.95)]:F3} ms");

    using var engine = new WorldEngine();
    var started0 = Stopwatch.GetTimestamp();
    engine.NewGame(7, "infinite");
    var newGame = Stopwatch.GetElapsedTime(started0).TotalMilliseconds;
    var niko = engine.OpenSession().GetActor(Ids.Player)!;
    var (cx, cy) = (PyMath.FloorDiv(niko.TileX, ChunkConst.Size), PyMath.FloorDiv(niko.TileY, ChunkConst.Size));
    var frameStart = Stopwatch.GetTimestamp();
    engine.ChunksNear(cx, cy, 2);
    Console.WriteLine($"  new game (spawn search, kit): {newGame:F2} ms; first frame's chunks near Niko: {Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds:F2} ms");

    var crossing = Stopwatch.GetTimestamp();
    engine.ChunksNear(cx + 1, cy, 2);
    Console.WriteLine($"  chunk crossing, 5 new chunks read in the frame: {Stopwatch.GetElapsedTime(crossing).TotalMilliseconds:F2} ms (no prefetch)");

    var sampler = new MapSampler(registry);
    var mapTimes = new List<double>();
    for (var i = 0; i < 200; i++)
    {
        var begun = Stopwatch.GetTimestamp();
        sampler.Chunk(engine.SurfaceAt, 200 + i % 20, 200 + i / 20);
        mapTimes.Add(Stopwatch.GetElapsedTime(begun).TotalMilliseconds);
    }
    mapTimes.Sort();
    Console.WriteLine($"  minimap, one chunk:        avg {mapTimes.Average():F3} ms, p95 {mapTimes[(int)(mapTimes.Count * 0.95)]:F3} ms; a crossing's 9 new chunks ~ {mapTimes.Average() * 9:F2} ms");
    var window = Stopwatch.GetTimestamp();
    for (var j = 0; j < 9; j++)
    for (var i = 0; i < 9; i++) sampler.Chunk(engine.SurfaceAt, 400 + i, 400 + j);
    Console.WriteLine($"  minimap, first 9x9 window: {Stopwatch.GetElapsedTime(window).TotalMilliseconds:F2} ms");

    // The host's own first-frame clock, windowless, for the bounded and the endless world side by side.
    foreach (var generator in new[] { "test", "infinite" })
    for (var run = 0; run < 3; run++)
    {
        using var host = new EtherBound.Host.SimulationHost(seed: 7, generator: generator);
        host.WaitUntilReady(TimeSpan.FromSeconds(60));
        Console.WriteLine($"  host first frame, {generator,-8} run {run + 1}: {host.FirstFrameMilliseconds:F1} ms (world setup {host.WorldSetupMilliseconds:F1}, snapshot {host.SnapshotBuildMilliseconds:F1})");
    }
    return 0;
}
