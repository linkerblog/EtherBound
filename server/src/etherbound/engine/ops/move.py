from math import floor
from typing import ClassVar

from etherbound.engine.actions import Action
from etherbound.engine.movement import move_in_world
from etherbound.engine.verbs.base import ActionContext
from etherbound.events.models import ActorMoved, Event, TilePos

WALKING_SPEED_METRES_PER_SECOND = 4.0


class MoveHandler:
    verb: ClassVar[str] = "move"

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        return None

    def resolve(self, ctx: ActionContext, action: Action) -> list[Event]:
        from_tile = TilePos(x=floor(ctx.actor.x), y=floor(ctx.actor.y), h=ctx.actor.h)
        ctx.actor.x, ctx.actor.y, ctx.actor.h = move_in_world(
            ctx.actor.x,
            ctx.actor.y,
            ctx.actor.h,
            action.dx,
            action.dy,
            WALKING_SPEED_METRES_PER_SECOND * ctx.delta_seconds,
            ctx.grid,
        )
        ctx.actor.z = ctx.actor.h // 6
        to_tile = TilePos(x=floor(ctx.actor.x), y=floor(ctx.actor.y), h=ctx.actor.h)
        if (from_tile.x, from_tile.y, from_tile.h) == (to_tile.x, to_tile.y, to_tile.h):
            return []
        return [ActorMoved(actor_id=ctx.actor.id, from_tile=from_tile, to_tile=to_tile)]
