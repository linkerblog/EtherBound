from typing import ClassVar

from etherbound.engine.actions import Action, SelfTarget, Target, WaitAction
from etherbound.engine.ops.base import ActionContext, Resolution
from etherbound.events.models import Event

WAIT_MINUTES = 15


class WaitHandler:
    op: ClassVar[str] = "wait"

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        return isinstance(target, SelfTarget)

    def build(self, ctx: ActionContext, target: Target) -> Action:
        return WaitAction()

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return WAIT_MINUTES

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        return Resolution()

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        # Waiting leaves no mark on the world; activity.finished is the whole record.
        return []
