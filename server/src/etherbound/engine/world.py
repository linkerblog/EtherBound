import asyncio
from dataclasses import dataclass
from math import floor

from pydantic import TypeAdapter
from sqlalchemy import func, select
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, WorldMeta
from etherbound.db.models import Chunk as ChunkRow
from etherbound.db.models import ChunkLevel as ChunkLevelRow
from etherbound.db.models import Event as EventRow
from etherbound.db.models import Material as MaterialRow
from etherbound.engine.actions import (
    Action,
    ActionResult,
    ActivityState,
    MenuEntry,
    MoveAction,
    SelfTarget,
    Target,
    TileTarget,
)
from etherbound.engine.movement import nearest_surface
from etherbound.engine.ops import ActionContext, handled_ops, handler_for
from etherbound.engine.ops.base import metres
from etherbound.events.bus import EventBus
from etherbound.events.models import (
    ActivityFinished,
    ActivityStarted,
    ActorSpawned,
    ClockChanged,
    ClockTicked,
    Event,
    TilePos,
    WorldGenerated,
)
from etherbound.world.chunk import CHUNK_SIZE, Chunk, ChunkLevel
from etherbound.world.gen.testworld import GEN_VERSION, SPAWN_POINT, TestWorld, generate_test_world
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry

PLAYER_ID = "niko"
CHUNK_RADIUS = 2

action_adapter: TypeAdapter[Action] = TypeAdapter(Action)


@dataclass(frozen=True, slots=True)
class ActorState:
    id: str
    kind: str
    x: float
    y: float
    z: int
    h: int
    activity: ActivityState | None = None


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
class MenuPayload:
    x: float
    y: float
    z: int
    target: str
    entries: tuple[MenuEntry, ...]


@dataclass(frozen=True, slots=True)
class WorldInfo:
    chunk_size: int
    level_h: int
    bounds: tuple[int, int, int, int]


