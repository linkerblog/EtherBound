from typing import Any, ClassVar, Literal

from pydantic import BaseModel


class TilePos(BaseModel):
    x: int
    y: int
    h: int


class Event(BaseModel):
    logged: ClassVar[bool] = True
    seq: int = 0
    game_minute: int = 0
    type: Any  # Subclasses narrow this field to a Literal.
    actor_id: str | None = None


class WorldGenerated(Event):
    type: Literal["world.generated"] = "world.generated"
    seed: int
    gen_version: int


class ActorSpawned(Event):
    type: Literal["actor.spawned"] = "actor.spawned"
    kind: str
    tile: TilePos
    reason: Literal["created", "relocated"]


class ActorMoved(Event):
    type: Literal["actor.moved"] = "actor.moved"
    from_tile: TilePos
    to_tile: TilePos


class ClockTicked(Event):
    logged: ClassVar[bool] = False
    type: Literal["clock.ticked"] = "clock.ticked"


class ClockChanged(Event):
    type: Literal["clock.changed"] = "clock.changed"
    speed: int
    paused: bool
