import asyncio
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from math import floor

from sqlalchemy import select
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, WorldMeta
from etherbound.db.models import Chunk as ChunkRow
from etherbound.db.models import ChunkLevel as ChunkLevelRow
from etherbound.db.models import Material as MaterialRow
from etherbound.engine.actions import Action, ActionResult
from etherbound.engine.movement import move_in_world
from etherbound.world.chunk import CHUNK_SIZE, Chunk, ChunkLevel
from etherbound.world.gen.testworld import GEN_VERSION, SPAWN_POINT, TestWorld, generate_test_world
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry

PLAYER_ID = "niko"
WALKING_SPEED_METRES_PER_SECOND = 4.0
CHUNK_RADIUS = 2


@dataclass(frozen=True, slots=True)
class ActorState:
    id: str
    kind: str
    x: float
    y: float
    z: int
    h: int


@dataclass(frozen=True, slots=True)
class WorldState:
    seed: int
    game_minute: int
    speed: int
    paused: bool
    actors: tuple[ActorState, ...]
    gen_version: int


@dataclass(frozen=True, slots=True)
class ChunkLevelPayload:
    z: int
    floor_h: tuple[int, ...]
    floor_mat: tuple[int, ...]
    wall_n: tuple[int, ...]
    wall_w: tuple[int, ...]
    edge_flags: tuple[int, ...]
    flags: tuple[int, ...]


@dataclass(frozen=True, slots=True)
class ChunkPayload:
    cx: int
    cy: int
    revision: int
    ground_h: tuple[int, ...]
    surface_mat: tuple[int, ...]
    levels: tuple[ChunkLevelPayload, ...]


@dataclass(frozen=True, slots=True)
class WorldInfo:
    chunk_size: int
    level_h: int
    bounds: tuple[int, int, int, int]


