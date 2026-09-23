from dataclasses import dataclass
from typing import ClassVar, Protocol

from sqlalchemy.orm import Session

from etherbound.db.models import Actor, WorldMeta
from etherbound.engine.actions import Action
from etherbound.events.models import Event
from etherbound.world.grid import WorldGrid


@dataclass(slots=True)
class ActionContext:
    session: Session
    world: WorldMeta
    actor: Actor
    grid: WorldGrid
    delta_seconds: float


class VerbHandler(Protocol):
    verb: ClassVar[str]

    def validate(self, ctx: ActionContext, action: Action) -> str | None: ...

    def resolve(self, ctx: ActionContext, action: Action) -> list[Event]: ...
