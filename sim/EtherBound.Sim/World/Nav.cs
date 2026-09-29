namespace EtherBound.Sim.World;

/// <summary>A standing spot: tile and surface height in half-metres.</summary>
public readonly record struct Spot(int X, int Y, int H);

/// <summary>A standing spot plus the connected part of its tile occupied by the path.</summary>
public readonly record struct NavNode(Spot Spot, byte Region);

/// <summary>A* over standing spots and, where needed, their connected tile regions.</summary>
public static class Nav
{
    public sealed class SearchWorkspace
    {
        internal PriorityQueue<(NavNode Node, double Cost), (double, int, int, int, byte)> Frontier { get; } = new();
        internal Dictionary<NavNode, NavNode> CameFrom { get; } = new();
        internal Dictionary<NavNode, double> Cost { get; } = new();
    }

    private static readonly (int Dx, int Dy)[] Directions =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    private static IEnumerable<byte> VerticalRegions(WorldGrid grid, Spot spot, byte region, int nextH)
    {
        var fromMask = grid.InteriorWallMaskAt(spot.X, spot.Y, spot.H);
        var toMask = grid.InteriorWallMaskAt(spot.X, spot.Y, nextH);
        var fromQuadrants = region == 0 ? WallRegions.AllQuadrants : WallRegions.Quadrants(fromMask, region);
        foreach (var next in WallRegions.For(toMask))
            if ((fromQuadrants & next.Quadrants) != 0) yield return next.Id;
    }

    private static IEnumerable<NavNode> Neighbours(WorldGrid grid, NavNode node)
    {
        var spot = node.Spot;
        foreach (var surface in grid.StandingSurfaces(spot.X, spot.Y))
            if (surface.H != spot.H && grid.CanStep(spot.X, spot.Y, spot.X, spot.Y, spot.H))
                foreach (var region in VerticalRegions(grid, spot, node.Region, surface.H))
                    yield return new NavNode(spot with { H = surface.H }, region);

        foreach (var (dx, dy) in Directions)
        {
            int tx = spot.X + dx, ty = spot.Y + dy;
            if (!grid.CanStep(spot.X, spot.Y, tx, ty, spot.H)) continue;
            foreach (var surface in grid.StandingSurfaces(tx, ty))
            {
                if (Math.Abs(surface.H - spot.H) > 1) continue;
                var targetMask = grid.InteriorWallMaskAt(tx, ty, surface.H);
                var sourceMask = grid.InteriorWallMaskAt(spot.X, spot.Y, spot.H);
                if (dx != 0 && dy != 0)
                {
                    // The two orthogonal legs are represented explicitly when a slot divides either tile.
                    if (sourceMask != 0 || targetMask != 0 ||
                        grid.InteriorWallMaskAt(tx, spot.Y, spot.H) != 0 ||
                        grid.InteriorWallMaskAt(spot.X, ty, spot.H) != 0) continue;
                    yield return new NavNode(new Spot(tx, ty, surface.H), 0);
                    continue;
                }

                foreach (var region in grid.EntryRegions(tx, ty, surface.H, dx, dy))
                {
                    if (WallRegions.SharedPorts(sourceMask, node.Region, targetMask, region, dx, dy) == 0) continue;
                    yield return new NavNode(new Spot(tx, ty, surface.H), region);
                }
            }
        }
    }

    // `(dx*dx + dy*dy) ** 0.5` is C pow(), not sqrt(); both runtimes call the same CRT on Windows.
    private static double Heuristic(Spot a, Spot b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y, dz = a.H - b.H;
        return Math.Pow(dx * dx + dy * dy, 0.5) + Math.Abs(dz) * 0.5;
    }

    public static List<Spot>? FindPath(WorldGrid grid, Spot start, Spot goal, int maxExpansions = 20000,
        SearchWorkspace? workspace = null) =>
        FindPathNodes(grid, start, goal, 0, maxExpansions, workspace)?.Select(node => node.Spot).ToList();

    public static List<NavNode>? FindPathNodes(WorldGrid grid, Spot start, Spot goal, byte startRegion = 0,
        int maxExpansions = 20000, SearchWorkspace? workspace = null)
    {
        var startNode = new NavNode(start, startRegion);
        if (start == goal) return new List<NavNode> { startNode };
        if (!grid.StandingSurfaces(goal.X, goal.Y).Any(s => s.H == goal.H)) return null;
        // Include region after the historic (priority, x, y, h) key; unsplit worlds retain their old ordering.
        workspace ??= new SearchWorkspace();
        var frontier = workspace.Frontier;
        var cameFrom = workspace.CameFrom;
        var cost = workspace.Cost;
        frontier.Clear();
        cameFrom.Clear();
        cost.Clear();
        frontier.Enqueue((startNode, 0.0), (0.0, start.X, start.Y, start.H, startRegion));
        cost[startNode] = 0.0;
        var expansions = 0;
        while (frontier.Count > 0)
        {
            var (current, currentCost) = frontier.Dequeue();
            if (!cost.TryGetValue(current, out var best) || currentCost != best) continue;
            if (expansions > maxExpansions) return null;
            if (current.Spot == goal)
            {
                var path = new List<NavNode> { current };
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
                var candidate = currentCost + Heuristic(current.Spot, neighbour.Spot);
                if (!cost.TryGetValue(neighbour, out var known) || candidate < known)
                {
                    cost[neighbour] = candidate;
                    cameFrom[neighbour] = current;
                    var priority = candidate + Heuristic(neighbour.Spot, goal);
                    frontier.Enqueue((neighbour, candidate),
                        (priority, neighbour.Spot.X, neighbour.Spot.Y, neighbour.Spot.H, neighbour.Region));
                }
            }
        }
        return null;
    }
}
