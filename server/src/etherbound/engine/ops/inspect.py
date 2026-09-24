from collections.abc import Sequence
from math import hypot
from typing import ClassVar

from etherbound.db.models import Actor
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import (
    Action,
    ActorTarget,
    InspectAction,
    ObjectTarget,
    Target,
    TileTarget,
)
from etherbound.engine.objects import children, object_total_mass
from etherbound.engine.ops.base import ActionContext, Resolution, actor_name, metres
from etherbound.engine.ops.dig import HAND_DIG_MAX_COST
from etherbound.events.models import Event

INSPECT_RANGE_M = 30


class InspectHandler:
    op: ClassVar[str] = "inspect"

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if isinstance(target, ActorTarget):
            return ctx.session.get(Actor, target.id) is not None
        if isinstance(target, ObjectTarget):
            return ctx.session.get(ObjectRow, target.id) is not None
        return isinstance(target, TileTarget) and bool(
            ctx.grid.standing_surfaces(target.x, target.y)
        )

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        return (InspectAction(target=target),)  # type: ignore[arg-type]

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, InspectAction)
        if isinstance(action.target, ActorTarget):
            actor = ctx.session.get(Actor, action.target.id)
            return actor_name(actor) if actor is not None else None
        if isinstance(action.target, ObjectTarget):
            row = ctx.session.get(ObjectRow, action.target.id)
            if row is not None:
                kind = ctx.grid.catalog.get(row.kind)
                if kind is not None:
                    return kind.name
        return None

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, InspectAction)
        target = action.target
        ax, ay = ctx.actor_tile()
        if isinstance(target, ActorTarget):
            actor = ctx.session.get(Actor, target.id)
            if actor is None:
                return "nobody there"
            if hypot(actor.x - ax, actor.y - ay) > INSPECT_RANGE_M:
                return "too far to see"
            return None
        if isinstance(target, ObjectTarget):
            row = ctx.session.get(ObjectRow, target.id)
            if row is None:
                return "nothing to see"
            if row.loc == "tile":
                if row.x is None or row.y is None:
                    return "nothing to see"
                if hypot(row.x - ax, row.y - ay) > INSPECT_RANGE_M:
                    return "too far to see"
            return None
        if not any(s.h == target.h for s in ctx.grid.standing_surfaces(target.x, target.y)):
            return "nothing to see"
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
        if isinstance(target, ActorTarget):
            return Resolution(text=self._actor_text(ctx, target.id))
        if isinstance(target, ObjectTarget):
            return Resolution(text=self._object_text(ctx, target.id))
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

    def _actor_text(self, ctx: ActionContext, actor_id: str) -> str:
        actor = ctx.session.get(Actor, actor_id)
        if actor is None:
            return "Unknown"
        activity = actor.activity or {}
        if activity.get("op") == "wait":
            state = "Waiting"
        elif (actor.mind or {}).get("goal") is not None:
            state = "Walking"
        else:
            state = "Standing"
        return f"{actor_name(actor)}. {state}."

    def _object_text(self, ctx: ActionContext, object_id: int) -> str:
        row = ctx.session.get(ObjectRow, object_id)
        if row is None:
            return "Unknown"
        kind = ctx.grid.catalog[row.kind]
        material = ctx.grid.registry.get(kind.material)
        parts = [kind.name, material.name if material is not None else kind.material]
        if kind.openable:
            parts.append("open" if bool((row.state or {}).get("open", False)) else "closed")
        if kind.container is not None and (
            not kind.openable or bool((row.state or {}).get("open"))
        ):
            parts.append(f"holds {len(children(ctx.session, row.id))} things")
        if row.loc in ("held", "worn"):
            parts.append(f"{object_total_mass(ctx.session, ctx.grid.catalog, row):.1f} kg")
        return " · ".join(parts)

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []
