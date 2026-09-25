from math import floor

from sqlalchemy import select
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import (
    ActorTarget,
    EdgeTarget,
    MenuEntry,
    MenuPlace,
    ObjectTarget,
    SelfTarget,
    Target,
    TileTarget,
)
from etherbound.engine.objects import children, held_objects, is_accessible, worn_objects
from etherbound.engine.ops import ActionContext, handled_ops
from etherbound.engine.ops.base import in_close_reach, metres
from etherbound.engine.payloads import MenuPayload
from etherbound.engine.world_setup import world_row
from etherbound.world.chunk import CHUNK_SIZE
from etherbound.world.grid import StandingSurface, WorldGrid
from etherbound.world.materials import MaterialRegistry
from etherbound.world.objects import ObjectCatalog

ORTHOGONAL_STEPS = ((1, 0), (-1, 0), (0, 1), (0, -1))
DIAGONAL_STEPS = ((1, 1), (1, -1), (-1, 1), (-1, -1))


def _standing_surface(grid: WorldGrid, x: int, y: int, z: int) -> StandingSurface | None:
    surfaces = grid.standing_surfaces(x, y)
    visible = [surface for surface in surfaces if surface.z == z] or list(surfaces)
    return min(visible, key=lambda item: abs(item.z - z)) if visible else None


def _place(
    grid: WorldGrid, registry: MaterialRegistry, x: int, y: int, z: int
) -> tuple[int, str] | None:
    """Height and material name of the surface a menu reads on this tile."""
    chosen = _standing_surface(grid, x, y, z)
    if chosen is None:
        return None
    material = registry.get(chosen.material_id)
    return chosen.h, material.name if material is not None else "unknown"


def _tile_candidates(
    session: Session,
    grid: WorldGrid,
    catalog: ObjectCatalog,
    actor: Actor,
    actors: list[Actor],
    x: int,
    y: int,
    z: int,
    *,
    on_own_tile: bool,
) -> list[Target]:
    candidates: list[Target] = []
    chosen = _standing_surface(grid, x, y, z)
    if chosen is not None:
        candidates.append(TileTarget(x=x, y=y, h=chosen.h))
    if on_own_tile:
        candidates.append(SelfTarget())
    surface_z = chosen.z if chosen is not None else z
    tile_rows = [
        row
        for row in session.scalars(
            select(ObjectRow)
            .where(ObjectRow.loc == "tile", ObjectRow.x == x, ObjectRow.y == y)
            .order_by(ObjectRow.id)
        )
        if row.h is not None and row.h // 6 == surface_z
    ]
    candidates.extend(ObjectTarget(id=row.id) for row in tile_rows)
    cx, cy, local_x, local_y = grid.chunk_coords(x, y)
    cell_index = local_y * CHUNK_SIZE + local_x
    for level in sorted(grid.levels.values(), key=lambda item: item.z):
        if (level.cx, level.cy) != (cx, cy):
            continue
        if level.wall_n[cell_index]:
            candidates.append(EdgeTarget(x=x, y=y, z=level.z, direction="north"))
        if level.wall_w[cell_index]:
            candidates.append(EdgeTarget(x=x, y=y, z=level.z, direction="west"))
    candidates.extend(
        ActorTarget(id=other.id)
        for other in actors
        if other.id != actor.id
        and (floor(other.x), floor(other.y), other.h) == (x, y, chosen.h if chosen else actor.h)
    )
    for row in tile_rows:
        kind = catalog.get(row.kind)
        if kind is not None and is_accessible(kind, row):
            candidates.extend(ObjectTarget(id=child.id) for child in children(session, row.id))
    if on_own_tile:
        carried = held_objects(session, actor.id) + worn_objects(session, actor.id)
        candidates.extend(ObjectTarget(id=row.id) for row in carried)
        for row in worn_objects(session, actor.id):
            kind = catalog.get(row.kind)
            if kind is not None and is_accessible(kind, row):
                candidates.extend(ObjectTarget(id=child.id) for child in children(session, row.id))
    return candidates


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
    radius: int = 0,
) -> MenuPayload:
    """Generated right-click entries. A read: no lock, since submit validates again."""
    tile_x, tile_y = floor(x), floor(y)
    origin = _place(grid, registry, tile_x, tile_y, z)
    label = f"{origin[1]} · {metres(origin[0])}" if origin is not None else "nothing"
    entries: list[MenuEntry] = []
    places: list[MenuPlace] = []
    with sessions() as session:
        world = world_row(session)
        actor = session.get(Actor, actor_id)
        if actor is None:
            raise KeyError(f"unknown actor: {actor_id}")
        ctx = ActionContext(session, world, actor, grid, 0, load_cache.get(actor_id, 0.0))
        actor_tile = ctx.actor_tile()
        offsets = [(0, 0)]
        if radius >= 1:
            offsets.extend(
                (dx, dy)
                for dx, dy in ORTHOGONAL_STEPS
                if in_close_reach(ctx, tile_x + dx, tile_y + dy)
            )
            offsets.extend(
                (dx, dy)
                for dx, dy in DIAGONAL_STEPS
                if in_close_reach(ctx, tile_x + dx, tile_y)
                or in_close_reach(ctx, tile_x, tile_y + dy)
            )
        actors = list(session.scalars(select(Actor).order_by(Actor.id)))
        # Each candidate keeps the offset of the tile it came from, so its entries can say where
        # they act without the client resolving object ids to tiles.
        candidates: list[tuple[Target, int, int]] = []
        for dx, dy in offsets:
            nx, ny = tile_x + dx, tile_y + dy
            place = _place(grid, registry, nx, ny, z)
            h, name = place if place is not None else (z * 6, "nothing")
            places.append(MenuPlace(dx=dx, dy=dy, h=h, label=name))
            candidates.extend(
                (target, dx, dy)
                for target in _tile_candidates(
                    session,
                    grid,
                    catalog,
                    actor,
                    actors,
                    nx,
                    ny,
                    z,
                    on_own_tile=(nx, ny) == actor_tile,
                )
            )
        for spec, handler in handled_ops():
            for target, dx, dy in candidates:
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
                            tile_dx=dx,
                            tile_dy=dy,
                        )
                    )
        session.rollback()
    return MenuPayload(x=x, y=y, z=z, target=label, entries=tuple(entries), places=tuple(places))
