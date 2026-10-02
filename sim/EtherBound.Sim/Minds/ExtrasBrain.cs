using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Events;
using EtherBound.Sim.Rng;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Minds;

/// <summary>
/// The deterministic routine brain of an Extra (<c>minds/extras.py</c>). It only proposes: every
/// step goes through <see cref="IEnginePort.Submit"/> and every goal through
/// <see cref="IEnginePort.SetGoal"/>; everything else it asks the engine is a read (the tick state, the
/// grid, a <c>Menu</c>, <c>Percepts</c>). It keeps no state the engine cannot rebuild, so a restart at
/// any tick reproduces the same run. Needs (Dev-011) are ranked by <see cref="NeedsUtility"/>.
/// </summary>
public sealed class ExtrasBrain : ISystem
{
    public const double WalkSpeed = 4.0;
    public const int MaxMovesPerTick = 6;
    public const double WanderChance = 0.6;
    public const int WanderRadiusM = 12;
    public const int WanderTries = 5;
    public const double ArriveMetres = 0.35;
    public const double StallMetres = 0.05;
    public const int MaxExpansions = 3000;

    private sealed class Plan
    {
        public required GoalSpot Goal { get; init; }
        public required List<NavNode> Path { get; init; }
        public required long GridRevision { get; init; }
        public int Index { get; set; }
    }

    private readonly IEnginePort _engine;
    private readonly Dictionary<string, Plan> _plans = new(StringComparer.Ordinal);
    private readonly Nav.SearchWorkspace _pathSearch = new();
    private WorldGrid? _grid;
    private readonly HashSet<string> _retried = new(StringComparer.Ordinal);

    /// <param name="timeScale">Real seconds per game minute; the brain walks 4 m per real second, like Niko.</param>
    public ExtrasBrain(IEnginePort engine, double timeScale = 1.0)
    {
        _engine = engine;
        TimeScale = timeScale;
    }

    public string Name => "minds.extras";
    public double TimeScale { get; }

    public void Attach(EventBus bus) => bus.Subscribe("clock.ticked", _ => OnTick(), Name, Phase.Minds);

    public void OnTick()
    {
        var state = _engine.GetTickState();
        if (!ReferenceEquals(_grid, _engine.Grid))
        {
            _plans.Clear();
            _grid = _engine.Grid;
        }
        if (state.Paused) return;
        var tickSeconds = TimeScale / state.Speed;
        foreach (var actor in state.Actors)
            if (actor.Kind == "extra") TickActor(actor, state, tickSeconds);
    }

    private void TickActor(ActorState actor, WorldState state, double tickSeconds)
    {
        var waiting = actor.Activity is { Op: "wait" };
        if (actor.Activity is not null && !waiting)
        {
            _retried.Remove(actor.Id);
            return;
        }
        if (actor.Mind is not { } mind) return;
        // A needy Extra that is only waiting or wandering re-decides on its scan minute, so a source in
        // reach is not ignored for a quarter of an hour. Anything it is really doing runs to its end.
        if ((waiting && mind.Goal is null || mind.Goal is { Kind: "wander" }) && actor.Needs is { } needy &&
            NeedsUtility.ScanDue(actor.Id, state.GameMinute) && SeekNeeds(actor, state, mind, needy))
            return;
        // Once it has a seek goal the first step submits a move, which ends the wait it was in.
        if (waiting && mind.Goal is not { Kind: NeedsUtility.SeekKind }) return;
        if (mind.Goal is not { } goal)
        {
            Routine(actor, state, mind);
            return;
        }
        if (Arrived(actor, goal))
        {
            _plans.Remove(actor.Id);
            _retried.Remove(actor.Id);
            _engine.SetGoal(actor.Id, null, "arrived");
            // Someone who walked to a source uses it at once, without waiting for a scan minute; a wanderer
            // (or a seeker that finds nothing left) rests a while first.
            var used = goal.Kind == NeedsUtility.SeekKind && actor.Needs is { } hungry &&
                SeekNeeds(actor, state, mind with { Goal = null }, hungry, arrived: true);
            if (!used) _engine.Submit(actor.Id, GameAction.Wait());
            return;
        }
        var plan = PlanFor(actor, goal);
        if (plan is null)
        {
            _plans.Remove(actor.Id);
            _engine.SetGoal(actor.Id, null, "stuck");
            return;
        }
        var (finalX, finalY, _) = Walk(actor, plan, tickSeconds);
        if (PyMath.Hypot(finalX - actor.X, finalY - actor.Y) >= StallMetres)
        {
            _retried.Remove(actor.Id);
            return;
        }
        // A stall is data for the brain, never a retry of the same step in the same tick.
        _plans.Remove(actor.Id);
        if (_retried.Contains(actor.Id) || !Recompute(actor.Id, finalX, finalY, actor.H, goal))
        {
            _retried.Remove(actor.Id);
            _engine.SetGoal(actor.Id, null, "stuck");
        }
        else
        {
            _retried.Add(actor.Id);
        }
    }

