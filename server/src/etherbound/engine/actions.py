from typing import Annotated, Any, Literal

from pydantic import BaseModel, Field


class SelfTarget(BaseModel):
    kind: Literal["self"] = "self"


class TileTarget(BaseModel):
    kind: Literal["tile"] = "tile"
    x: int
    y: int
    h: int


Target = Annotated[SelfTarget | TileTarget, Field(discriminator="kind")]


class MoveAction(BaseModel):
    op: Literal["move"] = "move"
    dx: float = Field(ge=-1, le=1)
    dy: float = Field(ge=-1, le=1)


class InspectAction(BaseModel):
    op: Literal["inspect"] = "inspect"
    target: TileTarget


class WaitAction(BaseModel):
    op: Literal["wait"] = "wait"
    target: SelfTarget = Field(default_factory=SelfTarget)


class DigAction(BaseModel):
    op: Literal["dig"] = "dig"
    target: TileTarget


class ClimbAction(BaseModel):
    op: Literal["climb"] = "climb"
    target: TileTarget


Action = Annotated[
    MoveAction | InspectAction | WaitAction | DigAction | ClimbAction,
    Field(discriminator="op"),
]


class ActivityState(BaseModel):
    op: str
    # The submitted action, re-parsed with the Action adapter at completion.
    action: dict[str, Any]
    started_minute: int
    ends_minute: int


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


class MenuEntry(BaseModel):
    op: str
    label: str
    tags: list[str]
    available: bool
    reason: str | None = None
    # Submitted back unchanged, so the menu never offers what submit would build differently.
    action: Action
