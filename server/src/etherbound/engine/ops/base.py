from dataclasses import dataclass, field
from math import floor
from typing import ClassVar, Protocol

from sqlalchemy import select
from sqlalchemy.orm import Session

from etherbound.db.models import Actor, WorldMeta
from etherbound.engine.actions import Action, Target
from etherbound.events.models import Event, TilePos
from etherbound.world.grid import WorldGrid


@dataclass(slots=True)
class ActionContext:
    session: Session
    world: WorldMeta
    actor: Actor
    grid: WorldGrid
    delta_seconds: float

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

    def build(self, ctx: ActionContext, target: Target) -> Action:
        """The exact action the menu offers."""
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
