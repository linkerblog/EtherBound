using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EtherBound.Llm.Narration;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Sim.Tests;

public sealed class NarrationPacketRules
{
    private static NarrationPacket PacketAt(WorldEngine engine, params string[] facts)
    {
        var state = engine.GetState();
        var player = state.Actors.First(a => a.Id == Ids.Player);
        return NarrationPacket.Build(state, engine.Menu(Ids.Player, player.X, player.Y, player.Z, 1), facts,
            new[] { "Niko waits." }, 3, 9, 6);
    }

    private static void StandBeside(WorldEngine engine, double x, double y, int tileX, int tileY)
    {
        var session = engine.OpenSession();
        var niko = session.GetActor(Ids.Player)!;
        var ground = engine.Grid.StandingSurfaces(tileX, tileY).OrderBy(s => s.H).First();
        (niko.X, niko.Y, niko.H, niko.Z) = (x, y, ground.H, ground.Z);
        session.Commit();
        engine.Reindex();
    }

    [Fact]
    public void The_message_has_no_coordinates_ids_or_numbers_beyond_the_facts_it_was_given()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        StandBeside(engine, 123.5, 126.5, 123, 126);

        var text = PacketAt(engine, "Niko picked up the shovel.").ToUserMessage();

        Assert.DoesNotMatch(new Regex(@"\d"), text.Replace("Niko picked up the shovel.", ""));
        Assert.DoesNotContain("niko", text.Replace("Niko", ""), StringComparison.Ordinal);
        Assert.DoesNotContain("extra-", text);
        Assert.DoesNotContain("object", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void It_names_the_surface_under_niko_and_the_things_beside_him_and_carries_the_time_word_and_recent_lines()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        StandBeside(engine, 123.5, 126.5, 123, 126);

        var packet = PacketAt(engine, "Niko looks closely: Shovel, Wood.");
        var text = packet.ToUserMessage();

        Assert.Equal("night", packet.TimeWord);
        Assert.Equal("asphalt", packet.Standing);
        Assert.Contains("shovel", packet.Nearby);
        Assert.Contains("chest", packet.Nearby);
        Assert.Contains("Time of day: night.", text);
        Assert.Contains("Niko stands on asphalt. Near him: ", text);
        Assert.Contains("- Niko looks closely: Shovel, Wood.", text);
        Assert.Contains("Earlier lines, never to be repeated:\n- Niko waits.", text.Replace("\r\n", "\n"));
        Assert.Equal((3, 9), (packet.FromSeq, packet.ToSeq));
    }

    [Fact]
    public void What_is_inside_a_closed_chest_is_never_named()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        // The chest at (124, 127) holds apples and bottles; Niko stands beside it with the lid down.
        StandBeside(engine, 123.5, 127.5, 123, 127);

        var packet = PacketAt(engine, "Niko waits.");

        Assert.Contains("chest", packet.Nearby);
        Assert.DoesNotContain(packet.Nearby, n => n.Contains("apple") || n.Contains("bottle"));
    }

    [Fact]
    public void Nearby_things_are_distinct_and_capped()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        StandBeside(engine, 125.5, 127.5, 125, 127);
        var state = engine.GetState();
        var player = state.Actors.First(a => a.Id == Ids.Player);

        var packet = NarrationPacket.Build(state, engine.Menu(Ids.Player, player.X, player.Y, player.Z, 1), new[] { "x" }, Array.Empty<string>(), 0, 0, 2);

        Assert.True(packet.Nearby.Count <= 2);
        Assert.Equal(packet.Nearby.Count, packet.Nearby.Distinct().Count());
    }

    [Theory]
    [InlineData(0, "night")]
    [InlineData(4 * 60 + 59, "night")]
    [InlineData(5 * 60, "dawn")]
    [InlineData(9 * 60, "morning")]
    [InlineData(12 * 60, "midday")]
    [InlineData(15 * 60, "afternoon")]
    [InlineData(19 * 60, "evening")]
    [InlineData(21 * 60, "night")]
    [InlineData(24 * 60 + 6 * 60, "dawn")]
    public void The_hour_is_one_word(int minute, string word) => Assert.Equal(word, NarrationPacket.TimeWordOf(minute));

    [Fact]
    public void Events_become_plain_sentences_with_no_ids_and_a_step_or_a_decision_becomes_nothing()
    {
        string? Say(string type, JsonObject data) => NarrationFacts.Summarize(type, data);

        Assert.Equal("Niko dug through the grass. Under it is topsoil.",
            Say("terrain.dug", Json.Obj(("removed", "Grass"), ("exposed", "Topsoil"), ("dug", 1))));
        Assert.Equal("Niko built a brick wall.", Say("wall.built", Json.Obj(("material", "brick"), ("kind", "wall"))));
        Assert.Equal("Niko laid a wood floor.", Say("wall.built", Json.Obj(("material", "wood_floor".Replace("_floor", "")), ("kind", "floor"))));
        Assert.Equal("Niko picked up the shovel.", Say("object.moved", Json.Obj(("op", "take"), ("kind", "shovel"), ("quantity", 1), ("object_id", 42))));
        Assert.Equal("Niko dropped 3 apple.", Say("object.moved", Json.Obj(("op", "drop"), ("kind", "apple"), ("quantity", 3), ("object_id", 7))));
        Assert.Equal("Niko opened the chest.", Say("object.changed", Json.Obj(("op", "open"), ("kind", "chest"))));
        Assert.Equal("Niko hits something hard. It breaks.", Say("physics.resolved", Json.Obj(("op", "hit"), ("broken", new JsonArray(1)))));
        Assert.Null(Say("physics.resolved", Json.Obj(("op", "push"), ("broken", new JsonArray()))));
        Assert.Equal("Time passes.", Say("activity.finished", Json.Obj(("op", "wait"), ("outcome", "completed"))));
        Assert.Null(Say("activity.finished", Json.Obj(("op", "dig"), ("outcome", "completed"))));
        Assert.Equal("Niko cannot finish dig: too far.", Say("activity.finished", Json.Obj(("op", "dig"), ("outcome", "failed"), ("reason", "too far"))));
        Assert.Null(Say("activity.finished", Json.Obj(("op", "dig"), ("outcome", "interrupted"))));
        Assert.Null(Say("actor.moved", Json.Obj(("mode", "walk"))));
        Assert.Null(Say("activity.started", Json.Obj(("op", "wait"))));
        Assert.Null(Say("llm.interpreted", Json.Obj(("text", "x"))));
        Assert.Null(Say("impact", Json.Obj(("energy", 3.0))));
    }

    [Fact]
    public void An_inspection_loses_its_numbers_before_the_narrator_sees_it()
    {
        Assert.Equal("Niko looks closely: Shovel, Wood.", NarrationFacts.Inspection("Shovel · Wood · 1.5 kg"));
        Assert.Equal("Niko looks closely: Asphalt, diggable.", NarrationFacts.Inspection("Asphalt · 1 m · dug 2 m · diggable"));
        Assert.Equal("Niko looks closely: Chest, Wood, closed.", NarrationFacts.Inspection("Chest · Wood · closed · holds 3 things"));
        Assert.Null(NarrationFacts.Inspection("1 m · 2 m"));
        Assert.Null(NarrationFacts.Inspection(null));
    }
}
