using EtherBound.Sim.Core;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Tests;

/// <summary>The arithmetic of needs and the data that feeds it: decay, urgency, the night, and what the files may say.</summary>
public sealed class NeedModelRules
{
    private static NeedSpec Spec(string key) => NeedCatalog.Default[key];

    [Fact]
    public void A_level_falls_linearly_with_the_clock_and_never_below_zero()
    {
        var hunger = Spec(NeedCatalog.Hunger);
        var start = new NeedValue(1.0, 100);
        Assert.Equal(1.0, NeedModel.Level(hunger, start, 100), 12);
        Assert.Equal(0.5, NeedModel.Level(hunger, start, 100 + 8 * 60), 9);
        Assert.Equal(0.0, NeedModel.Level(hunger, start, 100 + 16 * 60), 9);
        Assert.Equal(0.0, NeedModel.Level(hunger, start, 100 + 100 * 60), 12);
        // A clock behind the write (a reread, never a rewind) leaves the level where it was.
        Assert.Equal(1.0, NeedModel.Level(hunger, start, 40), 12);
        Assert.True(Spec(NeedCatalog.Thirst).RatePerMinute > hunger.RatePerMinute);
    }

    [Fact]
    public void Stored_needs_round_trip_and_a_missing_need_counts_as_satisfied()
    {
        var needs = ActorNeeds.Full(0).With(NeedCatalog.Hunger, 0.4, 60);
        var read = ActorNeeds.Parse(needs.ToJson())!;
        Assert.Equal(needs.Level(NeedCatalog.Hunger, 60), read.Level(NeedCatalog.Hunger, 60), 12);
        Assert.Equal(1.0, read.Level(NeedCatalog.Rest, 0), 12);

        Assert.Null(ActorNeeds.Parse(null));
        var partial = ActorNeeds.Parse(Json.Obj(("hunger", Json.Obj(("level", 0.3), ("at", 10)))))!;
        Assert.Equal(0.3, partial.Level(NeedCatalog.Hunger, 10), 12);
        Assert.Equal(1.0, partial.Level(NeedCatalog.Thirst, 500), 12);
        // Garbage is read defensively, never thrown at the sim thread.
        Assert.Equal(1.0, ActorNeeds.Parse(Json.Obj(("hunger", "full")))!.Level(NeedCatalog.Hunger, 5), 12);
        Assert.Equal(0.0, ActorNeeds.Parse(Json.Obj(("hunger", Json.Obj(("level", -3.0), ("at", 0)))))!.Level(NeedCatalog.Hunger, 0), 12);
    }

    [Theory]
    [InlineData(21 * 60 + 59, false)]
    [InlineData(22 * 60, true)]
    [InlineData(0, true)]
    [InlineData(5 * 60 + 59, true)]
    [InlineData(6 * 60, false)]
    [InlineData(NeedModel.MinutesPerDay + 23 * 60, true)]
    public void Night_is_ten_pm_to_six_am_on_every_day(int minute, bool night) => Assert.Equal(night, NeedModel.IsNight(minute));

    [Fact]
    public void Hunger_is_sought_below_its_threshold_at_any_hour()
    {
        var hunger = Spec(NeedCatalog.Hunger);
        foreach (var minute in new[] { 12 * 60, 23 * 60 })
        {
            Assert.Equal(0.0, NeedModel.Urgency(hunger, 0.5, minute));
            Assert.Equal(0.0, NeedModel.Urgency(hunger, 0.9, minute));
            Assert.True(NeedModel.Urgency(hunger, 0.25, minute) > 0);
        }
        Assert.Equal(1.0, NeedModel.Urgency(hunger, 0.0, 12 * 60), 12);
        Assert.True(NeedModel.Urgency(hunger, 0.1, 12 * 60) > NeedModel.Urgency(hunger, 0.4, 12 * 60));
    }

    [Fact]
    public void Rest_is_sought_below_half_at_night_and_only_below_the_urgent_level_by_day()
    {
        var rest = Spec(NeedCatalog.Rest);
        const int noon = 12 * 60, midnight = 0;
        Assert.Equal(0.0, NeedModel.Urgency(rest, 0.4, noon));
        Assert.Equal(0.0, NeedModel.Urgency(rest, 0.2, noon));
        Assert.True(NeedModel.Urgency(rest, 0.15, noon) > 0);
        Assert.True(NeedModel.Urgency(rest, 0.4, midnight) > 0);
        Assert.Equal(0.0, NeedModel.Urgency(rest, 0.5, midnight));
        // By day the scale is the urgent level; at night it is the threshold, times the night weight.
        Assert.Equal(0.5, NeedModel.Urgency(rest, 0.1, noon), 12);
        Assert.Equal(1.2, NeedModel.Urgency(rest, 0.1, midnight), 12);
    }

    [Fact]
    public void A_start_level_stays_in_the_first_band_so_no_extra_begins_hungry()
    {
        Assert.Equal(0.8, NeedModel.StartLevel(0.0), 12);
        Assert.Equal(1.0, NeedModel.StartLevel(1.0), 12);
        Assert.Equal(0.8, NeedModel.StartLevel(-5.0), 12);
        Assert.Equal(1.0, NeedModel.StartLevel(9.0), 12);
        // Two game hours of decay still leave every need at or above its threshold.
        foreach (var spec in NeedCatalog.Default.Specs)
            Assert.True(NeedModel.Level(spec, new NeedValue(0.8, 0), 120) >= spec.Threshold - 1e-9, spec.Key);
    }

