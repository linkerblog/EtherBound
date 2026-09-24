import logging
from math import floor
from typing import Any, Literal

from pydantic import ValidationError
from sqlalchemy import select
from sqlalchemy.orm import Session

from etherbound.db.models import Actor, WorldMeta
from etherbound.db.models import Chunk as ChunkRow
from etherbound.db.models import ChunkLevel as ChunkLevelRow
from etherbound.db.models import Material as MaterialRow
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import PLAYER_ID, Mind, Spot
from etherbound.engine.movement import nearest_surface
from etherbound.events.models import ActorSpawned, Event, TilePos
from etherbound.world.chunk import CHUNK_SIZE, Chunk, ChunkLevel
from etherbound.world.gen.registry import DEFAULT_GENERATOR, GeneratorSpec, get_generator
from etherbound.world.gen.types import GeneratedObject, GeneratedWorld
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry
from etherbound.world.population import GeneratedActor, populate

logger = logging.getLogger("etherbound.engine")


def sync_materials(session: Session, registry: MaterialRegistry) -> None:
    existing_rows = list(session.scalars(select(MaterialRow)))
    existing = {row.key: row.id for row in existing_rows}
    rows_by_key = {row.key: row for row in existing_rows}
    if existing and any(key not in registry.ids() for key in existing):
        missing = sorted(key for key in existing if key not in registry.ids())
        raise RuntimeError(f"saved materials missing from materials.toml: {', '.join(missing)}")
    for material in registry:
        if material.key in existing:
            rows_by_key[material.key].resistance = material.resistance
            continue
        session.add(
            MaterialRow(
                id=material.id,
                key=material.key,
                name=material.name,
                color=material.color,
                walkable=material.walkable,
                walk_cost=material.walk_cost,
                solid=material.solid,
                blocks_sight=material.blocks_sight,
                diggable=material.diggable,
                dig_cost=material.dig_cost,
                flammable=material.flammable,
                density=material.density,
                resistance=material.resistance,
                liquid=material.liquid,
                tags=list(material.tags),
            )
        )


def world_row(session: Session) -> WorldMeta:
    world = session.get(WorldMeta, 1)
    if world is None:
        raise RuntimeError("world has not been initialized")
    return world


def load_grid(session: Session) -> tuple[list[Chunk], list[ChunkLevel]]:
    chunks = [
        Chunk.from_blobs(
            row.cx,
            row.cy,
            row.ground_h,
            row.surface_mat,
            tuple((int(depth), str(key)) for depth, key in row.strata),
            row.revision,
            row.gen_version,
            row.dug,
        )
        for row in session.scalars(select(ChunkRow).order_by(ChunkRow.cx, ChunkRow.cy))
    ]
    levels = [
        ChunkLevel.from_blobs(
            row.cx,
            row.cy,
            row.z,
            row.floor_h,
            row.floor_mat,
            row.wall_n,
            row.wall_w,
            row.edge_flags,
            row.flags,
        )
        for row in session.scalars(
            select(ChunkLevelRow).order_by(ChunkLevelRow.cx, ChunkLevelRow.cy, ChunkLevelRow.z)
        )
    ]
    return chunks, levels


def commit_world(session: Session, world: GeneratedWorld) -> None:
    for chunk in world.chunks.values():
        session.add(
            ChunkRow(
                cx=chunk.cx,
                cy=chunk.cy,
                ground_h=chunk.ground_blob,
                surface_mat=chunk.surface_blob,
                strata=[list(entry) for entry in chunk.strata],
                revision=chunk.revision,
                gen_version=chunk.gen_version,
                dug=chunk.dug_blob,
            )
        )
    for level in world.levels.values():
        session.add(
            ChunkLevelRow(
                cx=level.cx,
                cy=level.cy,
                z=level.z,
                floor_h=level.floor_blob,
                floor_mat=level.floor_mat_blob,
                wall_n=level.wall_n_blob,
                wall_w=level.wall_w_blob,
                edge_flags=level.edge_flags_blob,
                flags=level.flags_blob,
            )
        )


def has_chunks(session: Session) -> bool:
    return session.scalar(select(ChunkRow.cx).limit(1)) is not None


def delete_uncarried_objects(session: Session) -> None:
    """Keep held and worn objects and everything inside them; drop the rest."""
    keep: set[int] = {
        row.id
        for row in session.scalars(select(ObjectRow).where(ObjectRow.loc.in_(("held", "worn"))))
    }
    frontier = set(keep)
    while frontier:
        children = list(
            session.scalars(
                select(ObjectRow).where(ObjectRow.loc == "in", ObjectRow.container_id.in_(frontier))
            )
        )
        frontier = {child.id for child in children} - keep
        keep |= frontier
    for row in session.scalars(select(ObjectRow).order_by(ObjectRow.id.desc())):
        if row.id not in keep:
            session.delete(row)


def commit_objects(session: Session, objects: tuple[GeneratedObject, ...]) -> None:
    ids: list[int] = []
    for obj in objects:
        row = ObjectRow(
            kind=obj.kind,
            loc="in" if obj.parent is not None else "tile",
            quantity=obj.quantity,
            state={"open": obj.open} if obj.open else {},
        )
        if obj.parent is not None:
            row.container_id = ids[obj.parent]
        else:
            row.x, row.y, row.h = obj.x, obj.y, obj.h
            row.cx, row.cy = obj.x // CHUNK_SIZE, obj.y // CHUNK_SIZE
        session.add(row)
        session.flush()
        ids.append(row.id)


