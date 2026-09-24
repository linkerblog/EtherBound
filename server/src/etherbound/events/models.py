from typing import Any, ClassVar, Literal

from pydantic import BaseModel, Field

from etherbound.engine.actions import Location, PhysicsPosition


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
    generator: str = "test"
    options: dict[str, Any] = Field(default_factory=dict)


class ActorSpawned(Event):
    type: Literal["actor.spawned"] = "actor.spawned"
    kind: str
    tile: TilePos
    reason: Literal["created", "relocated"]
    name: str | None = None


class ActorMoved(Event):
    type: Literal["actor.moved"] = "actor.moved"
    from_tile: TilePos
    to_tile: TilePos
    mode: Literal["walk", "climb", "lowered", "physics"] = "walk"


class ActorGoalSet(Event):
    type: Literal["actor.goal_set"] = "actor.goal_set"
    goal: dict[str, Any] | None = None
    reason: Literal["chosen", "arrived", "stuck", "unreachable"]


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


class ObjectMoved(Event):
    type: Literal["object.moved"] = "object.moved"
    object_id: int
    kind: str
    quantity: int
    op: str
    # Stored and sent as "from", a Python keyword, hence the field name and the serialization alias.
    from_: Location = Field(serialization_alias="from")
    to: Location
    split_from: int | None = None
    merged_into: int | None = None


class ObjectChanged(Event):
    type: Literal["object.changed"] = "object.changed"
    object_id: int
    kind: str
    op: str
    changes: dict[str, Any]


class Impact(Event):
    type: Literal["impact"] = "impact"
    target: dict[str, Any]
    energy: float


class PhysicsResolved(Event):
    type: Literal["physics.resolved"] = "physics.resolved"
    op: str
    trajectory: list[PhysicsPosition]
    damage: list[dict[str, Any]]
    broken: list[dict[str, Any]]


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
