using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Minds;
using EtherBound.Sim.Rng;
using EtherBound.Sim.World;
using static EtherBound.Sim.Tests.NeedScenes;

namespace EtherBound.Sim.Tests;

/// <summary>
/// The Extras' utility step: the pure scoring of options, then the whole loop on a real engine (an Extra
/// that is hungry, thirsty or tired finds a source, walks to it and uses it through the action API).
/// </summary>
public sealed class NeedsUtilityRules
{
    private const string Extra = "extra-001";
    private const int Noon = 12 * 60;
    private const int Night = 23 * 60;

    private static ActorState Standing(double x, double y, int h, ActorNeeds? needs = null) =>
        new("extra-001", "extra", x, y, PyMath.FloorDiv(h, 6), h, null, Array.Empty<CarriedObject>(), 0.0, null, null, needs);

    private static IReadOnlyDictionary<string, double> Hungry(double level = 0.2) =>
        NeedsUtility.Urgencies(ActorNeeds.Full(Noon).With(NeedCatalog.Hunger, level, Noon), Noon);

    // --- pure scoring -------------------------------------------------------------------------

    [Fact]
    public void A_content_actor_has_no_urgency_and_a_hungry_one_has_only_hunger()
    {
        Assert.Empty(NeedsUtility.Urgencies(ActorNeeds.Full(Noon), Noon));
        var urgencies = Hungry();
        Assert.Equal(new[] { NeedCatalog.Hunger }, urgencies.Keys);
        Assert.Equal(0.6, urgencies[NeedCatalog.Hunger], 9);
    }

    [Fact]
    public void Each_actor_scans_on_one_minute_in_five_and_always_the_same_one()
    {
        foreach (var id in new[] { "extra-001", "extra-002", "extra-003", "extra-006" })
        {
            var minutes = Enumerable.Range(0, 10).Where(m => NeedsUtility.ScanDue(id, m)).ToList();
            Assert.Equal(2, minutes.Count);
            Assert.Equal(NeedsUtility.ScanEveryMinutes, minutes[1] - minutes[0]);
            Assert.Equal(minutes, Enumerable.Range(0, 10).Where(m => NeedsUtility.ScanDue(id, m)).ToList());
        }
        Assert.Contains(Enumerable.Range(0, 6).Select(i => $"extra-00{i + 1}"),
            id => NeedsUtility.ScanDue(id, 0) != NeedsUtility.ScanDue("extra-001", 0));
    }

    [Fact]
    public void A_percept_scores_by_urgency_minus_distance_and_the_tile_you_stand_on_is_skipped()
    {
        var actor = Standing(10.5, 10.5, 2);
        var near = new Percept(NeedCatalog.Hunger, 1, 13, 10, 2, 12, 10, 2);
        var far = new Percept(NeedCatalog.Hunger, 2, 25, 10, 2, 24, 10, 2);
        var here = new Percept(NeedCatalog.Hunger, 3, 10, 10, 2, 10, 10, 2);
        var water = new Percept(NeedCatalog.Thirst, null, 14, 10, 0, 13, 10, 2);
        var options = NeedsUtility.FromPercepts(actor, new[] { here, far, near, water }, Hungry()).ToList();

        Assert.Equal(2, options.Count);
        var nearOption = options.Single(o => o.Goal!.X == 12);
        Assert.Equal(0.6 - 2.0 / NeedsUtility.DistanceCost, nearOption.Score, 9);
        Assert.Equal(new GoalSpot(12, 10, 2, NeedsUtility.SeekKind), nearOption.Goal);
        Assert.Null(nearOption.Action);
        Assert.True(nearOption.Score > options.Single(o => o.Goal!.X == 24).Score);
    }

    [Fact]
    public void Rest_is_a_sleep_at_home_and_a_walk_home_anywhere_else()
    {
        var mind = new Mind(new TilePos(10, 10, 2));
        var tired = NeedsUtility.Urgencies(ActorNeeds.Full(Night).With(NeedCatalog.Rest, 0.1, Night), Night);
        var home = NeedsUtility.FromRest(Standing(10.5, 10.5, 2), mind, tired).Single();
        Assert.Equal("sleep", home.Action!.Op);
        Assert.Null(home.Goal);

        var away = NeedsUtility.FromRest(Standing(20.5, 10.5, 2), mind, tired).Single();
        Assert.Null(away.Action);
        Assert.Equal(new GoalSpot(10, 10, 2, NeedsUtility.SeekKind), away.Goal);
        Assert.True(away.Score < home.Score);
        Assert.Empty(NeedsUtility.FromRest(Standing(10.5, 10.5, 2), mind, Hungry()));
    }

