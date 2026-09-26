using System.Globalization;
using EtherBound.Sim.Rng;
using Tomlyn.Model;

namespace EtherBound.Sim.World;

/// <summary>One seeded Extra: where it starts and the home spot its routine wanders around.</summary>
public sealed record GeneratedActor(string Id, string Name, double X, double Y, int H, int AnchorX, int AnchorY, int AnchorH);

/// <summary>
/// Who lives in a generated world (<c>world/population.py</c>). Deliberately not part of the
/// generated world: population is actors, not terrain, and never changes the world's bytes.
/// </summary>
public static class Population
{
    public const int ExtraCount = 6;
    public const int PlacementRadiusM = 15;
    public const double MinSpacingM = 2.0;
    public const int MaxExpansions = 3000;
    public const double ExtraMassKg = 80.0;

    private static readonly Lazy<(IReadOnlyList<string> Given, IReadOnlyList<string> Family)> NameLists = new(() =>
    {
        var document = DataFiles.Read("names.toml");
        if (document["given"] is not TomlArray given || document["family"] is not TomlArray family)
            throw new InvalidDataException("names.toml must contain given and family arrays");
        return (given.Select(n => (string)n!).ToList(), family.Select(n => (string)n!).ToList());
    });

    /// <summary>Walkable, standing ground tiles within the placement radius, spawn tile excluded.</summary>
    private static List<Spot> Candidates(WorldGrid grid, MaterialRegistry registry, int tileX, int tileY)
    {
        var result = new List<Spot>();
        for (var dy = -PlacementRadiusM; dy <= PlacementRadiusM; dy++)
        for (var dx = -PlacementRadiusM; dx <= PlacementRadiusM; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            if (PyMath.Hypot(dx, dy) > PlacementRadiusM) continue;
            int x = tileX + dx, y = tileY + dy;
            if (grid.GroundAt(x, y) is not { } ground) continue;
            var material = registry.Get(ground.SurfaceMat);
            if (material is null || !material.Walkable) continue;
            if (!grid.StandingSurfaces(x, y).Any(s => s.H == ground.GroundH)) continue;
            result.Add(new Spot(x, y, ground.GroundH));
        }
        return result;
    }

    private static bool SpacingOk(Spot candidate, List<Spot> chosen, double limit) =>
        chosen.All(other => PyMath.Hypot(candidate.X - other.X, candidate.Y - other.Y) >= limit);

    /// <summary>Seeds <see cref="ExtraCount"/> Extras near the spawn, deterministically from one stream.</summary>
    public static IReadOnlyList<GeneratedActor> Populate(WorldGrid grid, MaterialRegistry registry,
        (double X, double Y, int H) spawn, long seed)
    {
        var rng = new RngStreams(seed).Stream("population");
        var (given, family) = NameLists.Value;
        var givenPicked = rng.Sample(given, ExtraCount);
        var familyPicked = rng.Sample(family, ExtraCount);

        int tileX = PyMath.Floor(spawn.X), tileY = PyMath.Floor(spawn.Y);
        var start = new Spot(tileX, tileY, spawn.H);
        var candidates = Candidates(grid, registry, tileX, tileY);
        rng.Shuffle(candidates);

        var chosen = new List<Spot>();
        foreach (var limit in new[] { MinSpacingM, 1.0, 0.0 })
        {
            foreach (var candidate in candidates)
            {
                if (chosen.Count == ExtraCount) break;
                if (chosen.Contains(candidate) || !SpacingOk(candidate, chosen, limit)) continue;
                if (Nav.FindPath(grid, start, candidate, MaxExpansions) is null) continue;
                chosen.Add(candidate);
            }
            if (chosen.Count == ExtraCount) break;
        }

        return chosen.Select((spot, i) => new GeneratedActor(
            string.Create(CultureInfo.InvariantCulture, $"extra-{i + 1:000}"),
            $"{givenPicked[i]} {familyPicked[i]}",
            spot.X + 0.5, spot.Y + 0.5, spot.H, spot.X, spot.Y, spot.H)).ToList();
    }
}
