"""Object-row helpers shared by the handling handlers (docs/Dev-012.md [Sec. 5])."""

from __future__ import annotations

from dataclasses import replace
from typing import TYPE_CHECKING

from sqlalchemy import select
from sqlalchemy.orm import Session

from etherbound.db.models import Chunk as ChunkRow
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import HeldLoc, InLoc, Location, TileLoc, WornLoc
from etherbound.events.models import ChunkChanged
from etherbound.world.chunk import CHUNK_SIZE
from etherbound.world.grid import TileObject
from etherbound.world.objects import ObjectCatalog, ObjectKind

if TYPE_CHECKING:
    from etherbound.engine.ops.base import ActionContext

MAX_CONTENT_DEPTH = 8


def children(session: Session, container_id: int) -> list[ObjectRow]:
    return list(
        session.scalars(
            select(ObjectRow)
            .where(ObjectRow.loc == "in", ObjectRow.container_id == container_id)
            .order_by(ObjectRow.id)
        )
    )


def object_total_mass(
    session: Session, catalog: ObjectCatalog, obj: ObjectRow, depth: int = 0
) -> float:
    """Kilograms of an object and everything inside it, recursively (depth cap 8)."""
    kind: ObjectKind = catalog[obj.kind]
    contents = 0.0
    if kind.container is not None and depth < MAX_CONTENT_DEPTH:
        for child in children(session, obj.id):
            contents += object_total_mass(session, catalog, child, depth + 1)
    return kind.mass * obj.quantity + contents


def object_bulk(session: Session, catalog: ObjectCatalog, obj: ObjectRow) -> float:
    return catalog[obj.kind].bulk * obj.quantity


def container_contents_bulk(session: Session, catalog: ObjectCatalog, container_id: int) -> float:
    return sum(object_bulk(session, catalog, child) for child in children(session, container_id))


def actor_load_kg(session: Session, catalog: ObjectCatalog, actor_id: str) -> float:
    total = 0.0
    rows = session.scalars(
        select(ObjectRow).where(ObjectRow.actor_id == actor_id).order_by(ObjectRow.id)
    )
    for row in rows:
        total += object_total_mass(session, catalog, row)
    return total


def is_open(obj: ObjectRow) -> bool:
    return bool((obj.state or {}).get("open", False))


def has_lid(kind: ObjectKind) -> bool:
    return kind.openable


def is_accessible(kind: ObjectKind, obj: ObjectRow) -> bool:
    """Contents reachable: an open container, or a container without a lid at all."""
    return kind.container is not None and (not kind.openable or is_open(obj))


def location_of(obj: ObjectRow) -> Location:
    if obj.loc == "tile":
        assert obj.x is not None and obj.y is not None and obj.h is not None
        return TileLoc(x=obj.x, y=obj.y, h=obj.h)
    if obj.loc == "in":
        assert obj.container_id is not None
        return InLoc(object_id=obj.container_id)
    if obj.loc == "held":
        assert obj.actor_id is not None and obj.slot is not None
        return HeldLoc(actor_id=obj.actor_id, hand=obj.slot)  # type: ignore[arg-type]
    assert obj.actor_id is not None and obj.slot is not None
    return WornLoc(actor_id=obj.actor_id, slot=obj.slot)  # type: ignore[arg-type]


def set_tile(obj: ObjectRow, x: int, y: int, h: int) -> None:
    obj.loc = "tile"
    obj.x, obj.y, obj.h = x, y, h
    obj.cx, obj.cy = x // CHUNK_SIZE, y // CHUNK_SIZE
    obj.container_id = None
    obj.actor_id = None
    obj.slot = None


def set_in(obj: ObjectRow, container_id: int) -> None:
    obj.loc = "in"
    obj.x = obj.y = obj.h = obj.cx = obj.cy = None
    obj.container_id = container_id
    obj.actor_id = None
    obj.slot = None


def set_held(obj: ObjectRow, actor_id: str, hand: str) -> None:
    obj.loc = "held"
    obj.x = obj.y = obj.h = obj.cx = obj.cy = None
    obj.container_id = None
    obj.actor_id = actor_id
    obj.slot = hand


def set_worn(obj: ObjectRow, actor_id: str, slot: str) -> None:
    obj.loc = "worn"
    obj.x = obj.y = obj.h = obj.cx = obj.cy = None
    obj.container_id = None
    obj.actor_id = actor_id
    obj.slot = slot


def held_objects(session: Session, actor_id: str) -> list[ObjectRow]:
    return list(
        session.scalars(
            select(ObjectRow)
            .where(ObjectRow.loc == "held", ObjectRow.actor_id == actor_id)
            .order_by(ObjectRow.id)
        )
    )


def worn_objects(session: Session, actor_id: str) -> list[ObjectRow]:
    return list(
        session.scalars(
            select(ObjectRow)
            .where(ObjectRow.loc == "worn", ObjectRow.actor_id == actor_id)
            .order_by(ObjectRow.id)
        )
    )


def free_hands(session: Session, actor_id: str) -> list[str]:
    used = {row.slot for row in held_objects(session, actor_id)}
    return [hand for hand in ("right", "left") if hand not in used]


def tile_objects(session: Session, catalog: ObjectCatalog, cx: int, cy: int) -> list[TileObject]:
    rows = session.scalars(
        select(ObjectRow)
        .where(ObjectRow.loc == "tile", ObjectRow.cx == cx, ObjectRow.cy == cy)
        .order_by(ObjectRow.id)
    )
    result: list[TileObject] = []
    for row in rows:
        kind = catalog.get(row.kind)
        open_value = bool((row.state or {}).get("open", False)) if kind and kind.openable else None
        assert row.x is not None and row.y is not None and row.h is not None
        result.append(
            TileObject(
                id=row.id,
                kind=row.kind,
                x=row.x,
                y=row.y,
                h=row.h,
                quantity=row.quantity,
                open=open_value,
            )
        )
    return result


def refresh_chunk_objects(ctx: ActionContext, cx: int, cy: int) -> None:
    ctx.grid.set_chunk_objects(cx, cy, tile_objects(ctx.session, ctx.grid.catalog, cx, cy))


def bump_chunk(ctx: ActionContext, cx: int, cy: int) -> ChunkChanged:
    chunk = ctx.grid.chunk(cx, cy)
    if chunk is None:
        raise KeyError(f"no chunk at {cx},{cy}")
    bumped = replace(chunk, revision=chunk.revision + 1)
    ctx.grid.add_chunk(bumped)
    row = ctx.session.get(ChunkRow, (cx, cy))
    if row is not None:
        row.revision = bumped.revision
    refresh_chunk_objects(ctx, cx, cy)
    return ChunkChanged(cx=cx, cy=cy, revision=bumped.revision)


def chunk_of_location(ctx: ActionContext, loc: Location) -> tuple[int, int] | None:
    if isinstance(loc, TileLoc):
        return loc.x // CHUNK_SIZE, loc.y // CHUNK_SIZE
    if isinstance(loc, InLoc):
        container = ctx.session.get(ObjectRow, loc.object_id)
        if container is not None and container.loc == "tile":
            assert container.cx is not None and container.cy is not None
            return container.cx, container.cy
    return None


def object_at_cell(ctx: ActionContext, x: int, y: int, h: int) -> list[ObjectRow]:
    """Tile objects resting exactly at ``h`` on a tile, in id order."""
    return list(
        ctx.session.scalars(
            select(ObjectRow)
            .where(
                ObjectRow.loc == "tile",
                ObjectRow.x == x,
                ObjectRow.y == y,
                ObjectRow.h == h,
            )
            .order_by(ObjectRow.id)
        )
    )
