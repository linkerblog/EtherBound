from etherbound.engine.actions import EdgeTarget
from etherbound.engine.ops.base import ActionContext
from etherbound.world.chunk import CHUNK_SIZE, EDGE_N_DOORWAY, EDGE_W_DOORWAY, NO_FLOOR, ChunkLevel
from etherbound.world.grid import WorldGrid


def sign(value: int) -> int:
    return (value > 0) - (value < 0)


def cardinal(dx: int, dy: int) -> bool:
    return abs(dx) + abs(dy) == 1


def canonical_edge(target: EdgeTarget) -> EdgeTarget:
    if target.direction == "south":
        return target.model_copy(update={"y": target.y + 1, "direction": "north"})
    if target.direction == "east":
        return target.model_copy(update={"x": target.x + 1, "direction": "west"})
    return target


def edge_cell(ctx: ActionContext, target: EdgeTarget) -> tuple[ChunkLevel, int, int] | None:
    edge = canonical_edge(target)
    cx, cy, local_x, local_y = WorldGrid.chunk_coords(edge.x, edge.y)
    level = ctx.grid.level(cx, cy, edge.z)
    if level is None:
        return None
    index = level.index(local_x, local_y)
    material_id = level.wall_n[index] if edge.direction == "north" else level.wall_w[index]
    if material_id == 0:
        return None
    return level, index, material_id


def wall_bottom(ctx: ActionContext, level: ChunkLevel, index: int) -> int:
    bottom = level.floor_h[index]
    if bottom != NO_FLOOR:
        return bottom
    chunk = ctx.grid.chunk(level.cx, level.cy)
    if chunk is None:
        return level.z * 6
    supports = [chunk.ground_h[index]]
    supports.extend(
        lower.floor_h[index]
        for lower in ctx.grid.levels.values()
        if lower.cx == level.cx
        and lower.cy == level.cy
        and lower.z < level.z
        and lower.floor_h[index] != NO_FLOOR
    )
    below_level = [support for support in supports if support < (level.z + 1) * 6]
    return max(below_level, default=level.z * 6)


def edge_reachable(ctx: ActionContext, target: EdgeTarget) -> bool:
    edge = canonical_edge(target)
    record = edge_cell(ctx, edge)
    if record is None:
        return False
    level, index, _ = record
    bottom = wall_bottom(ctx, level, index)
    ax, ay = ctx.actor_tile()
    sides = (
        ((edge.x, edge.y), (edge.x, edge.y - 1))
        if edge.direction == "north"
        else ((edge.x, edge.y), (edge.x - 1, edge.y))
    )
    return (
        any((ax, ay) == side for side in sides)
        and bottom <= ctx.actor.h + 3
        and bottom + 6 >= (ctx.actor.h - 2)
    )


def edge_for_step(x: int, y: int, z: int, dx: int, dy: int) -> EdgeTarget:
    if (dx, dy) == (0, -1):
        return EdgeTarget(x=x, y=y, z=z, direction="north")
    if (dx, dy) == (0, 1):
        return EdgeTarget(x=x, y=y + 1, z=z, direction="north")
    if (dx, dy) == (-1, 0):
        return EdgeTarget(x=x, y=y, z=z, direction="west")
    return EdgeTarget(x=x + 1, y=y, z=z, direction="west")


def wall_blocking_step(
    ctx: ActionContext, x: int, y: int, h: int, dx: int, dy: int
) -> EdgeTarget | None:
    nx, ny = x + dx, y + dy
    if not ctx.grid.wall_between(x, y, nx, ny, h):
        return None
    edge = canonical_edge(edge_for_step(x, y, h // 6, dx, dy))
    cx, cy, local_x, local_y = WorldGrid.chunk_coords(edge.x, edge.y)
    chunk = ctx.grid.chunk(cx, cy)
    if chunk is None:
        return edge
    index = local_y * CHUNK_SIZE + local_x
    for level in sorted(
        (level for level in ctx.grid.levels.values() if (level.cx, level.cy) == (cx, cy)),
        key=lambda item: item.z,
    ):
        material_id = level.wall_n[index] if edge.direction == "north" else level.wall_w[index]
        if material_id == 0:
            continue
        flags = level.edge_flags[index]
        doorway_flag = EDGE_N_DOORWAY if edge.direction == "north" else EDGE_W_DOORWAY
        if flags & doorway_flag:
            continue
        bottom = wall_bottom(ctx, level, index)
        if bottom < h + 4 and bottom + 6 > h:
            return edge.model_copy(update={"z": level.z})
    return edge


def surface_h(grid: WorldGrid, x: int, y: int, current_h: int, *, body: bool) -> int | None:
    surfaces = grid.standing_surfaces(x, y) if body else grid.resting_surfaces(x, y)
    candidates = [surface.h for surface in surfaces if surface.h <= current_h + 1]
    return max(candidates, default=None)
