from typing import Annotated, Literal

from pydantic import BaseModel, Field


class InputMessage(BaseModel):
    type: Literal["input"]
    sequence: int = Field(ge=0)
    dx: float = Field(ge=-1, le=1)
    dy: float = Field(ge=-1, le=1)


class ClockMessage(BaseModel):
    type: Literal["clock"]
    paused: bool | None = None
    speed: Literal[1, 3, 10] | None = None


ClientMessage = Annotated[InputMessage | ClockMessage, Field(discriminator="type")]


class ActorSnapshot(BaseModel):
    id: str
    kind: str
    x: float
    y: float
    z: int
    h: int = 0


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


class ChunkMessage(BaseModel):
    type: Literal["chunk"]
    cx: int
    cy: int
    revision: int
    ground_h: list[int]
    surface_mat: list[int]
    levels: list[ChunkLevelMessage]


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


class ErrorMessage(BaseModel):
    type: Literal["error"]
    message: str


ServerMessage = SnapshotMessage | TickMessage | AckMessage | ChunkMessage | ErrorMessage
