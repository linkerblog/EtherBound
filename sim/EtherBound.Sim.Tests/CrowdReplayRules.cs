using System.Security.Cryptography;
using System.Text;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.Minds;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Tests;

/// <summary>
/// A crowd of 200 on the lab map for a hundred ticks. The expected digest was taken before the actor index
/// (Fix20) replaced the all-actor scans, so a change in who collides with whom, in the order of events or in
/// where anyone ends up fails here.
/// </summary>
public sealed class CrowdReplayRules
{
    private const int Crowd = 200;
    private const int Ticks = 100;
    private const string Digest = "40D9ECFB3A8ED1146ECC124158AA3153B0EA14CB8526878457E891F676F2AE31";

    internal static WorldEngine SeededCrowd(int count, long seed = 7)
    {
        var engine = new WorldEngine();
        engine.NewGame(seed, "lab");
        var session = engine.OpenSession();
        var rng = new Random(7);
        var placed = 0;
        for (var attempts = 0; placed < count && attempts < count * 4; attempts++)
        {
            var x = 4 + rng.Next(120);
            var y = 4 + rng.Next(120);
            if (engine.Grid.GroundAt(x, y) is not { } ground) continue;
            if (!engine.Grid.StandingSurfaces(x, y).Any(s => s.H == ground.GroundH)) continue;
            var id = $"crowd-{placed:00000}";
            session.AddActor(new ActorRow
            {
                Id = id, Kind = "extra", Name = id, X = x + 0.5, Y = y + 0.5, H = ground.GroundH,
                Z = PyMath.FloorDiv(ground.GroundH, 6),
                Mind = new Mind(new TilePos(x, y, ground.GroundH)).ToJson(),
                Needs = WorldSetup.StartingNeeds(seed, id, 0).ToJson(),
            });
            placed++;
        }
        session.Commit();
        engine.Reindex();
        Assert.Equal(count, placed);
        return engine;
    }

    internal static string Summary(WorldEngine engine)
    {
        var text = new StringBuilder();
        foreach (var e in engine.ReadEvents(0, 5_000_000))
            text.Append(e.Type).Append('|').Append(e.Seq).Append('|').Append(e.ActorId).Append('|').Append(e.Data.ToJsonString()).Append('\n');
        foreach (var a in engine.OpenSession().Actors())
            text.Append(a.Id).Append('|').Append(a.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(a.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|').Append(a.H).Append('|')
                .Append(a.Activity?.ToJsonString()).Append('|').Append(a.Mind?.ToJsonString()).Append('|')
                .Append(a.Needs?.ToJsonString()).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    [Fact]
    public void A_crowd_of_two_hundred_lives_the_same_hundred_ticks_as_before_the_actor_index()
    {
        using var engine = SeededCrowd(Crowd);
        new ExtrasBrain(engine).Attach(engine.Bus);
        engine.SetClock(speed: 10);
        for (var i = 0; i < Ticks; i++) engine.AdvanceTime();

        Assert.Contains(engine.ReadEvents(0, 5_000_000, "actor.moved"), e => e.ActorId!.StartsWith("crowd-", StringComparison.Ordinal));
        var digest = Summary(engine);
        Assert.True(digest == Digest, $"the crowd's day changed; digest is now {digest}");
    }
}