def resolve_generator(meta: WorldMeta) -> tuple[GeneratorSpec, Any, bool]:
    """Return the save's generator, its validated options and whether it fell back."""
    fallback = False
    spec = get_generator(meta.generator or DEFAULT_GENERATOR)
    if spec is None:
        logger.warning(
            "unknown generator %r in save; falling back to %s",
            meta.generator,
            DEFAULT_GENERATOR,
        )
        spec = get_generator(DEFAULT_GENERATOR)
        assert spec is not None
        meta.generator = spec.key
        meta.gen_options = {}
        fallback = True
    assert spec is not None
    try:
        options = spec.options.model_validate(meta.gen_options or {})
    except ValidationError:
        logger.warning("stored options for generator %r are invalid; using defaults", spec.key)
        options = spec.options()
        fallback = True
    return spec, options, fallback


def spawn_point(
    grid: WorldGrid, registry: MaterialRegistry, spec: GeneratorSpec, options: Any, seed: int
) -> tuple[float, float, int]:
    spawn = spec.spawn(seed, options)
    if standing_h_near(grid, registry, spawn[0], spawn[1]) is not None:
        return spawn
    for cx, cy in sorted(grid.chunks):
        for local_y in range(CHUNK_SIZE):
            for local_x in range(CHUNK_SIZE):
                world_x = cx * CHUNK_SIZE + local_x
                world_y = cy * CHUNK_SIZE + local_y
                standing = standing_h_near(grid, registry, world_x + 0.5, world_y + 0.5)
                if standing is not None:
                    return world_x + 0.5, world_y + 0.5, standing
    return 0.5, 0.5, 0


def standing_h_near(grid: WorldGrid, registry: MaterialRegistry, x: float, y: float) -> int | None:
    surfaces = grid.standing_surfaces(floor(x), floor(y))
    if not surfaces:
        return None
    material = registry.get(surfaces[0].material_id)
    if material is None or not material.walkable:
        return None
    return surfaces[0].h


def spawn_event(actor: Actor, reason: Literal["created", "relocated"]) -> ActorSpawned:
    return ActorSpawned(
        actor_id=actor.id,
        kind=actor.kind,
        tile=TilePos(x=floor(actor.x), y=floor(actor.y), h=actor.h),
        reason=reason,
        name=actor.name,
    )


def settle_actor(
    grid: WorldGrid, actor: Actor, spawn_x: float, spawn_y: float, spawn_h: int
) -> ActorSpawned | None:
    """Snap a loaded actor to its surface, relocate it to spawn if it has none."""
    surface = nearest_surface(grid, actor.x, actor.y, actor.h)
    if surface is None:
        actor.x, actor.y, actor.h, actor.z = spawn_x, spawn_y, spawn_h, spawn_h // 6
        return spawn_event(actor, "relocated")
    if surface.h != actor.h:
        # A data repair, not a world fact: no event. The standing rule needs the exact h,
        # and the next actor.moved carries the corrected h in from_tile.
        actor.h, actor.z = surface.h, surface.h // 6
    return None


def create_extras(
    session: Session,
    grid: WorldGrid,
    registry: MaterialRegistry,
    seed: int,
    spawn: tuple[float, float, int],
) -> list[Event]:
    events: list[Event] = []
    for generated in populate(grid, registry, spawn, seed):
        commit_extra(session, generated)
        events.append(
            ActorSpawned(
                actor_id=generated.id,
                kind="extra",
                tile=TilePos(x=generated.anchor_x, y=generated.anchor_y, h=generated.anchor_h),
                reason="created",
                name=generated.name,
            )
        )
    return events


def commit_extra(session: Session, generated: GeneratedActor) -> None:
    actor = Actor(
        id=generated.id,
        kind="extra",
        name=generated.name,
        x=generated.x,
        y=generated.y,
        h=generated.h,
        z=generated.h // 6,
    )
    actor.mind = Mind(
        anchor=Spot(x=generated.anchor_x, y=generated.anchor_y, h=generated.anchor_h)
    ).model_dump(mode="json")
    session.add(actor)


def ensure_actors(
    session: Session,
    grid: WorldGrid,
    registry: MaterialRegistry,
    spec: GeneratorSpec,
    options: Any,
    seed: int,
) -> list[Event]:
    """Create or settle Niko and the Extras, emitting one actor.spawned per change."""
    spawn_x, spawn_y, spawn_h = spawn_point(grid, registry, spec, options, seed)
    events: list[Event] = []
    niko = session.get(Actor, PLAYER_ID)
    if niko is None:
        niko = Actor(
            id=PLAYER_ID,
            kind="player",
            x=spawn_x,
            y=spawn_y,
            h=spawn_h,
            z=spawn_h // 6,
            mass_kg=80.0,
        )
        session.add(niko)
        session.flush()
        events.append(spawn_event(niko, "created"))
    else:
        settled = settle_actor(grid, niko, spawn_x, spawn_y, spawn_h)
        if settled is not None:
            events.append(settled)
    extras = list(session.scalars(select(Actor).where(Actor.kind == "extra").order_by(Actor.id)))
    if extras:
        for extra in extras:
            settled = settle_actor(grid, extra, spawn_x, spawn_y, spawn_h)
            if settled is not None:
                events.append(settled)
    else:
        events.extend(create_extras(session, grid, registry, seed, (spawn_x, spawn_y, spawn_h)))
    return events
