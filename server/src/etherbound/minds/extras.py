"""The deterministic routine brain of an Extra (docs/Dev-018.md).

It only proposes: every step goes through ``WorldEngine.submit`` and every goal through
``WorldEngine.set_goal``. It keeps no state the engine cannot rebuild, so a restart at any tick
reproduces the same run.
"""

import logging
from dataclasses import dataclass
from math import floor, hypot

from etherbound.engine.actions import Goal, Mind, MoveAction, WaitAction
from etherbound.engine.payloads import ActorState, WorldState
from etherbound.engine.world import WorldEngine
from etherbound.events.bus import EventBus
from etherbound.events.models import ClockTicked
from etherbound.rng import RngStream, RNGStreams
from etherbound.world.nav import find_path

WALK_SPEED_M_S = 4.0
MAX_MOVES_PER_TICK = 6
WANDER_CHANCE = 0.6
WANDER_RADIUS_M = 12
WANDER_TRIES = 5
ARRIVE_METRES = 0.35
STALL_METRES = 0.05
MAX_EXPANSIONS = 3000

logger = logging.getLogger("etherbound.minds")


@dataclass(slots=True)
class _Plan:
    goal: Goal
    path: list[tuple[int, int, int]]
    index: int = 0


class ExtrasBrain:
    """One ``clock.ticked`` subscriber that walks the extras near their anchor."""

    def __init__(self, engine: WorldEngine, time_scale: float = 1.0) -> None:
        self.engine = engine
        # Real seconds per game minute; the brain walks 4 m per real second, like Niko.
        self.time_scale = time_scale
        self._plans: dict[str, _Plan] = {}
        self._retried: set[str] = set()

    def subscribe(self, bus: EventBus) -> None:
        bus.subscribe(ClockTicked, self.on_tick, name="minds.extras")

    async def on_tick(self, event: ClockTicked) -> None:
        del event
        state = self.engine.get_state()
        if state.paused:
            return
        tick_seconds = self.time_scale / state.speed
        for actor in state.actors:
            if actor.kind == "extra":
                await self._tick_actor(actor, state, tick_seconds)

    async def _tick_actor(self, actor: ActorState, state: WorldState, tick_seconds: float) -> None:
        if actor.activity is not None:
            self._retried.discard(actor.id)
            return
        mind = actor.mind
        if mind is None:
            return
        goal = mind.goal
        if goal is None:
            await self._routine(actor, state, mind)
            return
        if self._arrived(actor, goal):
            self._plans.pop(actor.id, None)
            self._retried.discard(actor.id)
            await self.engine.set_goal(actor.id, None, "arrived")
            await self.engine.submit(actor.id, WaitAction())
            return
        plan = self._plan_for(actor, goal)
        if plan is None:
            self._plans.pop(actor.id, None)
            await self.engine.set_goal(actor.id, None, "stuck")
            return
        start_x, start_y = actor.x, actor.y
        final_x, final_y, _ = await self._walk(actor, plan, tick_seconds)
        if hypot(final_x - start_x, final_y - start_y) >= STALL_METRES:
            self._retried.discard(actor.id)
            return
        # A stall is data for the brain, never a retry of the same step in the same tick.
        self._plans.pop(actor.id, None)
        if actor.id in self._retried or not self._recompute(
            actor.id, final_x, final_y, actor.h, goal
        ):
            self._retried.discard(actor.id)
            await self.engine.set_goal(actor.id, None, "stuck")
        else:
            self._retried.add(actor.id)

    async def _routine(self, actor: ActorState, state: WorldState, mind: Mind) -> None:
        rng = RNGStreams(state.seed).stream(f"extras:{actor.id}:{state.game_minute}")
        if rng.random() < WANDER_CHANCE:
            goal = self._choose_wander(actor, mind, rng)
            if goal is not None:
                await self.engine.set_goal(actor.id, goal, "chosen")
                return
        await self.engine.submit(actor.id, WaitAction())

    def _choose_wander(self, actor: ActorState, mind: Mind, rng: RngStream) -> Goal | None:
        start = (floor(actor.x), floor(actor.y), actor.h)
        for _ in range(WANDER_TRIES):
            dx = rng.randint(-WANDER_RADIUS_M, WANDER_RADIUS_M)
            dy = rng.randint(-WANDER_RADIUS_M, WANDER_RADIUS_M)
            if hypot(dx, dy) > WANDER_RADIUS_M or (dx == 0 and dy == 0):
                continue
            x, y = mind.anchor.x + dx, mind.anchor.y + dy
            if (x, y) == (start[0], start[1]):
                continue
            surfaces = self.engine.grid.standing_surfaces(x, y)
            if not surfaces:
                continue
            surface = min(surfaces, key=lambda item: (abs(item.h - mind.anchor.h), item.h))
            if find_path(self.engine.grid, start, (x, y, surface.h), MAX_EXPANSIONS) is not None:
                return Goal(x=x, y=y, h=surface.h)
        return None

    def _plan_for(self, actor: ActorState, goal: Goal) -> _Plan | None:
        plan = self._plans.get(actor.id)
        if plan is not None and plan.goal == goal and self._locate(plan, actor):
            return plan
        if not self._recompute(actor.id, actor.x, actor.y, actor.h, goal):
            return None
        return self._plans[actor.id]

    def _recompute(self, actor_id: str, x: float, y: float, h: int, goal: Goal) -> bool:
        path = find_path(
            self.engine.grid, (floor(x), floor(y), h), (goal.x, goal.y, goal.h), MAX_EXPANSIONS
        )
        if path is None:
            return False
        self._plans[actor_id] = _Plan(goal=goal, path=path)
        return True

    @staticmethod
    def _locate(plan: _Plan, actor: ActorState) -> bool:
        """Point the plan at the actor's tile; False when the actor is off the cached path."""
        for index, spot in enumerate(plan.path):
            if spot == (floor(actor.x), floor(actor.y), actor.h):
                plan.index = index
                return True
        return False

    async def _walk(
        self, actor: ActorState, plan: _Plan, tick_seconds: float
    ) -> tuple[float, float, int]:
        x, y, h = actor.x, actor.y, actor.h
        budget = WALK_SPEED_M_S * tick_seconds
        for _ in range(MAX_MOVES_PER_TICK):
            if budget <= 0:
                break
            self._advance(plan, x, y, h)
            if plan.index >= len(plan.path):
                break
            target_x, target_y, target_h = plan.path[plan.index]
            dx, dy = target_x + 0.5 - x, target_y + 0.5 - y
            distance = hypot(dx, dy)
            if distance < 1e-6:
                if h == target_h:
                    plan.index += 1
                    continue
                step_time = budget
                ux = uy = 0.0
            else:
                step_time = min(budget, distance / WALK_SPEED_M_S)
                ux, uy = dx / distance, dy / distance
            result = await self.engine.submit(
                actor.id, MoveAction(dx=ux, dy=uy), delta_seconds=step_time
            )
            budget -= step_time
            if not result.accepted:
                break
            x, y, h = result.x, result.y, result.h
        return x, y, h

    @staticmethod
    def _advance(plan: _Plan, x: float, y: float, h: int) -> None:
        # Keep the last waypoint as the target so a body lands on the goal centre, not its edge.
        while plan.index < len(plan.path) - 1:
            spot_x, spot_y, spot_h = plan.path[plan.index]
            if (floor(x), floor(y), h) == (spot_x, spot_y, spot_h):
                plan.index += 1
            else:
                return

    @staticmethod
    def _arrived(actor: ActorState, goal: Goal) -> bool:
        return (
            actor.h == goal.h
            and hypot(actor.x - (goal.x + 0.5), actor.y - (goal.y + 0.5)) <= ARRIVE_METRES
        )


__all__ = [
    "ExtrasBrain",
]