    private void Routine(ActorState actor, WorldState state, Mind mind)
    {
        if (actor.Needs is { } needs && SeekNeeds(actor, state, mind, needs)) return;
        var rng = new RngStreams(state.Seed).Stream($"extras:{actor.Id}:{state.GameMinute}");
        if (rng.Random() < WanderChance && ChooseWander(actor, mind, rng) is { } plan)
        {
            _plans[actor.Id] = plan;
            _engine.SetGoal(actor.Id, plan.Goal, "chosen");
            return;
        }
        _engine.Submit(actor.Id, GameAction.Wait());
    }

    /// <summary>
    /// The utility step (Dev-011): when a need wants a source, rank what the engine offers here, what
    /// the perception proxy shows and the Extra's home, and act on the best that works. False leaves the
    /// Extra to its wander roll. The Menu and the wider perception scan are read on the Extra's scan minute, or
    /// on arrival at a goal it chose to walk to.
    /// </summary>
    private bool SeekNeeds(ActorState actor, WorldState state, Mind mind, ActorNeeds needs, bool arrived = false)
    {
        var urgencies = NeedsUtility.Urgencies(needs, state.GameMinute);
        if (urgencies.Count == 0) return false;
        var options = new List<NeedOption>(NeedsUtility.FromRest(actor, mind, urgencies));
        // Reading the Menu costs a session, so a hungry or thirsty Extra does it on its scan minute (one in five)
        // and the moment it arrives somewhere, not on every idle tick; resting needs neither.
        if (urgencies.Keys.Any(need => need != NeedCatalog.Rest) && (arrived || NeedsUtility.ScanDue(actor.Id, state.GameMinute)))
        {
            var menu = _engine.Menu(actor.Id, actor.X, actor.Y, actor.Z, 1);
            var offered = NeedsUtility.FromMenu(menu, urgencies).ToList();
            options.AddRange(offered);
            var unmet = urgencies.Keys.Any(need => need != NeedCatalog.Rest && offered.All(o => o.Need != need));
            if (unmet)
                options.AddRange(NeedsUtility.FromPercepts(actor, _engine.Percepts(actor.Id, NeedsUtility.PerceptionRadiusM), urgencies));
        }
        if (options.Count == 0) return false;
        var rng = new RngStreams(state.Seed).Stream($"needs:{actor.Id}:{state.GameMinute}");
        foreach (var option in NeedsUtility.Rank(options, rng))
        {
            if (option.Action is { } action)
            {
                if (!_engine.Submit(actor.Id, action).Accepted) continue;
                // It stopped wandering to do this; the old goal would otherwise resume afterwards.
                if (mind.Goal is not null)
                {
                    _plans.Remove(actor.Id);
                    _engine.SetGoal(actor.Id, null, "interrupted");
                }
                return true;
            }
            var goal = option.Goal!;
            if (!_engine.Grid.StandingSurfaces(goal.X, goal.Y).Any(s => s.H == goal.H)) continue;
            if (!Recompute(actor.Id, actor.X, actor.Y, actor.H, goal)) continue;
            _engine.SetGoal(actor.Id, goal, "chosen");
            return true;
        }
        return false;
    }

    private Plan? ChooseWander(ActorState actor, Mind mind, PyRandom rng)
    {
        var start = new Spot(PyMath.Floor(actor.X), PyMath.Floor(actor.Y), actor.H);
        var grid = _engine.Grid;
        for (var i = 0; i < WanderTries; i++)
        {
            var dx = rng.RandInt(-WanderRadiusM, WanderRadiusM);
            var dy = rng.RandInt(-WanderRadiusM, WanderRadiusM);
            if (PyMath.Hypot(dx, dy) > WanderRadiusM || (dx == 0 && dy == 0)) continue;
            int x = mind.Anchor.X + dx, y = mind.Anchor.Y + dy;
            if ((x, y) == (start.X, start.Y)) continue;
            var surfaces = _engine.Grid.StandingSurfaces(x, y);
            if (surfaces.Count == 0) continue;
            var surface = surfaces.OrderBy(s => Math.Abs(s.H - mind.Anchor.H)).ThenBy(s => s.H).First();
            var goal = new GoalSpot(x, y, surface.H);
            var startRegion = grid.RegionAt(start.X, start.Y, start.H, actor.X, actor.Y);
            if (Nav.FindPathNodes(grid, start, new Spot(x, y, surface.H), startRegion, MaxExpansions, _pathSearch) is { } path)
                return new Plan { Goal = goal, Path = path, GridRevision = grid.NavigationRevision };
        }
        return null;
    }

    private Plan? PlanFor(ActorState actor, GoalSpot goal)
    {
        var grid = _engine.Grid;
        if (_plans.TryGetValue(actor.Id, out var plan) && plan.Goal == goal && plan.GridRevision == grid.NavigationRevision && Locate(plan, actor, grid)) return plan;
        return Recompute(actor.Id, actor.X, actor.Y, actor.H, goal) ? _plans[actor.Id] : null;
    }