    [Fact]
    public void Ranking_puts_the_best_first_and_settles_a_toss_up_with_the_seeded_stream()
    {
        NeedOption Option(double score) => new(NeedCatalog.Hunger, score, null, new GoalSpot((int)(score * 100), 0, 0));
        var clear = NeedsUtility.Rank(new[] { Option(0.2), Option(0.9), Option(0.5) }, new RngStreams(1).Stream("t"));
        Assert.Equal(new[] { 0.9, 0.5, 0.2 }, clear.Select(o => o.Score));

        var tied = new[] { Option(0.50), Option(0.52), Option(0.30) };
        var firsts = Enumerable.Range(0, 40).Select(seed => NeedsUtility.Rank(tied, new RngStreams(seed).Stream("t"))[0].Score).ToHashSet();
        Assert.Equal(new HashSet<double> { 0.50, 0.52 }, firsts);
        Assert.Equal(NeedsUtility.Rank(tied, new RngStreams(5).Stream("t"))[0], NeedsUtility.Rank(tied, new RngStreams(5).Stream("t"))[0]);
        Assert.Single(NeedsUtility.Rank(new[] { Option(0.4) }, new RngStreams(1).Stream("t")));
        Assert.Empty(NeedsUtility.Rank(Array.Empty<NeedOption>(), new RngStreams(1).Stream("t")));
    }

    // --- the loop on a real engine --------------------------------------------------------------

    private static ExtrasBrain Attach(WorldEngine engine)
    {
        var brain = new ExtrasBrain(engine);
        brain.Attach(engine.Bus);
        return brain;
    }

    private static void Advance(WorldEngine engine, int minutes)
    {
        for (var i = 0; i < minutes; i++) engine.AdvanceTime();
    }

    private static bool Ran(WorldEngine engine, int minutes, Func<bool> done)
    {
        for (var i = 0; i < minutes; i++)
        {
            engine.AdvanceTime();
            if (done()) return true;
        }
        return false;
    }

    /// <summary>A standing tile a few metres from a mind's anchor with a walking path home, or null.</summary>
    private static (int X, int Y)? Away(WorldEngine engine, Mind mind, int min, int max)
    {
        var home = new Spot(mind.Anchor.X, mind.Anchor.Y, mind.Anchor.H);
        for (var r = min; r <= max; r++)
        foreach (var (dx, dy) in new[] { (r, 0), (-r, 0), (0, r), (0, -r) })
        {
            int x = mind.Anchor.X + dx, y = mind.Anchor.Y + dy;
            var surfaces = engine.Grid.StandingSurfaces(x, y);
            if (surfaces.Count == 0) continue;
            if (Nav.FindPath(engine.Grid, new Spot(x, y, surfaces[0].H), home, 3000) is not null) return (x, y);
        }
        return null;
    }

    private static bool Consumed(WorldEngine engine, string actorId, string op) =>
        LoggedEvents(engine, "actor.consumed", actorId).Any(e => e.Data.Str("op") == op);

    [Fact]
    public void A_hungry_extra_walks_to_food_it_can_perceive_and_eats_it()
    {
        using var engine = NewEngine();
        Attach(engine);
        RemoveAll(engine, "apple");
        var mind = Actor(engine, Extra).Mind!;
        var spot = Away(engine, mind, 5, 9) ?? throw new InvalidOperationException("no tile to put the apple on");
        PlaceOnTile(engine, "apple", spot.X, spot.Y, 2);
        SetNeeds(engine, Extra, hunger: 0.2);

        Assert.True(Ran(engine, 60, () => Consumed(engine, Extra, "eat")), "the Extra never ate");
        var goals = LoggedEvents(engine, "actor.goal_set", Extra).Where(e => e.Data["goal"] is not null).ToList();
        Assert.Contains(goals, e => e.Data["goal"]!.Str("kind") == NeedsUtility.SeekKind);
        Assert.True(Level(engine, Extra, NeedCatalog.Hunger) > 0.35);
        Assert.Equal(1, engine.OpenSession().Objects().Single(o => o.Kind == "apple").Quantity);
        foreach (var other in new[] { "extra-002", "extra-003", "extra-004", "extra-005", "extra-006" })
            Assert.Empty(LoggedEvents(engine, "actor.consumed", other));
    }

    [Fact]
    public void An_extra_eats_what_it_carries_without_walking_anywhere()
    {
        using var engine = NewEngine();
        Attach(engine);
        RemoveAll(engine, "apple");
        var session = engine.OpenSession();
        WorldSetup.GiveFoodKit(session, Extra, 3);
        session.Commit();
        engine.Reindex();
        SetNeeds(engine, Extra, hunger: 0.2);

        Assert.True(Ran(engine, 20, () => Consumed(engine, Extra, "eat")), "the Extra never ate");
        Assert.DoesNotContain(LoggedEvents(engine, "actor.goal_set", Extra), e => e.Data["goal"] is { } goal && goal.Str("kind") == NeedsUtility.SeekKind);
    }

    [Fact]
    public void With_no_source_in_reach_a_hungry_extra_keeps_wandering_and_writes_nothing_extra()
    {
        using var engine = NewEngine();
        Attach(engine);
        RemoveAll(engine, "apple");
        SetNeeds(engine, Extra, hunger: 0.1);
        Advance(engine, 90);

        Assert.Empty(LoggedEvents(engine, "actor.consumed", Extra));
        Assert.DoesNotContain(LoggedEvents(engine, "actor.goal_set", Extra), e => e.Data["goal"] is { } goal && goal.Str("kind") == NeedsUtility.SeekKind);
        var wandered = LoggedEvents(engine, "actor.goal_set", Extra).Where(e => e.Data["goal"] is not null).ToList();
        Assert.NotEmpty(wandered);
        Assert.NotEmpty(LoggedEvents(engine, "actor.moved", Extra));
    }

    [Fact]
    public void A_thirsty_extra_walks_to_the_pond_and_drinks_from_the_bank()
    {
        using var engine = NewEngine();
        Attach(engine);
        var (wx, _) = ShallowWater(engine);
        Teleport(engine, Extra, wx + 10, 70);
        SetNeeds(engine, Extra, thirst: 0.15);

        Assert.True(Ran(engine, 80, () => Consumed(engine, Extra, "drink")), "the Extra never drank");
        var drank = LoggedEvents(engine, "actor.consumed", Extra).First(e => e.Data.Str("op") == "drink");
        Assert.Equal("water_shallow", drank.Data.Str("kind"));
        Assert.True(Level(engine, Extra, NeedCatalog.Thirst) > 0.3);
    }

    [Fact]
    public void At_night_a_tired_extra_walks_home_and_sleeps_there_undisturbed()
    {
        using var engine = NewEngine();
        Attach(engine);
        SetMinute(engine, 23 * 60);
        var mind = Actor(engine, Extra).Mind!;
        var spot = Away(engine, mind, 4, 8) ?? throw new InvalidOperationException("no tile away from home");
        Teleport(engine, Extra, spot.X, spot.Y);
        SetNeeds(engine, Extra, rest: 0.3);

        Assert.True(Ran(engine, 40, () => LoggedEvents(engine, "activity.started", Extra).Any(e => e.Data.Str("op") == "sleep")), "the Extra never slept");
        var home = Actor(engine, Extra);
        Assert.Equal((mind.Anchor.X, mind.Anchor.Y, mind.Anchor.H), (PyMath.Floor(home.X), PyMath.Floor(home.Y), home.H));
        Assert.Contains(LoggedEvents(engine, "actor.goal_set", Extra), e => e.Data["goal"] is { } goal && goal.Str("kind") == NeedsUtility.SeekKind);

        var moves = LoggedEvents(engine, "actor.moved", Extra).Count;
        Advance(engine, 60);
        Assert.Equal(moves, LoggedEvents(engine, "actor.moved", Extra).Count);
        Assert.Equal("sleep", Actor(engine, Extra).Activity!.Op);
    }

    [Fact]
    public void By_day_rest_is_ignored_until_it_is_urgent()
    {
        using var engine = NewEngine();
        Attach(engine);
        SetMinute(engine, 10 * 60);
        SetNeeds(engine, Extra, rest: 0.4);
        Advance(engine, 40);
        Assert.DoesNotContain(LoggedEvents(engine, "activity.started", Extra), e => e.Data.Str("op") == "sleep");

        SetNeeds(engine, Extra, rest: 0.1);
        Assert.True(Ran(engine, 60, () => LoggedEvents(engine, "activity.started", Extra).Any(e => e.Data.Str("op") == "sleep")),
            "an exhausted Extra should sleep even by day");
    }

    [Fact]
    public void A_content_extra_never_leaves_its_wander_routine()
    {
        using var engine = NewEngine();
        Attach(engine);
        Advance(engine, 100);
        Assert.Empty(engine.ReadEvents(0, 1_000_000, "actor.consumed"));
        Assert.Empty(engine.ReadEvents(0, 1_000_000, "actor.slept"));
        Assert.DoesNotContain(engine.ReadEvents(0, 1_000_000, "actor.goal_set"), e => e.Data["goal"] is { } goal && goal.Str("kind") == NeedsUtility.SeekKind);
    }
}
