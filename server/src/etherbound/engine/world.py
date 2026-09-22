import asyncio
from collections.abc import Awaitable, Callable
from dataclasses import dataclass

from sqlalchemy import select
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, WorldMeta
from etherbound.engine.actions import Action, ActionResult
from etherbound.engine.movement import move_on_map
from etherbound.testmap import DEFAULT_MAP, TestMap

PLAYER_ID = "niko"
WALKING_SPEED_METRES_PER_SECOND = 4.0


@dataclass(frozen=True, slots=True)
class ActorState:
    id: str
    kind: str
    x: float
    y: float
    z: int


@dataclass(frozen=True, slots=True)
class WorldState:
    seed: int
    game_minute: int
    speed: int
    paused: bool
    actors: tuple[ActorState, ...]


class WorldEngine:
    """The only component allowed to validate and commit world state changes."""

    def __init__(self, sessions: sessionmaker[Session], game_map: TestMap = DEFAULT_MAP) -> None:
        self.sessions = sessions
        self.game_map = game_map
        self._lock = asyncio.Lock()
        self._clock_listener: Callable[[int, int, bool], Awaitable[None] | None] | None = None

    def set_clock_listener(
        self, listener: Callable[[int, int, bool], Awaitable[None] | None]
    ) -> None:
        self._clock_listener = listener

    def ensure_world(self, seed: int = 0) -> None:
        with self.sessions() as session:
            if session.get(WorldMeta, 1) is None:
                session.add(WorldMeta(id=1, seed=seed, game_minute=0, speed=1, paused=False))
                session.add(Actor(id=PLAYER_ID, kind="player", x=2.0, y=2.0, z=0))
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
                ActorState(actor.id, actor.kind, actor.x, actor.y, actor.z)
                for actor in session.scalars(select(Actor).order_by(Actor.id))
            )
            return WorldState(world.seed, world.game_minute, world.speed, world.paused, actors)

    def clock_state(self) -> tuple[int, int, bool]:
        with self.sessions() as session:
            world = self._world_row(session)
            return world.game_minute, world.speed, world.paused

    async def new_game(self, seed: int) -> WorldState:
        async with self._lock:
            with self.sessions() as session:
                session.query(Actor).delete()
                world = self._world_row(session)
                world.seed = seed
                world.game_minute = 0
                world.speed = 1
                world.paused = False
                session.add(Actor(id=PLAYER_ID, kind="player", x=2.0, y=2.0, z=0))
                session.commit()
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
                        reason="paused",
                    )
                actor.x, actor.y = move_on_map(
                    actor.x,
                    actor.y,
                    action.dx,
                    action.dy,
                    WALKING_SPEED_METRES_PER_SECOND * delta_seconds,
                    self.game_map,
                )
                session.commit()
                return ActionResult(
                    accepted=True,
                    actor_id=actor_id,
                    action=action,
                    x=actor.x,
                    y=actor.y,
                    z=actor.z,
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
