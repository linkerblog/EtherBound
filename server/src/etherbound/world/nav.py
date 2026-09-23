import heapq
from collections.abc import Iterator

from etherbound.world.grid import WorldGrid

DIAGONAL_DIRECTIONS = (
    (1, 0),
    (-1, 0),
    (0, 1),
    (0, -1),
    (1, 1),
    (1, -1),
    (-1, 1),
    (-1, -1),
)

Spot = tuple[int, int, int]


def _neighbours(grid: WorldGrid, spot: Spot) -> Iterator[Spot]:
    x, y, h = spot
    for surface in grid.standing_surfaces(x, y):
        if surface.h != h and grid.can_step(x, y, x, y, h):
            yield x, y, surface.h
    for dx, dy in DIAGONAL_DIRECTIONS:
        target_x, target_y = x + dx, y + dy
        if not grid.can_step(x, y, target_x, target_y, h):
            continue
        for surface in grid.standing_surfaces(target_x, target_y):
            if abs(surface.h - h) <= 1:
                yield target_x, target_y, surface.h


def _heuristic(a: Spot, b: Spot) -> float:
    dx, dy, dz = a[0] - b[0], a[1] - b[1], a[2] - b[2]
    return (dx * dx + dy * dy) ** 0.5 + abs(dz) * 0.5


def find_path(
    grid: WorldGrid, start: Spot, goal: Spot, max_expansions: int = 20000
) -> list[Spot] | None:
    """A* over standing spots, using the same rule as movement."""
    if start == goal:
        return [start]
    if not any(surface.h == goal[2] for surface in grid.standing_surfaces(goal[0], goal[1])):
        return None
    frontier: list[tuple[float, Spot]] = [(0.0, start)]
    came_from: dict[Spot, Spot] = {}
    cost: dict[Spot, float] = {start: 0.0}
    expansions = 0
    while frontier:
        if expansions > max_expansions:
            return None
        _, current = heapq.heappop(frontier)
        if current == goal:
            path = [current]
            while current in came_from:
                current = came_from[current]
                path.append(current)
            path.reverse()
            return path
        expansions += 1
        for neighbour in _neighbours(grid, current):
            candidate = cost[current] + _heuristic(current, neighbour)
            if neighbour not in cost or candidate < cost[neighbour]:
                cost[neighbour] = candidate
                came_from[neighbour] = current
                priority = candidate + _heuristic(neighbour, goal)
                heapq.heappush(frontier, (priority, neighbour))
    return None
