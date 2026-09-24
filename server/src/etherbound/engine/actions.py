from typing import Annotated, Any, Literal

from pydantic import BaseModel, Field


class SelfTarget(BaseModel):
    kind: Literal["self"] = "self"


class TileTarget(BaseModel):
    kind: Literal["tile"] = "tile"
    x: int
    y: int
    h: int


class ObjectTarget(BaseModel):
    kind: Literal["object"] = "object"
    id: int


class ActorTarget(BaseModel):
    kind: Literal["actor"] = "actor"
    id: str


class EdgeTarget(BaseModel):
    kind: Literal["edge"] = "edge"
    x: int
    y: int
    z: int
    direction: Literal["north", "south", "east", "west"]


Target = Annotated[
    SelfTarget | TileTarget | ObjectTarget | ActorTarget | EdgeTarget, Field(discriminator="kind")
]


class TileLoc(BaseModel):
    kind: Literal["tile"] = "tile"
    x: int
    y: int
    h: int


class InLoc(BaseModel):
    kind: Literal["in"] = "in"
    object_id: int


class HeldLoc(BaseModel):
    kind: Literal["held"] = "held"
    actor_id: str
    hand: Literal["left", "right", "both"]


class WornLoc(BaseModel):
    kind: Literal["worn"] = "worn"
    actor_id: str
    slot: Literal["back"]


Location = Annotated[TileLoc | InLoc | HeldLoc | WornLoc, Field(discriminator="kind")]


class MoveAction(BaseModel):
    op: Literal["move"] = "move"
    dx: float = Field(ge=-1, le=1)
    dy: float = Field(ge=-1, le=1)


class InspectAction(BaseModel):
    op: Literal["inspect"] = "inspect"
    target: TileTarget | ObjectTarget


class WaitAction(BaseModel):
    op: Literal["wait"] = "wait"
    target: SelfTarget = Field(default_factory=SelfTarget)


class DigAction(BaseModel):
    op: Literal["dig"] = "dig"
    target: TileTarget


class ClimbAction(BaseModel):
    op: Literal["climb"] = "climb"
    target: TileTarget


class TakeAction(BaseModel):
    op: Literal["take"] = "take"
    target: ObjectTarget


class DropAction(BaseModel):
    op: Literal["drop"] = "drop"
    target: ObjectTarget


class PutAction(BaseModel):
    op: Literal["put"] = "put"
    target: ObjectTarget
    into: ObjectTarget | TileTarget


class OpenAction(BaseModel):
    op: Literal["open"] = "open"
    target: ObjectTarget


class CloseAction(BaseModel):
    op: Literal["close"] = "close"
    target: ObjectTarget


class WearAction(BaseModel):
    op: Literal["wear"] = "wear"
    target: ObjectTarget


class RemoveAction(BaseModel):
    op: Literal["remove"] = "remove"
    target: ObjectTarget


class PushAction(BaseModel):
    op: Literal["push"] = "push"
    target: ObjectTarget | ActorTarget
    dx: int = Field(ge=-1, le=1)
    dy: int = Field(ge=-1, le=1)


class PullAction(BaseModel):
    op: Literal["pull"] = "pull"
    target: ObjectTarget | ActorTarget
    dx: int = Field(ge=-1, le=1)
    dy: int = Field(ge=-1, le=1)


class DragAction(BaseModel):
    op: Literal["drag"] = "drag"
    target: ObjectTarget | ActorTarget
    dx: int = Field(ge=-1, le=1)
    dy: int = Field(ge=-1, le=1)


class ThrowAction(BaseModel):
    op: Literal["throw"] = "throw"
    target: ObjectTarget
    dx: int = Field(ge=-1, le=1)
    dy: int = Field(ge=-1, le=1)


class HitAction(BaseModel):
    op: Literal["hit"] = "hit"
    target: ObjectTarget | ActorTarget | EdgeTarget
    tool: ObjectTarget | None = None


class BreakAction(BaseModel):
    op: Literal["break"] = "break"
    target: ObjectTarget | EdgeTarget
    tool: ObjectTarget | None = None


Action = Annotated[
    MoveAction
    | InspectAction
    | WaitAction
    | DigAction
    | ClimbAction
    | TakeAction
    | DropAction
    | PutAction
    | OpenAction
    | CloseAction
    | WearAction
    | RemoveAction
    | PushAction
    | PullAction
    | DragAction
    | ThrowAction
    | HitAction
    | BreakAction,
    Field(discriminator="op"),
]


class PhysicsPosition(BaseModel):
    kind: Literal["actor", "object"]
    id: str | int
    x: float
    y: float
    h: int


class ActivityState(BaseModel):
    op: str
    # The submitted action, re-parsed with the Action adapter at completion.
    action: dict[str, Any]
    started_minute: int
    ends_minute: int


class CarriedObject(BaseModel):
    id: int
    kind: str
    name: str
    quantity: int
    slot: str


class ActionResult(BaseModel):
    accepted: bool
    actor_id: str
    action: Action
    x: float
    y: float
    z: int
    h: int = 0
    reason: str | None = None
    text: str | None = None
    activity: ActivityState | None = None
    carried: list[CarriedObject] = Field(default_factory=lambda: [])
    load_kg: float = 0.0
    trajectory: list[PhysicsPosition] = Field(default_factory=lambda: [])


class MenuEntry(BaseModel):
    op: str
    label: str
    tags: list[str]
    available: bool
    reason: str | None = None
    # What the op acts on, when it is not the tile itself ("Bottle ×3", "Bottle into Chest").
    subject: str | None = None
    # Submitted back unchanged, so the menu never offers what submit would build differently.
    action: Action