    [Fact]
    public void Seeded_starting_needs_repeat_for_a_seed_and_differ_between_actors()
    {
        var a = WorldSetup_StartingNeeds(7, "extra-001");
        Assert.True(Json.Same(a.ToJson(), WorldSetup_StartingNeeds(7, "extra-001").ToJson()));
        Assert.False(Json.Same(a.ToJson(), WorldSetup_StartingNeeds(7, "extra-002").ToJson()));
        Assert.False(Json.Same(a.ToJson(), WorldSetup_StartingNeeds(8, "extra-001").ToJson()));
        foreach (var spec in NeedCatalog.Default.Specs) Assert.InRange(a.Level(spec.Key, 0), 0.8, 1.0);
    }

    private static ActorNeeds WorldSetup_StartingNeeds(long seed, string id) => EtherBound.Sim.Engine.WorldSetup.StartingNeeds(seed, id, 0);

    private const string Valid = """
        [[needs]]
        key = "hunger"
        empty_hours = 16.0
        threshold = 0.5
        urgent = 0.2
        night_only = false
        night_weight = 1.0
        [[needs]]
        key = "thirst"
        empty_hours = 8.0
        threshold = 0.5
        urgent = 0.2
        night_only = false
        night_weight = 1.0
        [[needs]]
        key = "rest"
        empty_hours = 17.0
        threshold = 0.5
        urgent = 0.2
        night_only = true
        night_weight = 1.5
        """;

    [Fact]
    public void The_shipped_needs_file_parses_and_a_valid_copy_does_too()
    {
        Assert.Equal(new[] { "hunger", "thirst", "rest" }, NeedCatalog.Default.Specs.Select(s => s.Key));
        Assert.True(Spec(NeedCatalog.Rest).NightOnly);
        Assert.Equal(3, NeedCatalog.FromText(Valid).Specs.Count);
    }

    [Theory]
    [InlineData("empty_hours = 16.0", "empty_hours = 0.0", "empty_hours must be positive")]
    [InlineData("threshold = 0.5", "threshold = 1.5", "threshold must be in (0, 1]")]
    [InlineData("urgent = 0.2", "urgent = 0.9", "urgent must be in (0, threshold]")]
    [InlineData("night_weight = 1.0", "night_weight = 0.0", "night_weight must be positive")]
    [InlineData("urgent = 0.2", "urgent = 0.2\nsurprise = 1", "unknown fields surprise")]
    [InlineData("night_weight = 1.0", "", "night_weight is required")]
    public void A_bad_needs_file_is_refused(string find, string replace, string message)
    {
        var broken = Valid.Replace(find, replace, StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => NeedCatalog.FromText(broken));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void A_needs_file_must_carry_all_three_needs_once()
    {
        var withoutRest = Valid[..Valid.LastIndexOf("[[needs]]", StringComparison.Ordinal)];
        Assert.Contains("missing the need rest", Assert.Throws<InvalidDataException>(() => NeedCatalog.FromText(withoutRest)).Message);
        Assert.Contains("duplicate", Assert.Throws<InvalidDataException>(() => NeedCatalog.FromText(Valid + "\n" + Valid)).Message);
        Assert.Throws<InvalidDataException>(() => NeedCatalog.FromText("title = \"nothing\""));
    }

    [Fact]
    public void Edibility_and_drinkability_are_components_and_water_is_a_material_property()
    {
        var catalog = ObjectCatalog.Load();
        Assert.Equal(0.2, catalog["apple"].Edible!.Satiety, 12);
        Assert.Null(catalog["bottle"].Edible);
        Assert.Null(catalog["apple"].Drinkable);
        var registry = MaterialRegistry.Load();
        Assert.True(registry["water_shallow"].Drinkable);
        Assert.False(registry["water_deep"].Drinkable);
        Assert.False(registry["grass"].Drinkable);

        const string bottle = """
            [[kinds]]
            key = "tonic"
            name = "Tonic"
            material = "glass"
            mass = 0.4
            bulk = 1.0
            height = 0
            stackable = true
            [kinds.drinkable]
            hydration = 0.3
            """;
        var parsed = ObjectCatalog.FromText(bottle, registry);
        Assert.Equal(0.3, parsed["tonic"].Drinkable!.Hydration, 12);
        Assert.Contains("hydration is required", Assert.Throws<InvalidDataException>(() =>
            ObjectCatalog.FromText(bottle.Replace("hydration = 0.3", "", StringComparison.Ordinal), registry)).Message);
        Assert.Contains("must be in (0, 1]", Assert.Throws<InvalidDataException>(() =>
            ObjectCatalog.FromText(bottle.Replace("0.3", "1.5", StringComparison.Ordinal), registry)).Message);
        Assert.Contains("unknown edible keys", Assert.Throws<InvalidDataException>(() =>
            ObjectCatalog.FromText(bottle.Replace("[kinds.drinkable]\nhydration = 0.3", "[kinds.edible]\nsatiety = 0.1\ncalories = 3", StringComparison.Ordinal), registry)).Message);
    }
}
