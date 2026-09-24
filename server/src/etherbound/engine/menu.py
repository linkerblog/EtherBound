from math import floor

from sqlalchemy import select
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import (
    ActorTarget,
    EdgeTarget,
    MenuEntry,
    ObjectTarget,
    SelfTarget,
    Target,
    TileTarget,
)
from etherbound.engine.objects import children, held_objects, is_accessible, worn_objects
from etherbound.engine.ops import ActionContext, handled_ops
from etherbound.engine.ops.base import metres
from etherbound.engine.payloads import MenuPayload
from etherbound.engine.world_setup import world_row
from etherbound.world.chunk import CHUNK_SIZE
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry
from etherbound.world.objects import ObjectCatalog


def build_menu(
    sessions: sessionmaker[Session],
    grid: WorldGrid,
    registry: MaterialRegistry,
    catalog: ObjectCatalog,
    load_cache: dict[str, float],
    *,
    actor_id: str,
    x: float,
    y: float,
    z: int,
) -> MenuPayload:
    """Generated right-click entries. A read: no lock, since submit validates again."""
    tile_x, tile_y = floor(x), floor(y)
    surfaces = grid.standing_surfaces(tile_x, tile_y)
    visible = [surface for surface in surfaces if surface.z == z] or list(surfaces)
    chosen = None
    if visible:
        chosen = min(visible, key=lambda item: abs(item.z - z))
        material = registry.get(chosen.material_id)
        name = material.name if material is not None else "unknown"
        label = f"{name} · {metres(chosen.h)}"
    else:
        label = "nothing"
    surface_z = chosen.z if chosen is not None else z
    entries: list[MenuEntry] = []
    with sessions() as session:
        world = world_row(session)
        actor = session.get(Actor, actor_id)
        if actor is None:
            raise KeyError(f"unknown actor: {actor_id}")
        candidates: list[Target] = []
        if chosen is not None:
            candidates.append(TileTarget(x=tile_x, y=tile_y, h=chosen.h))
        on_own_tile = (floor(actor.x), floor(actor.y)) == (tile_x, tile_y)
        if on_own_tile:
            candidates.append(SelfTarget())
        tile_rows = [
            row
            for row in session.scalars(
                select(ObjectRow)
                .where(ObjectRow.loc == "tile", ObjectRow.x == tile_x, ObjectRow.y == tile_y)
                .order_by(ObjectRow.id)
            )
            if row.h is not None and row.h // 6 == surface_z
        ]
        candidates.extend(ObjectTarget(id=row.id) for row in tile_rows)
        cx, cy, local_x, local_y = grid.chunk_coords(tile_x, tile_y)
        cell_index = local_y * CHUNK_SIZE + local_x
        for level in sorted(grid.levels.values(), key=lambda item: item.z):
            if (level.cx, level.cy) != (cx, cy):
                continue
            if level.wall_n[cell_index]:
                candidates.append(EdgeTarget(x=tile_x, y=tile_y, z=level.z, direction="north"))
            if level.wall_w[cell_index]:
                candidates.append(EdgeTarget(x=tile_x, y=tile_y, z=level.z, direction="west"))
        candidates.extend(
            ActorTarget(id=other.id)
            for other in session.scalars(select(Actor).order_by(Actor.id))
            if other.id != actor.id
            and (floor(other.x), floor(other.y), other.h)
            == (tile_x, tile_y, chosen.h if chosen else actor.h)
        )
        for row in tile_rows:
            kind = catalog.get(row.kind)
            if kind is not None and is_accessible(kind, row):
                candidates.extend(ObjectTarget(id=child.id) for child in children(session, row.id))
        if on_own_tile:
            carried = held_objects(session, actor_id) + worn_objects(session, actor_id)
            candidates.extend(ObjectTarget(id=row.id) for row in carried)
            for row in worn_objects(session, actor_id):
                kind = catalog.get(row.kind)
                if kind is not None and is_accessible(kind, row):
                    candidates.extend(
                        ObjectTarget(id=child.id) for child in children(session, row.id)
                    )
        ctx = ActionContext(session, world, actor, grid, 0, load_cache.get(actor_id, 0.0))
        for spec, handler in handled_ops():
            for target in candidates:
                if target.kind not in spec.targets or not handler.applies(ctx, target):
                    continue
                for action in handler.builds(ctx, target):
                    reason = handler.validate(ctx, action)
                    entries.append(
                        MenuEntry(
                            op=spec.key,
                            label=spec.label,
                            tags=list(spec.tags),
                            available=reason is None,
                            reason=reason,
                            subject=handler.subject(ctx, action),
                            action=action,
                        )
                    )
        session.rollback()
    return MenuPayload(x=x, y=y, z=z, target=label, entries=tuple(entries))