class WorldEngine:
    """The only component allowed to validate and commit world state changes."""

    def __init__(
        self,
        sessions: sessionmaker[Session],
        registry: MaterialRegistry | None = None,
        bus: EventBus | None = None,
    ) -> None:
        self.sessions = sessions
        self.registry = registry or MaterialRegistry.load()
        self.grid = WorldGrid(registry=self.registry)
        self._lock = asyncio.Lock()
        self.bus = bus or EventBus()
        self._next_seq = 1

    def _stamp_and_store(
        self,
        session: Session,
        world: WorldMeta,
        events: list[Event],
        *,
        first_seq: int | None = None,
    ) -> int:
        next_seq = self._next_seq if first_seq is None else first_seq
        for event in events:
            event.seq = next_seq
            event.game_minute = world.game_minute
            next_seq += 1
            if event.logged:
                session.add(
                    EventRow(
                        seq=event.seq,
                        game_minute=event.game_minute,
                        type=event.type,
                        actor_id=event.actor_id,
                        data=event.model_dump(
                            mode="json", exclude={"seq", "game_minute", "type", "actor_id"}
                        ),
                    )
                )
        return next_seq

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

    def _has_chunks(self, session: Session) -> bool:
        return session.scalar(select(ChunkRow.cx).limit(1)) is not None

    def ensure_world(self, seed: int = 0) -> None:
        events: list[Event] = []
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
            generated_world = not self._has_chunks(session)
            if generated_world:
                world = generate_test_world(meta.seed, self.registry)
                self._commit_world(session, world)
                meta.gen_version = world.gen_version
                events.append(WorldGenerated(seed=meta.seed, gen_version=world.gen_version))
            chunks, levels = self._load_grid(session)
            self.grid = WorldGrid(chunks, levels, self.registry)
            actor_event = self._ensure_actor(session)
            if actor_event is not None:
                events.append(actor_event)
            if generated_world:
                events.append(ClockChanged(speed=meta.speed, paused=meta.paused))
            self._next_seq = (session.scalar(select(func.max(EventRow.seq))) or 0) + 1
            next_seq = self._stamp_and_store(session, meta, events)
            session.commit()
        self._next_seq = next_seq
        self.bus.enqueue(events)

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

    def _ensure_actor(self, session: Session) -> ActorSpawned | None:
        spawn_x, spawn_y, spawn_h = self._spawn_point()
        actor = session.get(Actor, PLAYER_ID)
        reason: str | None = None
        if actor is None:
            actor = Actor(
                id=PLAYER_ID,
                kind="player",
                x=spawn_x,
                y=spawn_y,
                h=spawn_h,
                z=spawn_h // 6,
            )
            session.add(actor)
            reason = "created"
        elif (surface := nearest_surface(self.grid, actor.x, actor.y, actor.h)) is None:
            actor.x, actor.y, actor.h, actor.z = spawn_x, spawn_y, spawn_h, spawn_h // 6
            reason = "relocated"
        elif surface.h != actor.h:
            # A data repair, not a world fact: no event. The standing rule needs the exact h,
            # and the next actor.moved carries the corrected h in from_tile.
            actor.h, actor.z = surface.h, surface.h // 6
        if reason is None:
            return None
        return ActorSpawned(
            actor_id=actor.id,
            kind=actor.kind,
            tile=TilePos(x=floor(actor.x), y=floor(actor.y), h=actor.h),
            reason=reason,
        )

    def _world_row(self, session: Session) -> WorldMeta:
        world = session.get(WorldMeta, 1)
        if world is None:
            raise RuntimeError("world has not been initialized")
        return world

    def get_state(self) -> WorldState:
        with self.sessions() as session:
            world = self._world_row(session)
            actors = tuple(
                ActorState(
                    actor.id,
                    actor.kind,
                    actor.x,
                    actor.y,
                    actor.z,
                    actor.h,
                    _activity(actor),
                )
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
                session.query(EventRow).delete()
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
                self.grid = WorldGrid(
                    generated.chunks.values(), generated.levels.values(), self.registry
                )
                events: list[Event] = [WorldGenerated(seed=seed, gen_version=world.gen_version)]
                actor_event = self._ensure_actor(session)
                if actor_event is not None:
                    events.append(actor_event)
                events.append(ClockChanged(speed=world.speed, paused=world.paused))
                next_seq = self._stamp_and_store(session, world, events, first_seq=1)
                session.commit()
            self._next_seq = next_seq
            self.bus.enqueue(events)
            state = self.get_state()
        await self.bus.drain()
        return state

    async def submit(
        self, actor_id: str, action: Action, delta_seconds: float = 1 / 20
    ) -> ActionResult:
        events: list[Event] = []
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
                if isinstance(action, MoveAction) and action.dx == 0 and action.dy == 0:
                    # Releasing WASD sends a zero vector; it is not an action, so it must not
                    # interrupt an activity that was chosen a moment earlier.
                    return ActionResult(
                        accepted=True,
                        actor_id=actor_id,
                        action=action,
                        x=actor.x,
                        y=actor.y,
                        z=actor.z,
                        h=actor.h,
                        activity=_activity(actor),
                    )
                handler = handler_for(action.op)
                ctx = ActionContext(session, world, actor, self.grid, delta_seconds)
                reason = handler.validate(ctx, action)
                if reason is not None:
                    # A rejection changes nothing, a running activity included.
                    return ActionResult(
                        accepted=False,
                        actor_id=actor_id,
                        action=action,
                        x=actor.x,
                        y=actor.y,
                        z=actor.z,
                        h=actor.h,
                        reason=reason,
                        activity=_activity(actor),
                    )
                running = _activity(actor)
                if running is not None:
                    actor.activity = None
                    events.append(
                        ActivityFinished(
                            actor_id=actor.id,
                            op=running.op,
                            outcome="interrupted",
                            reason=action.op,
                        )
                    )
                text: str | None = None
                activity: ActivityState | None = None
                duration = handler.duration(ctx, action)
                if duration == 0:
                    resolution = handler.resolve(ctx, action)
                    events.extend(resolution.events)
                    text = resolution.text
                else:
                    activity = ActivityState(
                        op=action.op,
                        action=action.model_dump(mode="json"),
                        started_minute=world.game_minute,
                        ends_minute=world.game_minute + duration,
                    )
                    actor.activity = activity.model_dump(mode="json")
                    target = None if isinstance(action, MoveAction) else action.target
                    events.append(
                        ActivityStarted(
                            actor_id=actor.id,
                            op=action.op,
                            target=target.model_dump(mode="json") if target is not None else {},
                            ends_minute=activity.ends_minute,
                        )
                    )
                next_seq = self._stamp_and_store(session, world, events)
                session.commit()
                self._next_seq = next_seq
                self.bus.enqueue(events)
                result = ActionResult(
                    accepted=True,
                    actor_id=actor_id,
                    action=action,
                    x=actor.x,
                    y=actor.y,
                    z=actor.z,
                    h=actor.h,
                    text=text,
                    activity=activity,
                )
        await self.bus.drain()
        return result

    async def advance_time(self) -> WorldState:
        events: list[Event] = []
        async with self._lock:
            with self.sessions() as session:
                world = self._world_row(session)
                if not world.paused:
                    world.game_minute += 1
                    # Completions come before clock.ticked, in actor-id order: replay needs it.
                    events.extend(self._complete_activities(session, world))
                    events.append(ClockTicked())
                    next_seq = self._stamp_and_store(session, world, events)
                    session.commit()
                    self._next_seq = next_seq
                    self.bus.enqueue(events)
            state = self.get_state()
        await self.bus.drain()
        return state

    def _complete_activities(self, session: Session, world: WorldMeta) -> list[Event]:
        events: list[Event] = []
        for actor in session.scalars(select(Actor).order_by(Actor.id)):
            running = _activity(actor)
            if running is None or running.ends_minute > world.game_minute:
                continue
            actor.activity = None
            action = action_adapter.validate_python(running.action)
            handler = handler_for(action.op)
            ctx = ActionContext(session, world, actor, self.grid, 0)
            # The world may have changed since the start; effects apply only if still valid.
            reason = handler.validate(ctx, action)
            if reason is not None:
                events.append(
                    ActivityFinished(
                        actor_id=actor.id, op=running.op, outcome="failed", reason=reason
                    )
                )
                continue
            events.extend(handler.complete(ctx, action))
            events.append(ActivityFinished(actor_id=actor.id, op=running.op, outcome="completed"))
        return events

    def menu(self, actor_id: str, x: float, y: float, z: int) -> MenuPayload:
        """Generated right-click entries. A read: no lock, since submit validates again."""
        tile_x, tile_y = floor(x), floor(y)
        surfaces = self.grid.standing_surfaces(tile_x, tile_y)
        visible = [surface for surface in surfaces if surface.z == z] or list(surfaces)
        candidates: list[Target] = []
        if visible:
            surface = min(visible, key=lambda item: abs(item.z - z))
            material = self.registry.get(surface.material_id)
            name = material.name if material is not None else "unknown"
            label = f"{name} · {metres(surface.h)}"
            candidates.append(TileTarget(x=tile_x, y=tile_y, h=surface.h))
        else:
            label = "nothing"
        entries: list[MenuEntry] = []
        with self.sessions() as session:
            world = self._world_row(session)
            actor = session.get(Actor, actor_id)
            if actor is None:
                raise KeyError(f"unknown actor: {actor_id}")
            if (floor(actor.x), floor(actor.y)) == (tile_x, tile_y):
                candidates.append(SelfTarget())
            ctx = ActionContext(session, world, actor, self.grid, 0)
            for spec, handler in handled_ops():
                for target in candidates:
                    if target.kind not in spec.targets or not handler.applies(ctx, target):
                        continue
                    action = handler.build(ctx, target)
                    reason = handler.validate(ctx, action)
                    entries.append(
                        MenuEntry(
                            op=spec.key,
                            label=spec.label,
                            tags=list(spec.tags),
                            available=reason is None,
                            reason=reason,
                            action=action,
                        )
                    )
            session.rollback()
        return MenuPayload(x=x, y=y, z=z, target=label, entries=tuple(entries))

    async def set_clock(
        self, *, paused: bool | None = None, speed: int | None = None
    ) -> WorldState:
        events: list[Event] = []
        async with self._lock:
            with self.sessions() as session:
                world = self._world_row(session)
                old_speed, old_paused = world.speed, world.paused
                if paused is not None:
                    world.paused = paused
                if speed is not None:
                    if speed not in (1, 3, 10):
                        raise ValueError("speed must be 1, 3, or 10")
                    world.speed = speed
                if (world.speed, world.paused) != (old_speed, old_paused):
                    events.append(ClockChanged(speed=world.speed, paused=world.paused))
                    next_seq = self._stamp_and_store(session, world, events)
                    session.commit()
                    self._next_seq = next_seq
                    self.bus.enqueue(events)
            state = self.get_state()
        await self.bus.drain()
        return state

    def read_events(
        self,
        after_seq: int = 0,
        limit: int = 100,
        event_type: str | None = None,
        actor_id: str | None = None,
    ) -> list[EventRow]:
        statement = select(EventRow).where(EventRow.seq > after_seq).order_by(EventRow.seq)
        if event_type is not None:
            statement = statement.where(EventRow.type == event_type)
        if actor_id is not None:
            statement = statement.where(EventRow.actor_id == actor_id)
        statement = statement.limit(limit)
        with self.sessions() as session:
            return list(session.scalars(statement))


def _activity(actor: Actor) -> ActivityState | None:
    return ActivityState.model_validate(actor.activity) if actor.activity else None
