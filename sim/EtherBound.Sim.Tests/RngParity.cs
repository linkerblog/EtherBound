using System.Text.Json;
using EtherBound.Sim.Rng;

namespace EtherBound.Sim.Tests;

/// <summary>Every draw of every stream in <c>rng.json</c> equals CPython's, bit for bit.</summary>
public class RngParity
{
    public static IEnumerable<object[]> Streams()
    {
        using var doc = Goldens.Load("rng.json");
        return doc.RootElement.EnumerateArray()
            .Select(s => new object[] { s.GetProperty("seed").GetInt64(), s.GetProperty("system").GetString()! })
            .ToList();
    }

    private static JsonElement Find(JsonDocument doc, long seed, string system) =>
        doc.RootElement.EnumerateArray().First(s =>
            s.GetProperty("seed").GetInt64() == seed && s.GetProperty("system").GetString() == system);

    [Theory]
    [MemberData(nameof(Streams))]
    public void Every_method_matches_cpython(long seed, string system)
    {
        using var doc = Goldens.Load("rng.json");
        var golden = Find(doc, seed, system);
        var streams = new RngStreams(seed);

        var rng = streams.Stream(system);
        foreach (var expected in golden.GetProperty("random").EnumerateArray())
            Assert.Equal(expected.GetDouble(), rng.Random());

        rng = streams.Stream(system);
        foreach (var row in golden.GetProperty("randint").EnumerateArray())
            Assert.Equal(row[2].GetInt64(), rng.RandInt(row[0].GetInt64(), row[1].GetInt64()));

        rng = streams.Stream(system);
        foreach (var row in golden.GetProperty("uniform").EnumerateArray())
            Assert.Equal(row[2].GetDouble(), rng.Uniform(row[0].GetDouble(), row[1].GetDouble()));

        rng = streams.Stream(system);
        foreach (var expected in golden.GetProperty("shuffle").EnumerateArray())
        {
            var values = Enumerable.Range(0, expected.GetArrayLength()).ToList();
            rng.Shuffle(values);
            Assert.Equal(expected.EnumerateArray().Select(v => v.GetInt32()), values);
        }

        rng = streams.Stream(system);
        foreach (var row in golden.GetProperty("sample").EnumerateArray())
        {
            var population = Enumerable.Range(0, row[0].GetInt32()).ToList();
            var picked = rng.Sample(population, row[1].GetInt32());
            Assert.Equal(row[2].EnumerateArray().Select(v => v.GetInt32()), picked);
        }
    }
}
