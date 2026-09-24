import asyncio
from math import floor
from typing import Any, Literal

from pydantic import TypeAdapter, ValidationError
from sqlalchemy import func, select
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, WallIntegrity, WorldMeta
from etherbound.db.models import Chunk as ChunkRow
from etherbound.db.models import ChunkLevel as ChunkLevelRow
from etherbound.db.models import Event as EventRow
from etherbound.db.models import Material as MaterialRow
from etherbound.db.models import Object as ObjectRow
from etherbound.engine import actions, payloads, world_setup
from etherbound.engine.menu import build_menu
from etherbound.engine.objects import actor_load_kg, tile_objects
from etherbound.engine.ops import ActionContext, handler_for
from etherbound.events.bus import EventBus
from etherbound.events.models import (
    ActivityFinished,
    ActivityStarted,
    ActorGoalSet,
    ClockChanged,
    ClockTicked,
    Event,
    WorldGenerated,
)
from etherbound.world.chunk import CHUNK_SIZE
from etherbound.world.gen.registry import DEFAULT_GENERATOR, get_generator
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry
from etherbound.world.objects import ObjectCatalog

CHUNK_RADIUS = 2
HANDLING_OPS = frozenset({"take", "drop", "put", "open", "close", "wear", "remove"})
PLAYER_ID = actions.PLAYER_ID

action_adapter: TypeAdapter[actions.Action] = TypeAdapter(actions.Action)