    private bool Recompute(string actorId, double x, double y, int h, GoalSpot goal)
    {
        var grid = _engine.Grid;
        var start = new Spot(PyMath.Floor(x), PyMath.Floor(y), h);
        var startRegion = grid.RegionAt(start.X, start.Y, h, x, y);
        var path = Nav.FindPathNodes(grid, start, new Spot(goal.X, goal.Y, goal.H), startRegion, MaxExpansions, _pathSearch);
        if (path is null) return false;
        _plans[actorId] = new Plan { Goal = goal, Path = path, GridRevision = grid.NavigationRevision };
        return true;
    }

    /// <summary>Point the plan at the actor's tile; false when the actor is off the cached path.</summary>
    private static bool Locate(Plan plan, ActorState actor, WorldGrid grid)
    {
        var here = new Spot(PyMath.Floor(actor.X), PyMath.Floor(actor.Y), actor.H);
        var region = grid.RegionAt(here.X, here.Y, here.H, actor.X, actor.Y);
        var index = plan.Path.FindIndex(node => node.Spot == here && node.Region == region);
        if (index < 0) return false;
        plan.Index = index;
        return true;
    }

    private static (double X, double Y) Waypoint(WorldGrid grid, IReadOnlyList<NavNode> path, int index)
    {
        var node = path[index];
        var spot = node.Spot;
        var mask = grid.InteriorWallMaskAt(spot.X, spot.Y, spot.H);
        var point = WallRegions.Waypoint(mask, node.Region);
        if (index >= path.Count - 1) return point;

        var next = path[index + 1];
        int dx = next.Spot.X - spot.X, dy = next.Spot.Y - spot.Y;
        if (Math.Abs(dx) + Math.Abs(dy) != 1) return point;
        var nextMask = grid.InteriorWallMaskAt(next.Spot.X, next.Spot.Y, next.Spot.H);
        var ports = WallRegions.SharedPorts(mask, node.Region, nextMask, next.Region, dx, dy);
        if (ports == 0) return point;
        if (dx != 0 && ((mask | nextMask) & ChunkConst.SlotHalfH) != 0)
            point.Y = (ports & WallRegions.FirstPort) != 0
                ? 0.5 - WallRegions.BodyClearance
                : 0.5 + WallRegions.BodyClearance;
        if (dy != 0 && ((mask | nextMask) & ChunkConst.SlotHalfV) != 0)
            point.X = (ports & WallRegions.FirstPort) != 0
                ? 0.5 - WallRegions.BodyClearance
                : 0.5 + WallRegions.BodyClearance;
        return point;
    }

    private (double X, double Y, int H) Walk(ActorState actor, Plan plan, double tickSeconds)
    {
        double x = actor.X, y = actor.Y;
        var h = actor.H;
        var budget = WalkSpeed * tickSeconds;
        for (var i = 0; i < MaxMovesPerTick; i++)
        {
            if (budget <= 0) break;
            Advance(plan, x, y, h);
            if (plan.Index >= plan.Path.Count) break;
            var target = plan.Path[plan.Index];
            var spot = target.Spot;
            var local = Waypoint(_engine.Grid, plan.Path, plan.Index);
            double dx = spot.X + local.X - x, dy = spot.Y + local.Y - y;
            var distance = PyMath.Hypot(dx, dy);
            double stepTime, ux, uy;
            if (distance < 1e-6)
            {
                if (h == spot.H)
                {
                    plan.Index += 1;
                    continue;
                }
                stepTime = budget;
                ux = uy = 0.0;
            }
            else
            {
                stepTime = Math.Min(budget, distance / WalkSpeed);
                (ux, uy) = (dx / distance, dy / distance);
            }
            var result = _engine.Submit(actor.Id, GameAction.Move(ux, uy), stepTime);
            budget -= stepTime;
            if (!result.Accepted) break;
            (x, y, h) = (result.X, result.Y, result.H);
        }
        return (x, y, h);
    }

    // Retain the region even when several path nodes share the same tile at different heights.
    private void Advance(Plan plan, double x, double y, int h)
    {
        while (plan.Index < plan.Path.Count - 1)
        {
            var node = plan.Path[plan.Index];
            var spot = node.Spot;
            if ((PyMath.Floor(x), PyMath.Floor(y), h) == (spot.X, spot.Y, spot.H) &&
                _engine.Grid.RegionAt(spot.X, spot.Y, h, x, y) == node.Region) plan.Index += 1;
            else return;
        }
    }

    private bool Arrived(ActorState actor, GoalSpot goal) => actor.H == goal.H &&
        (_engine.Grid.InteriorWallMaskAt(goal.X, goal.Y, goal.H) != 0
            ? (PyMath.Floor(actor.X), PyMath.Floor(actor.Y)) == (goal.X, goal.Y)
            : PyMath.Hypot(actor.X - (goal.X + 0.5), actor.Y - (goal.Y + 0.5)) <= ArriveMetres);
}
