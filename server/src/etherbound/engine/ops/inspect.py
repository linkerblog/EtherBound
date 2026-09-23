from math import hypot
from typing import ClassVar

from etherbound.engine.actions import Action, InspectAction, Target, TileTarget
from etherbound.engine.ops.base import ActionContext, Resolution, metres
from etherbound.engine.ops.dig import HAND_DIG_MAX_COST
from etherbound.events.models import Event

INSPECT_RANGE_M = 30


class InspectHandler:
    op: ClassVar[str] = "inspect"

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        return isinstance(target, TileTarget) and bool(
            ctx.grid.standing_surfaces(target.x, target.y)
        )

    def build(self, ctx: ActionContext, target: Target) -> Action:
        assert isinstance(target, TileTarget)
        return InspectAction(target=target)

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, InspectAction)
        target = action.target
        if not any(s.h == target.h for s in ctx.grid.standing_surfaces(target.x, target.y)):
            return "nothing to see"
        ax, ay = ctx.actor_tile()
        # No line of sight yet: that arrives with perception.
        if hypot(target.x - ax, target.y - ay) > INSPECT_RANGE_M:
            return "too far to see"
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return 0

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        # Looking changes nothing and emits no event; witnesses noticing it come later.
        assert isinstance(action, InspectAction)
        target = action.target
        surface = next(s for s in ctx.grid.standing_surfaces(target.x, target.y) if s.h == target.h)
        material = ctx.grid.registry.get(surface.material_id)
        name = material.name if material is not None else "Unknown"
        parts = [name, metres(surface.h)]
        ground = ctx.grid.ground_at(target.x, target.y)
        if ground is not None and ground[0] == surface.h and ground[2] > 0:
            parts.append(f"dug {metres(ground[2])}")
        # Only the surface is visible: the strata below are never revealed.
        visible: list[str] = []
        if material is not None:
            if material.diggable and material.dig_cost <= HAND_DIG_MAX_COST:
                visible.append("diggable")
            if material.flammable:
                visible.append("flammable")
            if material.liquid:
                visible.append("liquid")
        if visible:
            parts.append(", ".join(visible))
        return Resolution(text=" · ".join(parts))

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []
