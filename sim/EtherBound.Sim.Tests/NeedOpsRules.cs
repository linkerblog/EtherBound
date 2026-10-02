using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Engine.Ops;
using EtherBound.Sim.World;
using static EtherBound.Sim.Tests.NeedScenes;

namespace EtherBound.Sim.Tests;

/// <summary>
/// <c>eat</c>, <c>drink</c> and <c>sleep</c> on the shared action API: what they cost in minutes, what they
/// change, when they refuse, and that Niko, who has no needs row, uses the same handlers.
/// </summary>
public sealed class NeedOpsRules
{
    private const string Extra = "extra-001";

    private static (int X, int Y) TileOf(WorldEngine engine, string id)
    {
        var actor = Actor(engine, id);
        return (PyMath.Floor(actor.X), PyMath.Floor(actor.Y));
    }

    private static void Advance(WorldEngine engine, int minutes)
    {
        for (var i = 0; i < minutes; i++) engine.AdvanceTime();
    }

    [Fact]
    public void Eating_takes_three_minutes_consumes_one_unit_and_raises_hunger()
    {
        using var engine = NewEngine();
        RemoveAll(engine, "apple");
        SetNeeds(engine, Extra, hunger: 0.3);
        var (x, y) = TileOf(engine, Extra);
        var apples = PlaceOnTile(engine, "apple", x, y, 3);

        var result = engine.Submit(Extra, GameAction.On("eat", new ObjectTarget(apples)));
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(EatOp.Minutes, result.Activity!.EndsMinute - result.Activity.StartedMinute);
        Assert.Equal(3, engine.OpenSession().GetObject(apples)!.Quantity);

        Advance(engine, EatOp.Minutes);
        Assert.Equal(2, engine.OpenSession().GetObject(apples)!.Quantity);
        var expected = 0.3 - 3 * Spec(NeedCatalog.Hunger).RatePerMinute + 0.2;
        Assert.Equal(expected, Level(engine, Extra, NeedCatalog.Hunger), 9);
        var eaten = Assert.Single(LoggedEvents(engine, "actor.consumed", Extra));
        Assert.Equal(("eat", "apple", 1, "hunger"), (eaten.Data.Str("op"), eaten.Data.Str("kind"), eaten.Data.Int("quantity"), eaten.Data.Str("need")));
        Assert.Equal(expected, eaten.Data.Num("level"), 9);
        Assert.Contains(LoggedEvents(engine, "activity.finished", Extra), e => e.Data.Str("outcome") == "completed");
    }

    private static NeedSpec Spec(string key) => NeedCatalog.Default[key];

    [Fact]
    public void The_last_unit_removes_the_row_and_hunger_never_passes_full()
    {
        using var engine = NewEngine();
        RemoveAll(engine, "apple");
        SetNeeds(engine, Extra, hunger: 0.85);
        var (x, y) = TileOf(engine, Extra);
        var apple = PlaceOnTile(engine, "apple", x, y);
        Assert.True(engine.Submit(Extra, GameAction.On("eat", new ObjectTarget(apple))).Accepted);
        Advance(engine, EatOp.Minutes);
        Assert.Null(engine.OpenSession().GetObject(apple));
        Assert.InRange(Level(engine, Extra, NeedCatalog.Hunger), 0.99, 1.0);
        // The chunk's object index forgot it too, so the frame and the menus stop showing an apple.
        Assert.DoesNotContain(engine.Grid.ObjectsAt(x, y), o => o.Id == apple);
    }

    [Fact]
    public void Eating_from_a_worn_pack_lightens_the_load_when_the_activity_completes()
    {
        using var engine = NewEngine();
        RemoveAll(engine, "apple");
        SetNeeds(engine, Extra, hunger: 0.2);
        var session = engine.OpenSession();
        WorldSetup.GiveFoodKit(session, Extra, 2);
        session.Commit();
        engine.Reindex();
        var apple = engine.OpenSession().Objects().Single(o => o.Kind == "apple" && o.Loc == "in").Id;
        var before = Actor(engine, Extra).LoadKg;

        var (x, y) = TileOf(engine, Extra);
        var menu = engine.Menu(Extra, x + 0.5, y + 0.5, Actor(engine, Extra).Z);
        Assert.Contains(menu.Entries, e => e is { Op: "eat", Available: true } && e.Subject == "Apple ×2");
        Assert.True(engine.Submit(Extra, GameAction.On("eat", new ObjectTarget(apple))).Accepted);
        Advance(engine, EatOp.Minutes);

        Assert.Equal(1, engine.OpenSession().GetObject(apple)!.Quantity);
        Assert.Equal(before - 0.2, Actor(engine, Extra).LoadKg, 9);
    }

    [Fact]
    public void Eating_refuses_what_is_not_food_what_is_far_what_is_shut_in_and_a_full_stomach()
    {
        using var engine = NewEngine();
        RemoveAll(engine, "apple");
        var (x, y) = TileOf(engine, Extra);
        SetNeeds(engine, Extra, hunger: 0.2);
        var bottle = PlaceOnTile(engine, "bottle", x, y);
        Assert.Equal("not food", engine.Submit(Extra, GameAction.On("eat", new ObjectTarget(bottle))).Reason);

        var far = PlaceOnTile(engine, "apple", x + 6, y);
        Assert.Equal("out of reach", engine.Submit(Extra, GameAction.On("eat", new ObjectTarget(far))).Reason);

        var near = PlaceOnTile(engine, "apple", x, y);
        SetNeeds(engine, Extra, hunger: 0.95);
        Assert.Equal("not hungry", engine.Submit(Extra, GameAction.On("eat", new ObjectTarget(near))).Reason);
        SetNeeds(engine, Extra, hunger: 0.2);
        Assert.True(engine.Submit(Extra, GameAction.On("eat", new ObjectTarget(near))).Accepted);
    }

    [Fact]
    public void Food_in_a_closed_chest_cannot_be_eaten_until_it_is_opened()
    {
        using var engine = NewEngine();
        var session = engine.OpenSession();
        var chest = session.Objects().First(o => o.Kind == "chest" && !o.IsOpen);
        var apple = session.Objects().First(o => o.Kind == "apple" && o.ContainerId == chest.Id).Id;
        SetNeeds(engine, Extra, hunger: 0.2);
        Teleport(engine, Extra, chest.X!.Value, chest.Y!.Value - 1);
        Assert.Equal("closed", engine.Submit(Extra, GameAction.On("eat", new ObjectTarget(apple))).Reason);
        Assert.True(engine.Submit(Extra, GameAction.On("open", new ObjectTarget(chest.Id))).Accepted);
        Assert.True(engine.Submit(Extra, GameAction.On("eat", new ObjectTarget(apple))).Accepted);
    }

    [Fact]
    public void Drinking_from_shallow_water_takes_two_minutes_and_raises_thirst()
    {
        using var engine = NewEngine();
        var (wx, wy) = ShallowWater(engine);
        var h = engine.Grid.GroundAt(wx, wy)!.Value.GroundH;
        Teleport(engine, Extra, wx, wy);
        SetNeeds(engine, Extra, thirst: 0.2);

        var menu = engine.Menu(Extra, wx + 0.5, wy + 0.5, Actor(engine, Extra).Z);
        Assert.Contains(menu.Entries, e => e is { Op: "drink", Available: true } && e.Subject == "Shallow water");
        var result = engine.Submit(Extra, GameAction.On("drink", new TileTarget(wx, wy, h)));
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(DrinkOp.Minutes, result.Activity!.EndsMinute - result.Activity.StartedMinute);
        Advance(engine, DrinkOp.Minutes);

        Assert.Equal(0.2 - 2 * Spec(NeedCatalog.Thirst).RatePerMinute + 0.5, Level(engine, Extra, NeedCatalog.Thirst), 9);
        var drunk = Assert.Single(LoggedEvents(engine, "actor.consumed", Extra));
        Assert.Equal(("drink", "water_shallow", "thirst"), (drunk.Data.Str("op"), drunk.Data.Str("kind"), drunk.Data.Str("need")));
    }

    [Fact]
    public void Drinking_refuses_deep_water_dry_ground_distance_and_a_full_stomach()
    {
        using var engine = NewEngine();
        var (wx, wy) = ShallowWater(engine);
        var shallowH = engine.Grid.GroundAt(wx, wy)!.Value.GroundH;
        SetNeeds(engine, Extra, thirst: 0.2);

        // At spawn the pond is far away.
        Assert.Equal("out of reach", engine.Submit(Extra, GameAction.On("drink", new TileTarget(wx, wy, shallowH))).Reason);
        var deepH = engine.Grid.GroundAt(196, 70)!.Value.GroundH;
        Assert.Equal("nothing to drink", engine.Submit(Extra, GameAction.On("drink", new TileTarget(196, 70, deepH))).Reason);
        var (x, y) = TileOf(engine, Extra);
        var grassH = engine.Grid.GroundAt(x, y)!.Value.GroundH;
        Assert.Equal("nothing to drink", engine.Submit(Extra, GameAction.On("drink", new TileTarget(x, y, grassH))).Reason);

        Teleport(engine, Extra, wx, wy);
        SetNeeds(engine, Extra, thirst: 0.95);
        Assert.Equal("not thirsty", engine.Submit(Extra, GameAction.On("drink", new TileTarget(wx, wy, shallowH))).Reason);
    }

    [Fact]
    public void Sleep_lasts_as_long_as_the_deficit_restores_rest_and_logs_it()
    {
        using var engine = NewEngine();
        SetNeeds(engine, Extra, rest: 0.25);
        var result = engine.Submit(Extra, GameAction.On("sleep", new SelfTarget()));
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal((int)Math.Ceiling(0.75 * SleepOp.FullMinutes), result.Activity!.EndsMinute - result.Activity.StartedMinute);
        Advance(engine, result.Activity.EndsMinute - result.Activity.StartedMinute);

        Assert.Equal(1.0, Level(engine, Extra, NeedCatalog.Rest), 9);
        var slept = Assert.Single(LoggedEvents(engine, "actor.slept", Extra));
        Assert.Equal(1.0, slept.Data.Num("level"), 9);
        Assert.Contains(LoggedEvents(engine, "activity.finished", Extra), e => e.Data.Str("op") == "sleep" && e.Data.Str("outcome") == "completed");
        Assert.Equal("not tired", engine.Submit(Extra, GameAction.On("sleep", new SelfTarget())).Reason);
    }

    [Fact]
    public void An_interrupted_sleep_recovers_nothing()
    {
        using var engine = NewEngine();
        SetNeeds(engine, Extra, rest: 0.25);
        Assert.True(engine.Submit(Extra, GameAction.On("sleep", new SelfTarget())).Accepted);
        Advance(engine, 100);
        Assert.True(engine.Submit(Extra, GameAction.Wait()).Accepted);
        Advance(engine, 30);
        Assert.Empty(LoggedEvents(engine, "actor.slept", Extra));
        Assert.True(Level(engine, Extra, NeedCatalog.Rest) < 0.25);
    }

    [Fact]
    public void Niko_uses_the_same_ops_and_keeps_no_needs_row()
    {
        using var engine = NewEngine();
        RemoveAll(engine, "apple");
        var (x, y) = TileOf(engine, Ids.Player);
        var apple = PlaceOnTile(engine, "apple", x, y, 2);
        Assert.Null(Actor(engine, Ids.Player).Needs);

        var menu = engine.Menu(Ids.Player, x + 0.5, y + 0.5, Actor(engine, Ids.Player).Z);
        Assert.Contains(menu.Entries, e => e is { Op: "eat", Available: true });
        Assert.Contains(menu.Entries, e => e is { Op: "sleep", Available: true });
        Assert.True(engine.Submit(Ids.Player, GameAction.On("eat", new ObjectTarget(apple))).Accepted);
        Advance(engine, EatOp.Minutes);

        Assert.Equal(1, engine.OpenSession().GetObject(apple)!.Quantity);
        Assert.Null(Actor(engine, Ids.Player).Needs);
        Assert.True(LoggedEvents(engine, "actor.consumed", Ids.Player).Single().Data["level"] is null);
        var sleep = engine.Submit(Ids.Player, GameAction.On("sleep", new SelfTarget()));
        Assert.Equal((int)Math.Ceiling((1 - NeedModel.NeutralLevel) * SleepOp.FullMinutes),
            sleep.Activity!.EndsMinute - sleep.Activity.StartedMinute);
    }

    [Fact]
    public void A_paused_clock_freezes_every_level()
    {
        using var engine = NewEngine();
        SetNeeds(engine, Extra, hunger: 0.7, thirst: 0.6, rest: 0.5);
        var before = (Level(engine, Extra, NeedCatalog.Hunger), Level(engine, Extra, NeedCatalog.Thirst), Level(engine, Extra, NeedCatalog.Rest));
        engine.SetClock(paused: true);
        Advance(engine, 50);
        Assert.Equal(before, (Level(engine, Extra, NeedCatalog.Hunger), Level(engine, Extra, NeedCatalog.Thirst), Level(engine, Extra, NeedCatalog.Rest)));
    }
}
