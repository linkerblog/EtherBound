from collections.abc import Sequence
from typing import ClassVar

from etherbound.engine.actions import Action, MoveAction, Target
from etherbound.engine.movement import move_in_world
from etherbound.engine.ops.base import ActionContext, Resolution
from etherbound.events.models import ActorMoved, Event

WALKING_SPEED_METRES_PER_SECOND = 4.0


class MoveHandler:
    op: ClassVar[str] = "move"

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        # Movement is WASD, never a menu entry.
        return False

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        raise NotImplementedError("move is never offered in a menu")

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        return None

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return 0

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        assert isinstance(action, MoveAction)
        from_tile = ctx.actor_pos()
        ctx.actor.x, ctx.actor.y, ctx.actor.h = move_in_world(
            ctx.actor.x,
            ctx.actor.y,
            ctx.actor.h,
            action.dx,
            action.dy,
            WALKING_SPEED_METRES_PER_SECOND * ctx.delta_seconds,
            ctx.grid,
            ctx.load_kg,
        )
        ctx.actor.z = ctx.actor.h // 6
        to_tile = ctx.actor_pos()
        if from_tile == to_tile:
            return Resolution()
        return Resolution([ActorMoved(actor_id=ctx.actor.id, from_tile=from_tile, to_tile=to_tile)])

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []
