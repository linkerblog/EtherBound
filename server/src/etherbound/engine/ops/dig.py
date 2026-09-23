from math import ceil, floor
from typing import ClassVar

from sqlalchemy import select

from etherbound.db.models import Actor
from etherbound.db.models import Chunk as ChunkRow
from etherbound.engine.actions import Action, DigAction, Target, TileTarget
from etherbound.engine.ops.base import ActionContext, Resolution, in_close_reach
from etherbound.events.models import ActorMoved, ChunkChanged, Event, TerrainDug, TilePos
from etherbound.world.materials import Material

# Until tools exist, everyone digs as if with a shovel: about 0.5 m³ an hour in soft soil.
HAND_DIG_MAX_COST = 2.0
DIG_MINUTES_PER_COST = 30
DIG_REACH_H = 2


class DigHandler:
    op: ClassVar[str] = "dig"

    def _layers(self, ctx: ActionContext, x: int, y: int) -> tuple[int, int, Material, Material]:
        """``(ground_h, dug, removed, exposed)``; depth counts from the original ground."""
        ground = ctx.grid.ground_at(x, y)
        assert ground is not None
        ground_h, _, dug = ground
        registry = ctx.grid.registry
        removed = registry[ctx.grid.stratum_at(x, y, dug + 1)]
        exposed = registry[ctx.grid.stratum_at(x, y, dug + 2)]
        return ground_h, dug, removed, exposed

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if not isinstance(target, TileTarget):
            return False
        ground = ctx.grid.ground_at(target.x, target.y)
        # Only the ground surface is dug; a floor or roof above it is not ground.
        if ground is None or ground[0] != target.h:
            return False
        surface = ctx.grid.registry.get(ground[1])
        # Hides asphalt, water and glass; concrete and rock stay visible, as too hard.
        return surface is not None and surface.diggable

    def build(self, ctx: ActionContext, target: Target) -> Action:
        assert isinstance(target, TileTarget)
        return DigAction(target=target)

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, DigAction)
        x, y = action.target.x, action.target.y
        ground = ctx.grid.ground_at(x, y)
        if ground is None:
            return "out of reach"
        ground_h, _, removed, exposed = self._layers(ctx, x, y)
        surface = ctx.grid.registry[ground[1]]
        if not in_close_reach(ctx, x, y) or abs(ground_h - ctx.actor.h) > DIG_REACH_H:
            return "out of reach"
        if any(floor_h >= ground_h for floor_h in ctx.grid.floors_at(x, y)):
            return "floor in the way"
        if (
            not surface.diggable
            or surface.dig_cost > HAND_DIG_MAX_COST
            or not removed.diggable
            or removed.dig_cost > HAND_DIG_MAX_COST
        ):
            return "too hard to dig by hand"
        if not ctx.grid.solid_at(x, y, ground_h - 1):
            return "hollow below"
        if not exposed.walkable:
            return "hard layer below"
        if ctx.others_on(x, y, ground_h):
            return "someone is standing there"
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        assert isinstance(action, DigAction)
        _, _, removed, _ = self._layers(ctx, action.target.x, action.target.y)
        return ceil(DIG_MINUTES_PER_COST * removed.dig_cost)

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        raise NotImplementedError("dig is an activity; it completes, it never resolves")

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        assert isinstance(action, DigAction)
        x, y = action.target.x, action.target.y
        ground_h, _, removed, exposed = self._layers(ctx, x, y)
        # Whoever stands on the tile goes down with the ground, or the standing rule would
        # leave them floating 0.5 m up and frozen there.
        standing = [
            actor
            for actor in ctx.session.scalars(select(Actor).order_by(Actor.id))
            if (floor(actor.x), floor(actor.y), actor.h) == (x, y, ground_h)
        ]
        chunk = ctx.grid.lower_ground(x, y)
        row = ctx.session.get(ChunkRow, (chunk.cx, chunk.cy))
        assert row is not None
        row.ground_h = chunk.ground_blob
        row.surface_mat = chunk.surface_blob
        row.dug = chunk.dug_blob
        row.revision = chunk.revision
        new_h = ground_h - 1
        lowered = ctx.grid.ground_at(x, y)
        assert lowered is not None
        events: list[Event] = [
            TerrainDug(
                actor_id=ctx.actor.id,
                tile=TilePos(x=x, y=y, h=new_h),
                removed=removed.key,
                exposed=exposed.key,
                dug=lowered[2],
            )
        ]
        for actor in standing:
            from_tile = TilePos(x=x, y=y, h=actor.h)
            actor.h, actor.z = new_h, new_h // 6
            events.append(
                ActorMoved(
                    actor_id=actor.id,
                    from_tile=from_tile,
                    to_tile=TilePos(x=x, y=y, h=new_h),
                    mode="lowered",
                )
            )
        events.append(ChunkChanged(cx=chunk.cx, cy=chunk.cy, revision=chunk.revision))
        return events
