from typing import Annotated, Literal

from pydantic import BaseModel, Field


class MoveAction(BaseModel):
    verb: Literal["move"] = "move"
    dx: float = Field(ge=-1, le=1)
    dy: float = Field(ge=-1, le=1)


Action = Annotated[MoveAction, Field(discriminator="verb")]


class ActionResult(BaseModel):
    accepted: bool
    actor_id: str
    action: Action
    x: float
    y: float
    z: int
    h: int = 0
    reason: str | None = None
