from typing import ClassVar

from etherbound.engine.actions import Action, ClimbAction, Target, TileTarget
from etherbound.engine.ops.base import ActionContext, Resolution
from etherbound.events.models import ActorMoved, Event, TilePos
from etherbound.world.grid import StandingSurface

CLIMB_MINUTES = 1
# 1 m to 1.5 m: a person pulls up onto that, or lowers down from it, without a ladder.
CLIMB_MIN_H = 2
CLIMB_MAX_H = 3
WALKABLE_H = 1


class ClimbHandler:
    op: ClassVar[str] = "climb"

    def _neighbour(self, ctx: ActionContext, x: int, y: int) -> bool:
        ax, ay = ctx.actor_tile()
        return abs(x - ax) + abs(y - ay) == 1

    def _climbable(self, ctx: ActionContext, surface: StandingSurface) -> bool:
        return CLIMB_MIN_H <= abs(surface.h - ctx.actor.h) <= CLIMB_MAX_H

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if not isinstance(target, TileTarget) or not self._neighbour(ctx, target.x, target.y):
            return False
        surfaces = ctx.grid.standing_surfaces(target.x, target.y)
        # A surface within a step means the actor would simply walk there.
        return bool(surfaces) and not any(
            abs(surface.h - ctx.actor.h) <= WALKABLE_H for surface in surfaces
        )

    def build(self, ctx: ActionContext, target: Target) -> Action:
        assert isinstance(target, TileTarget)
        surfaces = ctx.grid.standing_surfaces(target.x, target.y)
        climbable = [surface for surface in surfaces if self._climbable(ctx, surface)]
        nearest = min(climbable or surfaces, key=lambda surface: abs(surface.h - ctx.actor.h))
        return ClimbAction(target=TileTarget(x=target.x, y=target.y, h=nearest.h))

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, ClimbAction)
        target = action.target
        if not self._neighbour(ctx, target.x, target.y):
            return "out of reach"
        surface = next(
            (s for s in ctx.grid.standing_surfaces(target.x, target.y) if s.h == target.h),
            None,
        )
        if surface is None:
            return "nothing to stand on"
        if not self._climbable(ctx, surface):
            return "too high to climb"
        ax, ay = ctx.actor_tile()
        if ctx.grid.wall_between(ax, ay, target.x, target.y, ctx.actor.h) or (
            ctx.grid.wall_between(ax, ay, target.x, target.y, target.h)
        ):
            return "wall in the way"
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return CLIMB_MINUTES

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        raise NotImplementedError("climb is an activity; it completes, it never resolves")

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        assert isinstance(action, ClimbAction)
        target = action.target
        from_tile = ctx.actor_pos()
        ctx.actor.x, ctx.actor.y = target.x + 0.5, target.y + 0.5
        ctx.actor.h, ctx.actor.z = target.h, target.h // 6
        return [
            ActorMoved(
                actor_id=ctx.actor.id,
                from_tile=from_tile,
                to_tile=TilePos(x=target.x, y=target.y, h=target.h),
                mode="climb",
            )
        ]
