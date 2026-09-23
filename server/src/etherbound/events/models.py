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
    mode: Literal["walk", "climb", "lowered"] = "walk"


class ActivityStarted(Event):
    type: Literal["activity.started"] = "activity.started"
    op: str
    target: dict[str, Any]
    ends_minute: int


class ActivityFinished(Event):
    type: Literal["activity.finished"] = "activity.finished"
    op: str
    outcome: Literal["completed", "interrupted", "failed"]
    reason: str | None = None


class TerrainDug(Event):
    type: Literal["terrain.dug"] = "terrain.dug"
    tile: TilePos
    removed: str
    exposed: str
    dug: int


class ChunkChanged(Event):
    # Replication only: the terrain.dug before it is the world fact.
    logged: ClassVar[bool] = False
    type: Literal["chunk.changed"] = "chunk.changed"
    cx: int
    cy: int
    revision: int


class ClockTicked(Event):
    logged: ClassVar[bool] = False
    type: Literal["clock.ticked"] = "clock.ticked"


class ClockChanged(Event):
    type: Literal["clock.changed"] = "clock.changed"
    speed: int
    paused: bool
