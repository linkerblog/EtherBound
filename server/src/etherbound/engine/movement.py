import math

from etherbound.testmap import TestMap


def move_on_map(
    x: float,
    y: float,
    dx: float,
    dy: float,
    distance: float,
    game_map: TestMap,
) -> tuple[float, float]:
    magnitude = math.hypot(dx, dy)
    if magnitude == 0 or distance <= 0:
        return x, y
    ux, uy = dx / magnitude, dy / magnitude
    remaining = distance
    step = 0.05
    while remaining > 0:
        current = min(step, remaining)
        candidate_x = x + ux * current
        if not game_map.is_blocked(candidate_x, y):
            x = candidate_x
        candidate_y = y + uy * current
        if not game_map.is_blocked(x, candidate_y):
            y = candidate_y
        if game_map.is_blocked(x + ux * current, y) and game_map.is_blocked(x, y + uy * current):
            break
        remaining -= current
    return x, y