class WorldEngine:
    """The only component allowed to validate and commit world state changes."""

    def __init__(
        self, sessions: sessionmaker[Session], registry: MaterialRegistry | None = None
    ) -> None:
        self.sessions = sessions
        self.registry = registry or MaterialRegistry.load()
        self.grid = WorldGrid(registry=self.registry)
        self._lock = asyncio.Lock()
        self._clock_listener: Callable[[int, int, bool], Awaitable[None] | None] | None = None

    def set_clock_listener(
        self, listener: Callable[[int, int, bool], Awaitable[None] | None]
    ) -> None:
        self._clock_listener = listener

    def _sync_materials(self, session: Session) -> None:
        existing_rows = list(session.scalars(select(MaterialRow)))
        existing = {row.key: row.id for row in existing_rows}
        if existing and any(key not in self.registry.ids() for key in existing):
            missing = sorted(key for key in existing if key not in self.registry.ids())
            raise RuntimeError(f"saved materials missing from materials.toml: {', '.join(missing)}")
        for material in self.registry:
            if material.key in existing:
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

    @staticmethod
    def _load_grid(session: Session) -> tuple[list[Chunk], list[ChunkLevel]]:
        chunks = [
            Chunk.from_blobs(
                row.cx,
                row.cy,
                row.ground_h,
                row.surface_mat,
                tuple((int(depth), str(key)) for depth, key in row.strata),
                row.revision,
                row.gen_version,
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

    @staticmethod
    def _commit_world(session: Session, world: TestWorld) -> None:
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

    def _has_chunks(self, session: Session) -> bool:
        return session.scalar(select(ChunkRow.cx).limit(1)) is not None

    def ensure_world(self, seed: int = 0) -> None:
        with self.sessions() as session:
            meta = session.get(WorldMeta, 1)
            if meta is None:
                meta = WorldMeta(
                    id=1, seed=seed, game_minute=0, speed=1, paused=False, gen_version=0
                )
                session.add(meta)
                session.flush()
            saved_ids = {row.key: row.id for row in session.scalars(select(MaterialRow))}
            if saved_ids:
                self.registry = MaterialRegistry.load(existing_ids=saved_ids)
                self.grid.registry = self.registry
            self._sync_materials(session)
            if not self._has_chunks(session):
                world = generate_test_world(meta.seed, self.registry)
                self._commit_world(session, world)
                meta.gen_version = world.gen_version
            chunks, levels = self._load_grid(session)
            session.commit()
        self.grid = WorldGrid(chunks, levels, self.registry)
        self._ensure_actor()

    def _spawn_point(self) -> tuple[float, float, int]:
        spawn_x, spawn_y = SPAWN_POINT
        spawn_h = self._standing_h_near(spawn_x, spawn_y)
        if spawn_h is not None:
            return spawn_x, spawn_y, spawn_h
        for cx, cy in sorted(self.grid.chunks):
            for local_y in range(CHUNK_SIZE):
                for local_x in range(CHUNK_SIZE):
                    world_x = cx * CHUNK_SIZE + local_x
                    world_y = cy * CHUNK_SIZE + local_y
                    standing = self._standing_h_near(world_x + 0.5, world_y + 0.5)
                    if standing is not None:
                        return world_x + 0.5, world_y + 0.5, standing
        return 0.5, 0.5, 0

    def _standing_h_near(self, x: float, y: float) -> int | None:
        surfaces = self.grid.standing_surfaces(floor(x), floor(y))
        if not surfaces:
            return None
        material = self.registry.get(surfaces[0].material_id)
        if material is None or not material.walkable:
            return None
        return surfaces[0].h

    def _stands(self, x: float, y: float, h: int) -> bool:
        return any(
            abs(surface.h - h) <= 1 for surface in self.grid.standing_surfaces(floor(x), floor(y))
        )

    def _ensure_actor(self) -> None:
        spawn_x, spawn_y, spawn_h = self._spawn_point()
        with self.sessions() as session:
            actor = session.get(Actor, PLAYER_ID)
            if actor is None:
                session.add(
                    Actor(
                        id=PLAYER_ID,
                        kind="player",
                        x=spawn_x,
                        y=spawn_y,
                        h=spawn_h,
                        z=spawn_h // 6,
                    )
                )
            elif not self._stands(actor.x, actor.y, actor.h):
                actor.x, actor.y, actor.h, actor.z = spawn_x, spawn_y, spawn_h, spawn_h // 6
            session.commit()

    def _world_row(self, session: Session) -> WorldMeta:
        world = session.get(WorldMeta, 1)
        if world is None:
            raise RuntimeError("world has not been initialized")
        return world

    def get_state(self) -> WorldState:
        with self.sessions() as session:
            world = self._world_row(session)
            actors = tuple(
                ActorState(actor.id, actor.kind, actor.x, actor.y, actor.z, actor.h)
                for actor in session.scalars(select(Actor).order_by(Actor.id))
            )
            return WorldState(
                world.seed, world.game_minute, world.speed, world.paused, actors, world.gen_version
            )

    def clock_state(self) -> tuple[int, int, bool]:
        with self.sessions() as session:
            world = self._world_row(session)
            return world.game_minute, world.speed, world.paused

    def world_info(self) -> WorldInfo:
        bounds = self.grid.bounds() or (0, 0, 0, 0)
        return WorldInfo(chunk_size=CHUNK_SIZE, level_h=6, bounds=bounds)

    def chunk_payload(self, cx: int, cy: int) -> ChunkPayload | None:
        chunk = self.grid.chunk(cx, cy)
        if chunk is None:
            return None
        levels = tuple(
            ChunkLevelPayload(
                z=level.z,
                floor_h=level.floor_h,
                floor_mat=level.floor_mat,
                wall_n=level.wall_n,
                wall_w=level.wall_w,
                edge_flags=level.edge_flags,
                flags=level.flags,
            )
            for (level_cx, level_cy, _), level in sorted(self.grid.levels.items())
            if level_cx == cx and level_cy == cy
        )
        return ChunkPayload(
            cx=cx,
            cy=cy,
            revision=chunk.revision,
            ground_h=chunk.ground_h,
            surface_mat=chunk.surface_mat,
            levels=levels,
        )

    def chunks_near(self, cx: int, cy: int, radius: int = CHUNK_RADIUS) -> tuple[ChunkPayload, ...]:
        payloads: list[ChunkPayload] = []
        for offset_y in range(-radius, radius + 1):
            for offset_x in range(-radius, radius + 1):
                payload = self.chunk_payload(cx + offset_x, cy + offset_y)
                if payload is not None:
                    payloads.append(payload)
        return tuple(payloads)

    async def new_game(self, seed: int) -> WorldState:
        async with self._lock:
            with self.sessions() as session:
                session.query(ChunkLevelRow).delete()
                session.query(ChunkRow).delete()
                session.query(Actor).delete()
                world = self._world_row(session)
                world.seed = seed
                world.game_minute = 0
                world.speed = 1
                world.paused = False
                generated = generate_test_world(seed, self.registry)
                self._commit_world(session, generated)
                world.gen_version = GEN_VERSION
                session.commit()
            self.grid = WorldGrid(
                generated.chunks.values(), generated.levels.values(), self.registry
            )
            self._ensure_actor()
            state = self.get_state()
        if self._clock_listener is not None:
            result = self._clock_listener(state.game_minute, state.speed, state.paused)
            if asyncio.iscoroutine(result):
                await result
        return state

    async def submit(
        self, actor_id: str, action: Action, delta_seconds: float = 1 / 20
    ) -> ActionResult:
        async with self._lock:
            with self.sessions() as session:
                world = self._world_row(session)
                actor = session.get(Actor, actor_id)
                if actor is None:
                    raise KeyError(f"unknown actor: {actor_id}")
                if world.paused:
                    return ActionResult(
                        accepted=False,
                        actor_id=actor_id,
                        action=action,
                        x=actor.x,
                        y=actor.y,
                        z=actor.z,
                        h=actor.h,
                        reason="paused",
                    )
                actor.x, actor.y, actor.h = move_in_world(
                    actor.x,
                    actor.y,
                    actor.h,
                    action.dx,
                    action.dy,
                    WALKING_SPEED_METRES_PER_SECOND * delta_seconds,
                    self.grid,
                )
                actor.z = actor.h // 6
                session.commit()
                return ActionResult(
                    accepted=True,
                    actor_id=actor_id,
                    action=action,
                    x=actor.x,
                    y=actor.y,
                    z=actor.z,
                    h=actor.h,
                )

    async def advance_time(self) -> WorldState:
        async with self._lock:
            with self.sessions() as session:
                world = self._world_row(session)
                if not world.paused:
                    world.game_minute += 1
                    session.commit()
            state = self.get_state()
        if self._clock_listener is not None:
            result = self._clock_listener(state.game_minute, state.speed, state.paused)
            if asyncio.iscoroutine(result):
                await result
        return state

    async def set_clock(
        self, *, paused: bool | None = None, speed: int | None = None
    ) -> WorldState:
        async with self._lock:
            with self.sessions() as session:
                world = self._world_row(session)
                if paused is not None:
                    world.paused = paused
                if speed is not None:
                    if speed not in (1, 3, 10):
                        raise ValueError("speed must be 1, 3, or 10")
                    world.speed = speed
                session.commit()
            state = self.get_state()
        if self._clock_listener is not None:
            result = self._clock_listener(state.game_minute, state.speed, state.paused)
            if asyncio.iscoroutine(result):
                await result
        return state
