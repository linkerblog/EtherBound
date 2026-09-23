from collections.abc import Sequence
from dataclasses import dataclass, field
from math import floor
from typing import ClassVar, Protocol

from sqlalchemy import select
from sqlalchemy.orm import Session

from etherbound.db.models import Actor, WorldMeta
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import Action, Target
from etherbound.events.models import Event, TilePos
from etherbound.world.chunk import CHUNK_SIZE
from etherbound.world.grid import WorldGrid


@dataclass(slots=True)
class ActionContext:
    session: Session
    world: WorldMeta
    actor: Actor
    grid: WorldGrid
    delta_seconds: float
    load_kg: float = 0.0

    def actor_tile(self) -> tuple[int, int]:
        return floor(self.actor.x), floor(self.actor.y)

    def actor_pos(self) -> TilePos:
        x, y = self.actor_tile()
        return TilePos(x=x, y=y, h=self.actor.h)

    def others_on(self, x: int, y: int, h: int) -> list[Actor]:
        return [
            other
            for other in self.session.scalars(select(Actor).order_by(Actor.id))
            if other.id != self.actor.id and (floor(other.x), floor(other.y), other.h) == (x, y, h)
        ]


@dataclass(slots=True)
class Resolution:
    events: list[Event] = field(default_factory=lambda: [])
    text: str | None = None


class OpHandler(Protocol):
    op: ClassVar[str]

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        """Whether the op is shown in the menu at all for this target."""
        ...

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        """The exact action(s) the menu offers; one target can yield several entries."""
        ...

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        """What the entry acts on, shown after the label; None for the target itself."""
        ...

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        """The reason the action cannot be done now, or None."""
        ...

    def duration(self, ctx: ActionContext, action: Action) -> int:
        """Game minutes; 0 makes the op instant."""
        ...

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        """Apply an instant op."""
        ...

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        """Apply an activity once its time is up, after it was validated again."""
        ...


def metres(h: int) -> str:
    return f"{h * 0.5:.1f} m".replace(".0 m", " m")


def in_close_reach(ctx: ActionContext, x: int, y: int) -> bool:
    """The actor's own tile, or an orthogonal neighbour with no wall on the shared edge."""
    ax, ay = ctx.actor_tile()
    if (x, y) == (ax, ay):
        return True
    if abs(x - ax) + abs(y - ay) != 1:
        return False
    return not ctx.grid.wall_between(ax, ay, x, y, ctx.actor.h)


# From 1 m below the feet to 1.5 m above them.
REACH_DOWN_H = 2
REACH_UP_H = 3


def within_reach_height(ctx: ActionContext, h: int) -> bool:
    return ctx.actor.h - REACH_DOWN_H <= h <= ctx.actor.h + REACH_UP_H


def object_reach(ctx: ActionContext, obj: ObjectRow) -> bool:
    """Whether an object can be touched, honouring carried objects and worn containers."""
    if obj.loc in ("held", "worn"):
        return True
    if obj.loc == "tile":
        if obj.x is None or obj.y is None or obj.h is None:
            return False
        return in_close_reach(ctx, obj.x, obj.y) and within_reach_height(ctx, obj.h)
    if obj.loc == "in":
        container = ctx.session.get(ObjectRow, obj.container_id)
        return container is not None and object_reach(ctx, container)
    return False


def tile_reach(ctx: ActionContext, x: int, y: int, h: int) -> bool:
    return in_close_reach(ctx, x, y) and within_reach_height(ctx, h)


def chunk_cell(x: int, y: int) -> tuple[int, int]:
    return x // CHUNK_SIZE, y // CHUNK_SIZE
