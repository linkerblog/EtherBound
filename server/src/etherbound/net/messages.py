from typing import Annotated, Literal

from pydantic import BaseModel, Field

from etherbound.engine.actions import Action, CarriedObject


class InputMessage(BaseModel):
    type: Literal["input"]
    sequence: int = Field(ge=0)
    dx: float = Field(ge=-1, le=1)
    dy: float = Field(ge=-1, le=1)
    dt: float = Field(default=0.05, gt=0, le=0.1)


class ClockMessage(BaseModel):
    type: Literal["clock"]
    paused: bool | None = None
    speed: Literal[1, 3, 10] | None = None


class ActionMessage(BaseModel):
    type: Literal["action"]
    sequence: int = Field(ge=0)
    action: Action


ClientMessage = Annotated[InputMessage | ClockMessage | ActionMessage, Field(discriminator="type")]


class ActivitySnapshot(BaseModel):
    op: str
    started_minute: int
    ends_minute: int


class ActorSnapshot(BaseModel):
    id: str
    kind: str
    x: float
    y: float
    z: int
    h: int = 0
    activity: ActivitySnapshot | None = None
    carried: list[CarriedObject] = Field(default_factory=lambda: [])
    load_kg: float = 0.0


class WorldInfo(BaseModel):
    chunk_size: int = 32
    level_h: int = 6
    bounds: list[int] = Field(default_factory=lambda: [0, 0, 256, 256])


class ChunkLevelMessage(BaseModel):
    z: int
    floor_h: list[int]
    floor_mat: list[int]
    wall_n: list[int]
    wall_w: list[int]
    edge_flags: list[int]
    flags: list[int]


class ObjectMessage(BaseModel):
    id: int
    kind: str
    x: int
    y: int
    h: int
    quantity: int
    open: bool | None = None


class ChunkMessage(BaseModel):
    type: Literal["chunk"]
    cx: int
    cy: int
    revision: int
    ground_h: list[int]
    surface_mat: list[int]
    levels: list[ChunkLevelMessage]
    objects: list[ObjectMessage] = Field(default_factory=lambda: [])


class SnapshotMessage(BaseModel):
    type: Literal["snapshot"]
    seed: int
    game_minute: int
    speed: int
    paused: bool
    actors: list[ActorSnapshot]
    world: WorldInfo = Field(default_factory=WorldInfo)
    gen_version: int = 0


class TickMessage(BaseModel):
    type: Literal["tick"]
    game_minute: int
    speed: int
    paused: bool
    actors: list[ActorSnapshot]
    world: WorldInfo = Field(default_factory=WorldInfo)
    gen_version: int = 0


class AckMessage(BaseModel):
    type: Literal["ack"]
    sequence: int
    accepted: bool
    actor_id: str
    x: float
    y: float
    z: int
    h: int = 0
    reason: str | None = None


class ResultMessage(BaseModel):
    type: Literal["result"]
    sequence: int
    accepted: bool
    reason: str | None = None
    text: str | None = None
    activity: ActivitySnapshot | None = None
    carried: list[CarriedObject] = Field(default_factory=lambda: [])
    load_kg: float = 0.0


class ActivityMessage(BaseModel):
    type: Literal["activity"]
    actor_id: str
    op: str
    outcome: Literal["completed", "interrupted", "failed"]
    reason: str | None = None


class ErrorMessage(BaseModel):
    type: Literal["error"]
    message: str


ServerMessage = (
    SnapshotMessage
    | TickMessage
    | AckMessage
    | ResultMessage
    | ActivityMessage
    | ChunkMessage
    | ErrorMessage
)
