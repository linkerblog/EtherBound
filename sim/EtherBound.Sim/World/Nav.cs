namespace EtherBound.Sim.World;

/// <summary>A standing spot: tile and surface height in half-metres.</summary>
public readonly record struct Spot(int X, int Y, int H);

/// <summary>A* over standing spots, using the same rule as movement (<c>world/nav.py</c>).</summary>
public static class Nav
{
    public sealed class SearchWorkspace
    {
        internal PriorityQueue<(Spot Spot, double Cost), (double, int, int, int)> Frontier { get; } = new();
        internal Dictionary<Spot, Spot> CameFrom { get; } = new();
        internal Dictionary<Spot, double> Cost { get; } = new();
    }

    private static readonly (int Dx, int Dy)[] Directions =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    private static IEnumerable<Spot> Neighbours(WorldGrid grid, Spot spot)
    {
        foreach (var surface in grid.StandingSurfaces(spot.X, spot.Y))
            if (surface.H != spot.H && grid.CanStep(spot.X, spot.Y, spot.X, spot.Y, spot.H))
                yield return spot with { H = surface.H };
        foreach (var (dx, dy) in Directions)
        {
            int tx = spot.X + dx, ty = spot.Y + dy;
            if (!grid.CanStep(spot.X, spot.Y, tx, ty, spot.H)) continue;
            foreach (var surface in grid.StandingSurfaces(tx, ty))
                if (Math.Abs(surface.H - spot.H) <= 1) yield return new Spot(tx, ty, surface.H);
        }
    }

    // `(dx*dx + dy*dy) ** 0.5` is C pow(), not sqrt(); both runtimes call the same CRT on Windows.
    private static double Heuristic(Spot a, Spot b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y, dz = a.H - b.H;
        return Math.Pow(dx * dx + dy * dy, 0.5) + Math.Abs(dz) * 0.5;
    }

    public static List<Spot>? FindPath(WorldGrid grid, Spot start, Spot goal, int maxExpansions = 20000, SearchWorkspace? workspace = null)
    {
        if (start == goal) return new List<Spot> { start };
        if (!grid.StandingSurfaces(goal.X, goal.Y).Any(s => s.H == goal.H)) return null;
        // heapq orders by (priority, (x, y, h)); equal keys are equal spots, so any min-heap agrees.
        workspace ??= new SearchWorkspace();
        var frontier = workspace.Frontier;
        var cameFrom = workspace.CameFrom;
        var cost = workspace.Cost;
        frontier.Clear();
        cameFrom.Clear();
        cost.Clear();
        frontier.Enqueue((start, 0.0), (0.0, start.X, start.Y, start.H));
        cost[start] = 0.0;
        var expansions = 0;
        while (frontier.Count > 0)
        {
            var (current, currentCost) = frontier.Dequeue();
            if (!cost.TryGetValue(current, out var best) || currentCost != best) continue;
            if (expansions > maxExpansions) return null;
            if (current == goal)
            {
                var path = new List<Spot> { current };
                while (cameFrom.TryGetValue(current, out var previous))
                {
                    current = previous;
                    path.Add(current);
                }
                path.Reverse();
                return path;
            }
            expansions++;
            foreach (var neighbour in Neighbours(grid, current))
            {
                var candidate = currentCost + Heuristic(current, neighbour);
                if (!cost.TryGetValue(neighbour, out var known) || candidate < known)
                {
                    cost[neighbour] = candidate;
                    cameFrom[neighbour] = current;
                    var priority = candidate + Heuristic(neighbour, goal);
                    frontier.Enqueue((neighbour, candidate), (priority, neighbour.X, neighbour.Y, neighbour.H));
                }
            }
        }
        return null;
    }
}
