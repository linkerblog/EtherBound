using EtherBound.Llm;

namespace EtherBound.Sim.Tests;

public sealed class SpendMeterRules
{
    private static CallRecord Call(string role, double? cost, string outcome = "ok") =>
        new(role, "m", 10, 5, cost, outcome, null, "p", "r");

    [Fact]
    public void Spend_adds_the_providers_costs_by_role_and_counts_calls_without_one_apart()
    {
        var meter = new SpendMeter(1.0);

        meter.Add(Call("narrator", 0.25));
        meter.Add(Call("narrator", 0.25));
        meter.Add(Call("jev", 0.001));
        meter.Add(Call("narrator", null));
        meter.Add(Call("narrator", null, "error"));

        Assert.Equal(0.501, meter.TotalUsd, 6);
        Assert.Equal(5, meter.Calls);
        Assert.Equal((4, 0.5), meter.Of("narrator"));
        Assert.Equal(1, meter.CallsWithoutCost);
    }

    [Fact]
    public void The_cap_is_reached_at_the_limit_and_announced_exactly_once()
    {
        var meter = new SpendMeter(0.5);
        meter.Add(Call("narrator", 0.49));
        Assert.False(meter.Capped);
        Assert.False(meter.AnnounceCapOnce());

        meter.Add(Call("narrator", 0.01));

        Assert.True(meter.Capped);
        Assert.True(meter.AnnounceCapOnce());
        Assert.False(meter.AnnounceCapOnce());
    }

    [Fact]
    public void A_cap_of_zero_means_nothing_may_be_spent()
    {
        Assert.True(new SpendMeter(0).Capped);
    }

    [Fact]
    public void Recent_calls_come_back_newest_first_and_are_bounded()
    {
        var meter = new SpendMeter(10);
        for (var i = 0; i < 30; i++) meter.Add(new CallRecord("narrator", "m", i, 0, null, "ok", null, "", ""));

        var recent = meter.Recent(5);

        Assert.Equal(new[] { 29, 28, 27, 26, 25 }, recent.Select(c => c.TokensIn));
        Assert.Equal(20, meter.Recent(100).Count);
    }
}
