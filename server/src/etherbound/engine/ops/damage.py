from dataclasses import replace
from typing import Any

from etherbound.db.models import ChunkLevel as ChunkLevelRow
from etherbound.db.models import Object as ObjectRow
from etherbound.db.models import WallIntegrity
from etherbound.engine.actions import EdgeTarget
from etherbound.engine.objects import children, location_of, refresh_chunk_objects, set_tile
from etherbound.engine.ops.base import ActionContext
from etherbound.engine.ops.edges import canonical_edge, edge_cell
from etherbound.engine.physics import absorb_energy, integrity_capacity
from etherbound.events.models import Event, Impact, ObjectChanged, ObjectMoved
from etherbound.world.chunk import CHUNK_SIZE


def damage_object(
    ctx: ActionContext,
    op: str,
    row: ObjectRow,
    energy: float,
    events: list[Event],
    damage: list[dict[str, Any]],
    broken: list[dict[str, Any]],
    changed_chunks: set[tuple[int, int]],
    *,
    emit_impact: bool = True,
) -> float:
    kind = ctx.grid.catalog[row.kind]
    material = ctx.grid.registry[kind.material]
    capacity = integrity_capacity(material.resistance, kind.height) * row.quantity
    remaining = row.integrity if row.integrity is not None else capacity
    absorbed, left = absorb_energy(energy, remaining)
    if emit_impact:
        events.append(
            Impact(actor_id=ctx.actor.id, target={"kind": "object", "id": row.id}, energy=energy)
        )
    record: dict[str, Any] = {
        "target": {"kind": "object", "id": row.id},
        "absorbed_j": absorbed,
        "remaining_j": max(0.0, remaining - absorbed),
    }
    if left > 0 or energy >= remaining:
        assert row.loc == "tile" and row.x is not None and row.y is not None and row.h is not None
        x, y, h = row.x, row.y, row.h
        if row.kind == "rubble":
            ctx.session.delete(row)
            rubble_id = None
            events.append(
                ObjectChanged(
                    actor_id=ctx.actor.id,
                    object_id=row.id,
                    kind=row.kind,
                    op=op,
                    changes={"removed": True},
                )
            )
        else:
            contents = children(ctx.session, row.id)
            for child in contents:
                old = location_of(child)
                set_tile(child, x, y, h)
                events.append(
                    ObjectMoved(
                        actor_id=ctx.actor.id,
                        object_id=child.id,
                        kind=child.kind,
                        quantity=child.quantity,
                        op=op,
                        from_=old,
                        to=location_of(child),
                    )
                )
            row.kind = "rubble"
            row.state = {}
            row.integrity = None
            rubble_id = row.id
            events.append(
                ObjectChanged(
                    actor_id=ctx.actor.id,
                    object_id=row.id,
                    kind="rubble",
                    op=op,
                    changes={"kind": "rubble", "integrity": None},
                )
            )
        broken.append({"kind": "object", "id": row.id, "rubble_id": rubble_id})
        record["remaining_j"] = 0.0
        changed_chunks.add((x // CHUNK_SIZE, y // CHUNK_SIZE))
        ctx.session.flush()
        refresh_chunk_objects(ctx, x // CHUNK_SIZE, y // CHUNK_SIZE)
    else:
        row.integrity = remaining - absorbed
        events.append(
            ObjectChanged(
                actor_id=ctx.actor.id,
                object_id=row.id,
                kind=row.kind,
                op=op,
                changes={"integrity": row.integrity},
            )
        )
        if row.x is not None and row.y is not None:
            changed_chunks.add((row.x // CHUNK_SIZE, row.y // CHUNK_SIZE))
    damage.append(record)
    return left


def damage_wall(
    ctx: ActionContext,
    op: str,
    target: EdgeTarget,
    energy: float,
    events: list[Event],
    damage: list[dict[str, Any]],
    broken: list[dict[str, Any]],
    changed_chunks: set[tuple[int, int]],
) -> float:
    edge = canonical_edge(target)
    record = edge_cell(ctx, edge)
    if record is None:
        return 0.0
    level, index, material_id = record
    material = ctx.grid.registry.get(material_id)
    if material is None:
        return 0.0
    key = (level.cx, level.cy, level.z, index, edge.direction)
    row = ctx.session.get(WallIntegrity, key)
    capacity = integrity_capacity(material.resistance, 6)
    remaining = row.integrity if row is not None else capacity
    absorbed, left = absorb_energy(energy, remaining)
    events.append(
        Impact(
            actor_id=ctx.actor.id,
            target=edge.model_dump(mode="json"),
            energy=energy,
        )
    )
    data = {
        "target": edge.model_dump(mode="json"),
        "absorbed_j": absorbed,
        "remaining_j": max(0.0, remaining - absorbed),
    }
    if energy >= remaining:
        if row is not None:
            ctx.session.delete(row)
        walls = list(level.wall_n if edge.direction == "north" else level.wall_w)
        walls[index] = 0
        updated = replace(
            level,
            wall_n=tuple(walls) if edge.direction == "north" else level.wall_n,
            wall_w=tuple(walls) if edge.direction == "west" else level.wall_w,
        )
        ctx.grid.add_level(updated)
        db_level = ctx.session.get(ChunkLevelRow, (level.cx, level.cy, level.z))
        if db_level is not None:
            db_level.wall_n = updated.wall_n_blob
            db_level.wall_w = updated.wall_w_blob
        broken.append({"kind": "wall", **edge.model_dump(mode="json")})
        data["remaining_j"] = 0.0
        changed_chunks.add((level.cx, level.cy))
    else:
        if row is None:
            row = WallIntegrity(
                cx=level.cx,
                cy=level.cy,
                z=level.z,
                cell_index=index,
                edge=edge.direction,
                integrity=remaining - absorbed,
            )
            ctx.session.add(row)
        else:
            row.integrity = remaining - absorbed
    damage.append(data)
    return left
