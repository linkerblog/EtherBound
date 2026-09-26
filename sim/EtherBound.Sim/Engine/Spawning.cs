using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;

namespace EtherBound.Sim.Engine;

/// <summary>Where a world's actors start (<c>world_setup.spawn_point</c>, <c>standing_h_near</c>).</summary>
public static class Spawning
{
    public static int? StandingHNear(WorldGrid grid, MaterialRegistry registry, double x, double y)
    {
        var surfaces = grid.StandingSurfaces(PyMath.Floor(x), PyMath.Floor(y));
        if (surfaces.Count == 0) return null;
        var material = registry.Get(surfaces[0].MaterialId);
        if (material is null || !material.Walkable) return null;
        return surfaces[0].H;
    }

    /// <summary>The generator's spawn if it stands, else the first standing tile in chunk order.</summary>
    public static (double X, double Y, int H) SpawnPoint(WorldGrid grid, MaterialRegistry registry, GeneratorSpec spec,
        IGeneratorOptions options, long seed)
    {
        var spawn = spec.Spawn(seed, options);
        if (StandingHNear(grid, registry, spawn.X, spawn.Y) is not null) return spawn;
        foreach (var (cx, cy) in grid.Chunks.Keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2))
        for (var ly = 0; ly < ChunkConst.Size; ly++)
        for (var lx = 0; lx < ChunkConst.Size; lx++)
        {
            int wx = cx * ChunkConst.Size + lx, wy = cy * ChunkConst.Size + ly;
            if (StandingHNear(grid, registry, wx + 0.5, wy + 0.5) is { } standing) return (wx + 0.5, wy + 0.5, standing);
        }
        return (0.5, 0.5, 0);
    }
}
