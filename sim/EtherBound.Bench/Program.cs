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
const int MeasuredTicks = 15;
int[] populations = { 1_000, 5_000, 10_000 };

Console.WriteLine($"EtherBound.Bench — {Environment.ProcessorCount} logical cores, .NET {Environment.Version}");
Console.WriteLine($"{"Extras",8} {"tick avg ms",12} {"tick p95 ms",12} {"move avg ms",12}");

foreach (var count in populations)
{
    using var engine = new WorldEngine();
    engine.NewGame(7, "lab");
    SeedExtras(engine, count);
    new ExtrasBrain(engine).Attach(engine.Bus);
    engine.SetClock(speed: 10);

    for (var i = 0; i < WarmupTicks; i++) engine.AdvanceTime();

    var tickTimes = new List<double>(MeasuredTicks);
    var watch = new Stopwatch();
    for (var i = 0; i < MeasuredTicks; i++)
    {
        watch.Restart();
        engine.AdvanceTime();
        watch.Stop();
        tickTimes.Add(watch.Elapsed.TotalMilliseconds);
    }
    tickTimes.Sort();
    var avg = tickTimes.Average();
    var p95 = tickTimes[(int)(tickTimes.Count * 0.95)];

    var moveWatch = Stopwatch.StartNew();
    var result = engine.Submit(Ids.Player, GameAction.Move(1, 0), 0.05);
    moveWatch.Stop();
    Console.WriteLine($"{count,8} {avg,12:F3} {p95,12:F3} {moveWatch.Elapsed.TotalMilliseconds,12:F3}");
    if (!result.Accepted) Console.WriteLine("  (move step was rejected; the bench spot may be blocked)");
}

return 0;

// The lab's open bay gives room for a synthetic crowd without touching the population algorithm's
// spacing search, which is not built for thousands of Extras.
static void SeedExtras(WorldEngine engine, int count)
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
        });
        placed++;
    }
    session.Commit();
    engine.Reindex();
    Console.WriteLine($"  seeded {placed}/{count} Extras in {attempts} attempts");
}
