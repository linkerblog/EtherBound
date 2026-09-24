from math import floor
from typing import Any

from sqlalchemy import select

from etherbound.db.models import Actor
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import PhysicsPosition
from etherbound.engine.objects import refresh_chunk_objects, set_tile
from etherbound.engine.ops.base import ActionContext
from etherbound.engine.ops.damage import damage_object, damage_wall
from etherbound.engine.ops.edges import surface_h, wall_blocking_step
from etherbound.engine.physics import MAX_TILE_STEPS, potential_energy, travel_energy_cost
from etherbound.events.models import Event, Impact
from etherbound.world.chunk import CHUNK_SIZE
from etherbound.world.grid import WorldGrid


def travel(
    ctx: ActionContext,
    op: str,
    mover: ObjectRow | Actor,
    dx: int,
    dy: int,
    energy: float,
    mass: float,
    events: list[Event],
    damage: list[dict[str, Any]],
    broken: list[dict[str, Any]],
    changed_chunks: set[tuple[int, int]],
) -> list[PhysicsPosition]:
    is_body = isinstance(mover, Actor)
    if is_body:
        x, y, h = floor(mover.x), floor(mover.y), mover.h
        entity_kind, entity_id = "actor", mover.id
    else:
        assert mover.x is not None and mover.y is not None and mover.h is not None
        x, y, h = mover.x, mover.y, mover.h
        entity_kind, entity_id = "object", mover.id
    path = [PhysicsPosition(kind=entity_kind, id=entity_id, x=x, y=y, h=h)]
    for _ in range(MAX_TILE_STEPS):
        cost = travel_energy_cost(mass)
        if energy < cost:
            break
        nx, ny = x + dx, y + dy
        wall = wall_blocking_step(ctx, x, y, h, dx, dy)
        if wall is not None:
            energy = damage_wall(ctx, op, wall, energy, events, damage, broken, changed_chunks)
            if energy <= 0:
                break
            if wall_blocking_step(ctx, x, y, h, dx, dy) is not None:
                break
        obstacle = solid_object_at(ctx, nx, ny, h, mover)
        if obstacle is not None:
            energy = damage_object(
                ctx, op, obstacle, energy, events, damage, broken, changed_chunks
            )
            if energy <= 0:
                break
            if solid_object_at(ctx, nx, ny, h, mover) is not None:
                break
        other_actor = actor_at(ctx, nx, ny, h, mover)
        if other_actor is not None:
            events.append(
                Impact(
                    actor_id=ctx.actor.id,
                    target={"kind": "actor", "id": other_actor.id},
                    energy=energy,
                )
            )
            damage.append({"target": {"kind": "actor", "id": other_actor.id}, "impact_j": energy})
            break
        mover_height = 3 if is_body else ctx.grid.catalog[mover.kind].height
        if terrain_blocks(ctx.grid, nx, ny, h, mover_height):
            events.append(
                Impact(
                    actor_id=ctx.actor.id,
                    target={"kind": "terrain", "x": nx, "y": ny, "h": h},
                    energy=energy,
                )
            )
            damage.append(
                {"target": {"kind": "terrain", "x": nx, "y": ny, "h": h}, "impact_j": energy}
            )
            break
        next_h = surface_h(ctx.grid, nx, ny, h, body=is_body)
        if next_h is None:
            break
        fall_height_m = max(0.0, (h - next_h) * 0.5)
        x, y, h = nx, ny, next_h
        path.append(PhysicsPosition(kind=entity_kind, id=entity_id, x=x, y=y, h=h))
        energy -= cost
        if fall_height_m:
            fall_energy = potential_energy(mass, fall_height_m)
            if not is_body or fall_height_m > 3.0:
                events.append(
                    Impact(
                        actor_id=ctx.actor.id,
                        target={"kind": entity_kind, "id": entity_id},
                        energy=fall_energy,
                    )
                )
                damage.append(
                    {"target": {"kind": entity_kind, "id": entity_id}, "fall_j": fall_energy}
                )
                if not is_body:
                    assert isinstance(mover, ObjectRow)
                    set_tile(mover, x, y, h)
                    ctx.session.flush()
                    changed_chunks.add((x // CHUNK_SIZE, y // CHUNK_SIZE))
                    refresh_chunk_objects(ctx, x // CHUNK_SIZE, y // CHUNK_SIZE)
                    damage_object(
                        ctx,
                        op,
                        mover,
                        fall_energy,
                        events,
                        damage,
                        broken,
                        changed_chunks,
                        emit_impact=False,
                    )
        if energy <= 0:
            break
    return path


def terrain_blocks(grid: WorldGrid, x: int, y: int, h: int, height: int) -> bool:
    return any(grid.terrain_solid_at(x, y, h + offset) for offset in range(1, height + 1))


def solid_object_at(
    ctx: ActionContext, x: int, y: int, h: int, mover: ObjectRow | Actor
) -> ObjectRow | None:
    for row in ctx.session.scalars(
        select(ObjectRow)
        .where(ObjectRow.loc == "tile", ObjectRow.x == x, ObjectRow.y == y)
        .order_by(ObjectRow.id)
    ):
        if isinstance(mover, ObjectRow) and row.id == mover.id:
            continue
        kind = ctx.grid.catalog[row.kind]
        mover_height = (
            3 if isinstance(mover, Actor) else max(ctx.grid.catalog[mover.kind].height, 1)
        )
        if (
            kind.solid
            and row.h is not None
            and row.h < h + mover_height
            and row.h + kind.height > h
        ):
            return row
    return None


def actor_at(ctx: ActionContext, x: int, y: int, h: int, mover: ObjectRow | Actor) -> Actor | None:
    for actor in ctx.session.scalars(select(Actor).order_by(Actor.id)):
        if isinstance(mover, Actor) and actor.id == mover.id:
            continue
        if (floor(actor.x), floor(actor.y), actor.h) == (x, y, h):
            return actor
    return None
