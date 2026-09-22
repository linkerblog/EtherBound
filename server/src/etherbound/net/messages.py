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


class SnapshotMessage(BaseModel):
    type: Literal["snapshot"]
    seed: int
    game_minute: int
    speed: int
    paused: bool
    actors: list[ActorSnapshot]


class TickMessage(BaseModel):
    type: Literal["tick"]
    game_minute: int
    speed: int
    paused: bool
    actors: list[ActorSnapshot]


class AckMessage(BaseModel):
    type: Literal["ack"]
    sequence: int
    accepted: bool
    actor_id: str
    x: float
    y: float
    z: int
    reason: str | None = None


class ErrorMessage(BaseModel):
    type: Literal["error"]
    message: str


ServerMessage = SnapshotMessage | TickMessage | AckMessage | ErrorMessage
