import math

from etherbound.world.grid import StandingSurface, WorldGrid

SLOPE_UP_MULTIPLIER = 0.6
SLOPE_DOWN_MULTIPLIER = 0.85
SUBSTEP_METRES = 0.05
BODY_RADIUS_METRES = 0.3


def _tile(x: float, y: float) -> tuple[int, int]:
    return math.floor(x), math.floor(y)


def nearest_surface(grid: WorldGrid, x: float, y: float, h: int) -> StandingSurface | None:
    tile_x, tile_y = _tile(x, y)
    best: StandingSurface | None = None
    for surface in grid.standing_surfaces(tile_x, tile_y):
        if abs(surface.h - h) > 1:
            continue
        if best is None or abs(surface.h - h) < abs(best.h - h):
            best = surface
    return best


def _can_enter(grid: WorldGrid, x: float, y: float, h: int, nx: float, ny: float) -> bool:
    current_tile = _tile(x, y)
    target_tile = _tile(nx, ny)
    if current_tile == target_tile:
        return True
    return grid.can_step(current_tile[0], current_tile[1], target_tile[0], target_tile[1], h)


def _movement_multiplier(grid: WorldGrid, h: int, surface: StandingSurface) -> float:
    if surface.h > h:
        cost = SLOPE_UP_MULTIPLIER
    elif surface.h < h:
        cost = SLOPE_DOWN_MULTIPLIER
    else:
        cost = 1.0
    material = grid.registry.get(surface.material_id)
    if material is not None and material.walk_cost > 0:
        cost /= material.walk_cost
    return cost


def move_in_world(
    x: float,
    y: float,
    h: int,
    dx: float,
    dy: float,
    distance: float,
    grid: WorldGrid,
) -> tuple[float, float, int]:
    """Advance a body one sub-step at a time, honouring the standing rule."""
    magnitude = math.hypot(dx, dy)
    if magnitude == 0 or distance <= 0:
        return x, y, h
    ux, uy = dx / magnitude, dy / magnitude
    remaining = distance
    blocked_x = ux == 0
    blocked_y = uy == 0
    while remaining > 0:
        base = min(SUBSTEP_METRES, remaining)
        moved = False
        spent = 0.0
        if not blocked_x:
            source_tile = _tile(x, y)
            peek_x = x + ux * base
            if _can_enter(grid, x, y, h, peek_x, y):
                surface = nearest_surface(grid, peek_x, y, h)
                if surface is not None:
                    multiplier = _movement_multiplier(grid, h, surface)
                    x += ux * base
                    if _tile(x, y) != source_tile:
                        h = surface.h
                    moved = True
                    spent = max(spent, base / multiplier)
            elif _tile(peek_x, y) != _tile(x, y):
                boundary = math.floor(x) + (1 if ux > 0 else 0)
                x = boundary - BODY_RADIUS_METRES if ux > 0 else boundary + BODY_RADIUS_METRES
                blocked_x = True
                moved = True
                spent = max(spent, base)
        if not blocked_y:
            source_tile = _tile(x, y)
            peek_y = y + uy * base
            if _can_enter(grid, x, y, h, x, peek_y):
                surface = nearest_surface(grid, x, peek_y, h)
                if surface is not None:
                    multiplier = _movement_multiplier(grid, h, surface)
                    y += uy * base
                    if _tile(x, y) != source_tile:
                        h = surface.h
                    moved = True
                    spent = max(spent, base / multiplier)
            elif _tile(x, peek_y) != _tile(x, y):
                boundary = math.floor(y) + (1 if uy > 0 else 0)
                y = boundary - BODY_RADIUS_METRES if uy > 0 else boundary + BODY_RADIUS_METRES
                blocked_y = True
                moved = True
                spent = max(spent, base)
        if not moved:
            break
        remaining -= spent if spent > 0 else base
        if blocked_x and blocked_y:
            break
    return x, y, h
