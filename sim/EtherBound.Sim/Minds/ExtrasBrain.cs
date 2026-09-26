using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Events;
using EtherBound.Sim.Rng;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Minds;

/// <summary>
/// The deterministic routine brain of an Extra (<c>minds/extras.py</c>). It only proposes: every
/// step goes through <see cref="IEnginePort.Submit"/> and every goal through
/// <see cref="IEnginePort.SetGoal"/>. It keeps no state the engine cannot rebuild, so a restart at
/// any tick reproduces the same run.
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
        public required List<Spot> Path { get; init; }
        public int Index { get; set; }
    }

    private readonly IEnginePort _engine;
    private readonly Dictionary<string, Plan> _plans = new(StringComparer.Ordinal);
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
        var state = _engine.GetState();
        if (state.Paused) return;
        var tickSeconds = TimeScale / state.Speed;
        foreach (var actor in state.Actors)
            if (actor.Kind == "extra") TickActor(actor, state, tickSeconds);
    }

    private void TickActor(ActorState actor, WorldState state, double tickSeconds)
    {
        if (actor.Activity is not null)
        {
            _retried.Remove(actor.Id);
            return;
        }
        if (actor.Mind is not { } mind) return;
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
            _engine.Submit(actor.Id, GameAction.Wait());
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
        var rng = new RngStreams(state.Seed).Stream($"extras:{actor.Id}:{state.GameMinute}");
        if (rng.Random() < WanderChance && ChooseWander(actor, mind, rng) is { } goal)
        {
            _engine.SetGoal(actor.Id, goal, "chosen");
            return;
        }
        _engine.Submit(actor.Id, GameAction.Wait());
    }

    private GoalSpot? ChooseWander(ActorState actor, Mind mind, PyRandom rng)
    {
        var start = new Spot(PyMath.Floor(actor.X), PyMath.Floor(actor.Y), actor.H);
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
            if (Nav.FindPath(_engine.Grid, start, new Spot(x, y, surface.H), MaxExpansions) is not null) return new GoalSpot(x, y, surface.H);
        }
        return null;
    }

    private Plan? PlanFor(ActorState actor, GoalSpot goal)
    {
        if (_plans.TryGetValue(actor.Id, out var plan) && plan.Goal == goal && Locate(plan, actor)) return plan;
        return Recompute(actor.Id, actor.X, actor.Y, actor.H, goal) ? _plans[actor.Id] : null;
    }

    private bool Recompute(string actorId, double x, double y, int h, GoalSpot goal)
    {
        var path = Nav.FindPath(_engine.Grid, new Spot(PyMath.Floor(x), PyMath.Floor(y), h), new Spot(goal.X, goal.Y, goal.H), MaxExpansions);
        if (path is null) return false;
        _plans[actorId] = new Plan { Goal = goal, Path = path };
        return true;
    }

    /// <summary>Point the plan at the actor's tile; false when the actor is off the cached path.</summary>
    private static bool Locate(Plan plan, ActorState actor)
    {
        var here = new Spot(PyMath.Floor(actor.X), PyMath.Floor(actor.Y), actor.H);
        var index = plan.Path.IndexOf(here);
        if (index < 0) return false;
        plan.Index = index;
        return true;
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
            double dx = target.X + 0.5 - x, dy = target.Y + 0.5 - y;
            var distance = PyMath.Hypot(dx, dy);
            double stepTime, ux, uy;
            if (distance < 1e-6)
            {
                if (h == target.H)
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

    // Keep the last waypoint as the target so a body lands on the goal centre, not its edge.
    private static void Advance(Plan plan, double x, double y, int h)
    {
        while (plan.Index < plan.Path.Count - 1)
        {
            var spot = plan.Path[plan.Index];
            if ((PyMath.Floor(x), PyMath.Floor(y), h) == (spot.X, spot.Y, spot.H)) plan.Index += 1;
            else return;
        }
    }

    private static bool Arrived(ActorState actor, GoalSpot goal) =>
        actor.H == goal.H && PyMath.Hypot(actor.X - (goal.X + 0.5), actor.Y - (goal.Y + 0.5)) <= ArriveMetres;
}