class WorldEngine:
    """The only component allowed to validate and commit world state changes."""

    def __init__(
        self,
        sessions: sessionmaker[Session],
        registry: MaterialRegistry | None = None,
        bus: EventBus | None = None,
        catalog: ObjectCatalog | None = None,
    ) -> None:
        self.sessions = sessions
        self.registry = registry or MaterialRegistry.load()
        self.catalog = catalog or ObjectCatalog.load(registry=self.registry)
        self.grid = WorldGrid(registry=self.registry, catalog=self.catalog)
        self._lock = asyncio.Lock()
        self.bus = bus or EventBus()
        self._next_seq = 1
        self._load_cache: dict[str, float] = {}

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
                            mode="json",
                            by_alias=True,
                            exclude={"seq", "game_minute", "type", "actor_id"},
                        ),
                    )
                )
        return next_seq

    def _load_object_index(self, session: Session) -> None:
        for cx, cy in self.grid.chunks:
            self.grid.set_chunk_objects(cx, cy, tile_objects(session, self.catalog, cx, cy))

    def _refresh_load(self, session: Session) -> None:
        self._load_cache = {
            actor.id: actor_load_kg(session, self.catalog, actor.id)
            for actor in session.scalars(select(Actor))
        }

    def load_kg(self, actor_id: str) -> float:
        return self._load_cache.get(actor_id, 0.0)

    def ensure_world(self, seed: int = 0) -> None:
        events: list[Event] = []
        with self.sessions() as session:
            meta = session.get(WorldMeta, 1)
            if meta is None:
                meta = WorldMeta(
                    id=1,
                    seed=seed,
                    game_minute=0,
                    speed=1,
                    paused=False,
                    gen_version=0,
                    generator=DEFAULT_GENERATOR,
                    gen_options={},
                )
                session.add(meta)
                session.flush()
            saved_ids = {row.key: row.id for row in session.scalars(select(MaterialRow))}
            if saved_ids:
                self.registry = MaterialRegistry.load(existing_ids=saved_ids)
                self.grid.registry = self.registry
            world_setup.sync_materials(session, self.registry)
            spec, options, fallback = world_setup.resolve_generator(meta)
            has_chunks = world_setup.has_chunks(session)
            generated_world = fallback or not has_chunks or meta.gen_version < spec.version
            if generated_world:
                if has_chunks:
                    session.query(WallIntegrity).delete(synchronize_session=False)
                    session.query(ChunkLevelRow).delete(synchronize_session=False)
                    session.query(ChunkRow).delete(synchronize_session=False)
                world_setup.delete_uncarried_objects(session)
                world = spec.generate(meta.seed, options, self.registry)
                world_setup.commit_world(session, world)
                world_setup.commit_objects(session, world.objects)
                meta.gen_version = world.gen_version
                meta.generator = spec.key
                meta.gen_options = options.model_dump(mode="json")
                events.append(
                    WorldGenerated(
                        seed=meta.seed,
                        gen_version=world.gen_version,
                        generator=spec.key,
                        options=meta.gen_options,
                    )
                )
            chunks, levels = world_setup.load_grid(session)
            self.grid = WorldGrid(chunks, levels, self.registry, catalog=self.catalog)
            self._load_object_index(session)
            events.extend(
                world_setup.ensure_actors(
                    session, self.grid, self.registry, spec, options, meta.seed
                )
            )
            self._refresh_load(session)
            if generated_world:
                events.append(ClockChanged(speed=meta.speed, paused=meta.paused))
            self._next_seq = (session.scalar(select(func.max(EventRow.seq))) or 0) + 1
            next_seq = self._stamp_and_store(session, meta, events)
            session.commit()
        self._next_seq = next_seq
        self.bus.enqueue(events)

    def _world_row(self, session: Session) -> WorldMeta:
        world = session.get(WorldMeta, 1)
        if world is None:
            raise RuntimeError("world has not been initialized")
        return world

    def get_state(self) -> payloads.WorldState:
        with self.sessions() as session:
            world = self._world_row(session)
            actors = tuple(
                payloads.ActorState(
                    id=actor.id,
                    kind=actor.kind,
                    x=actor.x,
                    y=actor.y,
                    z=actor.z,
                    h=actor.h,
                    activity=_activity(actor),
                    carried=self._carried(session, actor.id),
                    load_kg=self._load_cache.get(actor.id, 0.0),
                    name=actor.name,
                    mind=_mind(actor),
                )
                for actor in session.scalars(select(Actor).order_by(Actor.id))
            )
            return payloads.WorldState(
                world.seed,
                world.game_minute,
                world.speed,
                world.paused,
                actors,
                world.gen_version,
                world.generator,
                dict(world.gen_options or {}),
            )

    def _carried(self, session: Session, actor_id: str) -> tuple[actions.CarriedObject, ...]:
        carried: list[actions.CarriedObject] = []
        rows = session.scalars(
            select(ObjectRow).where(ObjectRow.actor_id == actor_id).order_by(ObjectRow.id)
        )
        for row in rows:
            kind = self.catalog.get(row.kind)
            carried.append(
                actions.CarriedObject(
                    id=row.id,
                    kind=row.kind,
                    name=kind.name if kind is not None else row.kind,
                    quantity=row.quantity,
                    slot=row.slot or "",
                )
            )
        return tuple(carried)

    def clock_state(self) -> tuple[int, int, bool]:
        with self.sessions() as session:
            world = self._world_row(session)
            return world.game_minute, world.speed, world.paused

    def world_info(self) -> payloads.WorldInfo:
        bounds = self.grid.bounds() or (0, 0, 0, 0)
        return payloads.WorldInfo(chunk_size=CHUNK_SIZE, level_h=6, bounds=bounds)

    def chunk_payload(self, cx: int, cy: int) -> payloads.ChunkPayload | None:
        chunk = self.grid.chunk(cx, cy)
        if chunk is None:
            return None
        levels = tuple(
            payloads.ChunkLevelPayload(
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
        return payloads.ChunkPayload(
            cx=cx,
            cy=cy,
            revision=chunk.revision,
            ground_h=chunk.ground_h,
            surface_mat=chunk.surface_mat,
            levels=levels,
            objects=tuple(
                payloads.ObjectPayload(
                    id=obj.id,
                    kind=obj.kind,
                    x=obj.x,
                    y=obj.y,
                    h=obj.h,
                    quantity=obj.quantity,
                    open=obj.open,
                )
                for obj in self.grid.objects_at_chunk(cx, cy)
            ),
        )

    def chunks_near(
        self, cx: int, cy: int, radius: int = CHUNK_RADIUS
    ) -> tuple[payloads.ChunkPayload, ...]:
        result: list[payloads.ChunkPayload] = []
        for offset_y in range(-radius, radius + 1):
            for offset_x in range(-radius, radius + 1):
                payload = self.chunk_payload(cx + offset_x, cy + offset_y)
                if payload is not None:
                    result.append(payload)
        return tuple(result)

    async def new_game(
        self,
        seed: int,
        generator: str = DEFAULT_GENERATOR,
        options: dict[str, Any] | None = None,
        *,
        paused: bool = False,
    ) -> payloads.WorldState:
        spec = get_generator(generator)
        if spec is None:
            raise ValueError(f"unknown generator: {generator}")
        resolved = spec.options.model_validate(options or {})
        async with self._lock:
            with self.sessions() as session:
                session.query(EventRow).delete()
                session.query(WallIntegrity).delete(synchronize_session=False)
                session.query(ObjectRow).delete()
                session.query(ChunkLevelRow).delete()
                session.query(ChunkRow).delete()
                session.query(Actor).delete()
                world = self._world_row(session)
                world.seed = seed
                world.game_minute = 0
                world.speed = 1
                world.paused = paused
                generated = spec.generate(seed, resolved, self.registry)
                world_setup.commit_world(session, generated)
                world_setup.commit_objects(session, generated.objects)
                world.gen_version = generated.gen_version
                world.generator = spec.key
                world.gen_options = resolved.model_dump(mode="json")
                self.grid = WorldGrid(
                    generated.chunks.values(),
                    generated.levels.values(),
                    self.registry,
                    catalog=self.catalog,
                )
                self._load_object_index(session)
                events: list[Event] = [
                    WorldGenerated(
                        seed=seed,
                        gen_version=world.gen_version,
                        generator=spec.key,
                        options=world.gen_options,
                    )
                ]
                actor_event = world_setup.ensure_actors(
                    session, self.grid, self.registry, spec, resolved, seed
                )
                events.extend(actor_event)
                self._refresh_load(session)
                events.append(ClockChanged(speed=world.speed, paused=world.paused))
                next_seq = self._stamp_and_store(session, world, events, first_seq=1)
                session.commit()
            self._next_seq = next_seq
            self.bus.enqueue(events)
            state = self.get_state()
        await self.bus.drain()
        return state

    async def submit(
        self, actor_id: str, action: actions.Action, delta_seconds: float = 1 / 20
    ) -> actions.ActionResult:
        events: list[Event] = []
        async with self._lock:
            with self.sessions() as session:
                world = self._world_row(session)
                actor = session.get(Actor, actor_id)
                if actor is None:
                    raise KeyError(f"unknown actor: {actor_id}")
                if world.paused:
                    return actions.ActionResult(
                        accepted=False,
                        actor_id=actor_id,
                        action=action,
                        x=actor.x,
                        y=actor.y,
                        z=actor.z,
                        h=actor.h,
                        reason="paused",
                        carried=list(self._carried(session, actor_id)),
                        load_kg=self.load_kg(actor_id),
                    )
                if isinstance(action, actions.MoveAction) and action.dx == 0 and action.dy == 0:
                    # Releasing WASD sends a zero vector; it is not an action, so it must not
                    # interrupt an activity that was chosen a moment earlier.
                    return actions.ActionResult(
                        accepted=True,
                        actor_id=actor_id,
                        action=action,
                        x=actor.x,
                        y=actor.y,
                        z=actor.z,
                        h=actor.h,
                        activity=_activity(actor),
                        carried=list(self._carried(session, actor_id)),
                        load_kg=self.load_kg(actor_id),
                    )
                handler = handler_for(action.op)
                ctx = ActionContext(
                    session, world, actor, self.grid, delta_seconds, self.load_kg(actor_id)
                )
                reason = handler.validate(ctx, action)
                if reason is not None:
                    # A rejection changes nothing, a running activity included.
                    return actions.ActionResult(
                        accepted=False,
                        actor_id=actor_id,
                        action=action,
                        x=actor.x,
                        y=actor.y,
                        z=actor.z,
                        h=actor.h,
                        reason=reason,
                        activity=_activity(actor),
                        carried=list(self._carried(session, actor_id)),
                        load_kg=self.load_kg(actor_id),
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
                activity: actions.ActivityState | None = None
                trajectory: list[actions.PhysicsPosition] = []
                duration = handler.duration(ctx, action)
                if duration == 0:
                    resolution = handler.resolve(ctx, action)
                    events.extend(resolution.events)
                    text = resolution.text
                    trajectory = resolution.trajectory
                else:
                    activity = actions.ActivityState(
                        op=action.op,
                        action=action.model_dump(mode="json"),
                        started_minute=world.game_minute,
                        ends_minute=world.game_minute + duration,
                    )
                    actor.activity = activity.model_dump(mode="json")
                    target = None if isinstance(action, actions.MoveAction) else action.target
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
                if action.op in HANDLING_OPS | {"throw"}:
                    self._refresh_load(session)
                result = actions.ActionResult(
                    accepted=True,
                    actor_id=actor_id,
                    action=action,
                    x=actor.x,
                    y=actor.y,
                    z=actor.z,
                    h=actor.h,
                    text=text,
                    activity=activity,
                    carried=list(self._carried(session, actor_id)),
                    load_kg=self.load_kg(actor_id),
                    trajectory=trajectory,
                )
        await self.bus.drain()
        return result

    async def set_goal(
        self,
        actor_id: str,
        goal: actions.Goal | None,
        reason: Literal["chosen", "arrived", "stuck", "unreachable"],
    ) -> None:
        """Commit an Extra's intention. The brain proposes; the engine writes and logs it."""
        events: list[Event] = []
        async with self._lock:
            with self.sessions() as session:
                world = self._world_row(session)
                actor = session.get(Actor, actor_id)
                if actor is None:
                    raise KeyError(f"unknown actor: {actor_id}")
                if actor.id == PLAYER_ID:
                    raise ValueError("the player is not driven by a goal")
                if goal is not None and not any(
                    surface.h == goal.h for surface in self.grid.standing_surfaces(goal.x, goal.y)
                ):
                    raise ValueError("goal tile has no standing surface")
                mind = _mind(actor) or actions.Mind(
                    anchor=actions.Spot(x=floor(actor.x), y=floor(actor.y), h=actor.h)
                )
                mind.goal = goal
                actor.mind = mind.model_dump(mode="json")
                events.append(
                    ActorGoalSet(
                        actor_id=actor_id,
                        goal=goal.model_dump(mode="json") if goal is not None else None,
                        reason=reason,
                    )
                )
                next_seq = self._stamp_and_store(session, world, events)
                session.commit()
                self._next_seq = next_seq
                self.bus.enqueue(events)
        await self.bus.drain()

    async def advance_time(self) -> payloads.WorldState:
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
            ctx = ActionContext(session, world, actor, self.grid, 0, self.load_kg(actor.id))
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

    def menu(self, actor_id: str, x: float, y: float, z: int) -> payloads.MenuPayload:
        """Generated right-click entries. A read: no lock, since submit validates again."""
        return build_menu(
            self.sessions,
            actor_id,
            x,
            y,
            z,
            self.grid,
            self.registry,
            self.catalog,
            self._load_cache,
        )

    async def set_clock(
        self, *, paused: bool | None = None, speed: int | None = None
    ) -> payloads.WorldState:
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


def _activity(actor: Actor) -> actions.ActivityState | None:
    return actions.ActivityState.model_validate(actor.activity) if actor.activity else None


def _mind(actor: Actor) -> actions.Mind | None:
    if not actor.mind:
        return None
    try:
        return actions.Mind.model_validate(actor.mind)
    except ValidationError:
        return None
