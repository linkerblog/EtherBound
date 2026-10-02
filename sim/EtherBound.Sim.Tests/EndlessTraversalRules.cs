using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;

namespace EtherBound.Sim.Tests;

/// <summary>The endless world is never partitioned by water or rock, and searching it stays bounded (Dev-010).</summary>
public class EndlessTraversalRules
{
    private static readonly MaterialRegistry Registry = MaterialRegistry.Load();

    [Theory]
    [InlineData(7L)]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(99L)]
    public void Spawn_reaches_nearly_all_the_walkable_ground_around_it(long seed)
    {
        var field = new TerrainField(seed, new EndlessOptions(), Registry);
        var spawn = EndlessWorld.Spawn(field);
        int sx = (int)Math.Floor(spawn.X), sy = (int)Math.Floor(spawn.Y);
        const int radius = 200;
        var walkable = 0;
        for (var y = sy - radius; y <= sy + radius; y++)
        for (var x = sx - radius; x <= sx + radius; x++)
            if (field.Walkable(field.At(x, y))) walkable++;

        var seen = new HashSet<(int, int)> { (sx, sy) };
        var queue = new Queue<(int X, int Y, int H)>();
        queue.Enqueue((sx, sy, spawn.H));
        while (queue.Count > 0)
        {
            var (x, y, h) = queue.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (Math.Abs(nx - sx) > radius || Math.Abs(ny - sy) > radius || !seen.Add((nx, ny))) continue;
                if (field.StandingHeight(nx, ny) is not { } nh || Math.Abs(nh - h) > 1) continue;
                queue.Enqueue((nx, ny, nh));
            }
        }
        var reached = seen.Count(p => field.Walkable(field.At(p.Item1, p.Item2)));
        Assert.True(reached >= walkable * 0.97, $"seed {seed}: {reached} of {walkable} walkable tiles are connected to spawn");
    }

    [Fact]
    public void A_bounded_search_reaches_targets_500_m_out_in_most_directions()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "infinite");
        var grid = engine.Grid;
        var niko = engine.OpenSession().GetActor(Ids.Player)!;
        var start = new Spot(niko.TileX, niko.TileY, niko.H);
        var reached = 0;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            // The nearest standing tile to the target point, so a lake at the exact spot does not fail the sample.
            Spot? goal = null;
            for (var r = 0; r < 12 && goal is null; r++)
            for (var oy = -r; oy <= r && goal is null; oy++)
            for (var ox = -r; ox <= r && goal is null; ox++)
            {
                if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != r) continue;
                int x = start.X + dx * 500 + ox, y = start.Y + dy * 500 + oy;
                if (grid.StandingSurfaces(x, y) is { Count: > 0 } surfaces) goal = new Spot(x, y, surfaces[0].H);
            }
            if (goal is not null && Nav.FindPath(grid, start, goal.Value, 60000) is not null) reached++;
        }
        Assert.True(reached >= 7, $"{reached} of 8 directions reachable");
    }

    [Fact]
    public void An_unreachable_goal_ends_within_the_budget_and_loads_a_bounded_number_of_chunks()
    {
        var grass = Registry["grass"].Id;
        var rock = Registry["rock"].Id;
        var loads = 0;
        var grid = new WorldGrid(registry: Registry, chunkLoader: (cx, cy) =>
        {
            loads++;
            var materials = Enumerable.Repeat((ushort)grass, ChunkConst.CellCount).ToArray();
            // A walkable island at (50, 50) sealed by rock: reachable by no path at all.
            for (var y = 49; y <= 51; y++)
            for (var x = 49; x <= 51; x++)
                if ((x, y) != (50, 50) && PyMath.FloorDiv(x, ChunkConst.Size) == cx && PyMath.FloorDiv(y, ChunkConst.Size) == cy)
                    materials[(y - cy * ChunkConst.Size) * ChunkConst.Size + (x - cx * ChunkConst.Size)] = (ushort)rock;
            return (Chunk.Flat(cx, cy, 0, grass).With(surfaceMat: materials), Array.Empty<ChunkLevel>());
        });

        var path = Nav.FindPath(grid, new Spot(0, 0, 0), new Spot(50, 50, 0), 2000);

        Assert.Null(path);
        Assert.InRange(loads, 1, 36);
        Assert.Equal(loads, grid.Chunks.Count);
    }
}
