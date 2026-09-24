from collections.abc import Sequence
from math import floor
from typing import Any, ClassVar

from sqlalchemy import select

from etherbound.db.models import Actor
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import (
    Action,
    ActorTarget,
    BreakAction,
    DragAction,
    EdgeTarget,
    HitAction,
    Location,
    ObjectTarget,
    PhysicsPosition,
    PullAction,
    PushAction,
    Target,
    ThrowAction,
    TileLoc,
)
from etherbound.engine.objects import (
    bump_chunk,
    location_of,
    object_total_mass,
    set_tile,
)
from etherbound.engine.ops.base import (
    ActionContext,
    Resolution,
    actor_name,
    in_close_reach,
    object_reach,
    within_reach_height,
)
from etherbound.engine.ops.damage import damage_object, damage_wall
from etherbound.engine.ops.edges import cardinal, edge_cell, edge_reachable, sign
from etherbound.engine.ops.travel import travel
from etherbound.engine.physics import (
    kinetic_energy,
    shove_impulse,
    strike_energy,
    throw_speed,
)
from etherbound.events.models import (
    ActorMoved,
    Event,
    ObjectMoved,
    PhysicsResolved,
    TilePos,
)
from etherbound.world.chunk import CHUNK_SIZE

_DIRECTIONS = ((0, -1), (1, 0), (0, 1), (-1, 0))
_DIRECTION_NAMES = {(0, -1): "north", (1, 0): "east", (0, 1): "south", (-1, 0): "west"}


class PhysicsHandler:
    """Resolve force and material damage atomically in the engine's action transaction."""

    def __init__(self, op: str) -> None:
        self._op = op

    @property
    def op_name(self) -> str:
        return self._op

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if self.op_name == "throw":
            return isinstance(target, ObjectTarget) and self._object(ctx, target) is not None
        if self.op_name in {"push", "pull", "drag"}:
            if isinstance(target, ObjectTarget):
                row = self._object(ctx, target)
                return row is not None and row.loc == "tile"
            if isinstance(target, ActorTarget):
                return self._actor(ctx, target) is not None
            return False
        if isinstance(target, EdgeTarget):
            return edge_cell(ctx, target) is not None and self.op_name in {"hit", "break"}
        if isinstance(target, ObjectTarget):
            row = self._object(ctx, target)
            return row is not None and row.loc == "tile" and self.op_name in {"hit", "break"}
        return (
            isinstance(target, ActorTarget)
            and self._actor(ctx, target) is not None
            and self.op_name == "hit"
        )

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        if self.op_name == "throw":
            assert isinstance(target, ObjectTarget)
            row = self._object(ctx, target)
            if row is None or row.loc != "held" or row.actor_id != ctx.actor.id:
                return ()
            return tuple(ThrowAction(target=target, dx=dx, dy=dy) for dx, dy in _DIRECTIONS)
        if self.op_name in {"push", "pull", "drag"}:
            if not isinstance(target, (ObjectTarget, ActorTarget)):
                return ()
            position = self._target_tile(ctx, target)
            if position is None:
                return ()
            tx, ty, _ = position
            ax, ay = ctx.actor_tile()
            dx, dy = sign(tx - ax), sign(ty - ay)
            if abs(tx - ax) + abs(ty - ay) != 1:
                return ()
            if self.op_name in {"pull", "drag"}:
                dx, dy = -dx, -dy
            if self.op_name == "push":
                return (PushAction(target=target, dx=dx, dy=dy),)
            if self.op_name == "pull":
                return (PullAction(target=target, dx=dx, dy=dy),)
            return (DragAction(target=target, dx=dx, dy=dy),)
        tools: tuple[ObjectTarget | None, ...] = (None,) + tuple(
            ObjectTarget(id=row.id)
            for row in ctx.session.scalars(
                select(ObjectRow)
                .where(ObjectRow.loc == "held", ObjectRow.actor_id == ctx.actor.id)
                .order_by(ObjectRow.id)
            )
            if (tool := ctx.grid.catalog[row.kind].tool) is not None
            and tool.strike_speed_m_s is not None
        )
        if self.op_name == "hit" and isinstance(target, (ObjectTarget, ActorTarget, EdgeTarget)):
            return tuple(HitAction(target=target, tool=tool) for tool in tools)
        if self.op_name == "break" and isinstance(target, (ObjectTarget, EdgeTarget)):
            return tuple(BreakAction(target=target, tool=tool) for tool in tools)
        return ()

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        if not isinstance(
            action, (PushAction, PullAction, DragAction, ThrowAction, HitAction, BreakAction)
        ):
            return None
        target = action.target
        label: str | None = None
        if isinstance(target, ObjectTarget):
            row = self._object(ctx, target)
            label = self._object_label(ctx, row) if row is not None else None
        elif isinstance(target, ActorTarget):
            actor = self._actor(ctx, target)
            label = actor_name(actor) if actor is not None else None
        else:
            record = edge_cell(ctx, target)
            material = ctx.grid.registry.get(record[2]) if record is not None else None
            label = f"{material.name} wall" if material is not None else "wall"
        if isinstance(action, (PushAction, PullAction, DragAction, ThrowAction)):
            label = f"{label} {_DIRECTION_NAMES[(action.dx, action.dy)]}" if label else None
        elif action.tool is not None:
            tool = self._object(ctx, action.tool)
            if tool is not None:
                label = f"{label} with {ctx.grid.catalog[tool.kind].name}" if label else None
        return label

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        if not isinstance(
            action, (PushAction, PullAction, DragAction, ThrowAction, HitAction, BreakAction)
        ):
            return "invalid physics action"
        if ctx.actor.mass_kg <= 0:
            return "invalid body mass"
        target = action.target
        if isinstance(action, (PushAction, PullAction, DragAction)):
            if not cardinal(action.dx, action.dy):
                return "choose one direction"
            position = self._target_tile(ctx, target)
            if position is None:
                return "nothing there"
            tx, ty, _ = position
            ax, ay = ctx.actor_tile()
            if abs(tx - ax) + abs(ty - ay) != 1:
                return "out of reach"
            expected = (sign(tx - ax), sign(ty - ay))
            if self.op_name in {"pull", "drag"}:
                expected = (-expected[0], -expected[1])
            if (action.dx, action.dy) != expected:
                return "cannot move it that way"
            if isinstance(target, ObjectTarget):
                row = self._object(ctx, target)
                if row is None or row.loc != "tile":
                    return "nothing there"
                if ctx.grid.catalog[row.kind].fixed:
                    return "fixed in place"
            elif isinstance(target, ActorTarget) and self._actor(ctx, target) is None:
                return "nobody there"
            elif isinstance(target, ActorTarget):
                other = self._actor(ctx, target)
                assert other is not None
                if not in_close_reach(
                    ctx, floor(other.x), floor(other.y)
                ) or not within_reach_height(ctx, other.h):
                    return "out of reach"
            if isinstance(target, ObjectTarget):
                row = self._object(ctx, target)
                if row is not None and not object_reach(ctx, row):
                    return "out of reach"
            return None
        if isinstance(action, ThrowAction):
            if not cardinal(action.dx, action.dy):
                return "choose one direction"
            row = self._object(ctx, action.target)
            if row is None or row.loc != "held" or row.actor_id != ctx.actor.id:
                return "not holding it"
            return None
        if action.tool is not None:
            tool = self._object(ctx, action.tool)
            if tool is None or tool.loc != "held" or tool.actor_id != ctx.actor.id:
                return "not holding the tool"
            tool_kind = ctx.grid.catalog[tool.kind].tool
            if tool_kind is None or tool_kind.strike_speed_m_s is None:
                return "not a striking tool"
        if isinstance(target, EdgeTarget):
            if edge_cell(ctx, target) is None:
                return "nothing there"
            return None if edge_reachable(ctx, target) else "out of reach"
        if isinstance(target, ObjectTarget):
            row = self._object(ctx, target)
            if row is None or row.loc != "tile":
                return "nothing there"
            if not object_reach(ctx, row):
                return "out of reach"
            return None
        other = self._actor(ctx, target)
        if other is None:
            return "nobody there"
        ax, ay = ctx.actor_tile()
        tx, ty = floor(other.x), floor(other.y)
        if abs(tx - ax) + abs(ty - ay) != 1:
            return "out of reach"
        if not in_close_reach(ctx, tx, ty) or not within_reach_height(ctx, other.h):
            return "out of reach"
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return 0

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        events: list[Event] = []
        damage: list[dict[str, Any]] = []
        broken: list[dict[str, object]] = []
        changed_chunks: set[tuple[int, int]] = set()
        trajectory: list[PhysicsPosition] = []
        moved: ObjectRow | Actor | None = None
        original_location: Location | None = None

        if isinstance(action, ThrowAction):
            row = self._object(ctx, action.target)
            assert row is not None
            original_location = location_of(row)
            x, y = ctx.actor_tile()
            set_tile(row, x, y, ctx.actor.h)
            ctx.session.flush()
            moved = row
            changed_chunks.add((x // CHUNK_SIZE, y // CHUNK_SIZE))
            mass = object_total_mass(ctx.session, ctx.grid.catalog, row)
            speed = throw_speed(mass)
            energy = kinetic_energy(mass, speed)
            trajectory = travel(
                ctx,
                self.op_name,
                row,
                action.dx,
                action.dy,
                energy,
                mass,
                events,
                damage,
                broken,
                changed_chunks,
            )
        elif isinstance(action, (PushAction, PullAction, DragAction)):
            position = self._target_tile(ctx, action.target)
            assert position is not None
            if isinstance(action.target, ObjectTarget):
                row = self._object(ctx, action.target)
                assert row is not None
                moved = row
                original_location = location_of(row)
                mass = object_total_mass(ctx.session, ctx.grid.catalog, row)
            else:
                target_actor = self._actor(ctx, action.target)
                assert target_actor is not None
                moved = target_actor
                mass = target_actor.mass_kg
            impulse = shove_impulse(ctx.actor.mass_kg, mass)
            energy = kinetic_energy(mass, impulse / mass)
            trajectory = travel(
                ctx,
                self.op_name,
                moved,
                action.dx,
                action.dy,
                energy,
                mass,
                events,
                damage,
                broken,
                changed_chunks,
            )
        elif isinstance(action, (HitAction, BreakAction)):
            energy = self._strike(ctx, action.tool)
            target = action.target
            if isinstance(target, EdgeTarget):
                damage_wall(
                    ctx, self.op_name, target, energy, events, damage, broken, changed_chunks
                )
            elif isinstance(target, ObjectTarget):
                row = self._object(ctx, target)
                assert row is not None
                damage_object(
                    ctx, self.op_name, row, energy, events, damage, broken, changed_chunks
                )
            else:
                other = self._actor(ctx, target)
                assert other is not None
                ax, ay = ctx.actor_tile()
                dx, dy = sign(floor(other.x) - ax), sign(floor(other.y) - ay)
                trajectory = travel(
                    ctx,
                    self.op_name,
                    other,
                    dx,
                    dy,
                    energy,
                    other.mass_kg,
                    events,
                    damage,
                    broken,
                    changed_chunks,
                )
                moved = other
        else:
            raise TypeError(f"unsupported physics action: {type(action).__name__}")

        if moved is not None and trajectory:
            last = trajectory[-1]
            if isinstance(moved, ObjectRow):
                if not isinstance(original_location, TileLoc) or (moved.x, moved.y, moved.h) != (
                    int(last.x),
                    int(last.y),
                    last.h,
                ):
                    from_loc = original_location or location_of(moved)
                    assert from_loc is not None
                    set_tile(moved, int(last.x), int(last.y), last.h)
                    ctx.session.flush()
                    events.append(
                        ObjectMoved(
                            actor_id=ctx.actor.id,
                            object_id=moved.id,
                            kind=moved.kind,
                            quantity=moved.quantity,
                            op=self.op_name,
                            from_=from_loc,
                            to=location_of(moved),
                        )
                    )
                    if isinstance(from_loc, TileLoc):
                        changed_chunks.add(
                            (int(from_loc.x) // CHUNK_SIZE, int(from_loc.y) // CHUNK_SIZE)
                        )
                    changed_chunks.add((int(last.x) // CHUNK_SIZE, int(last.y) // CHUNK_SIZE))
            elif (floor(moved.x), floor(moved.y), moved.h) != (last.x, last.y, last.h):
                before = TilePos(x=floor(moved.x), y=floor(moved.y), h=moved.h)
                moved.x, moved.y, moved.h = last.x + 0.5, last.y + 0.5, last.h
                moved.z = last.h // 6
                events.append(
                    ActorMoved(
                        actor_id=moved.id,
                        from_tile=before,
                        to_tile=TilePos(x=int(last.x), y=int(last.y), h=last.h),
                        mode="physics",
                    )
                )
        ctx.session.flush()
        events.append(
            PhysicsResolved(
                actor_id=ctx.actor.id,
                op=self.op_name,
                trajectory=trajectory,
                damage=damage,
                broken=broken,
            )
        )
        for cx, cy in sorted(changed_chunks):
            events.append(bump_chunk(ctx, cx, cy))
        return Resolution(events=events, trajectory=trajectory)

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []

    def _object(self, ctx: ActionContext, target: ObjectTarget) -> ObjectRow | None:
        return ctx.session.get(ObjectRow, target.id)

    def _actor(self, ctx: ActionContext, target: ActorTarget) -> Actor | None:
        row = ctx.session.get(Actor, target.id)
        return row if row is not None and row.id != ctx.actor.id and row.mass_kg > 0 else None

    def _target_tile(self, ctx: ActionContext, target: Target) -> tuple[int, int, int] | None:
        if isinstance(target, ObjectTarget):
            row = self._object(ctx, target)
            if row is None or row.loc != "tile" or row.x is None or row.y is None or row.h is None:
                return None
            return row.x, row.y, row.h
        if isinstance(target, ActorTarget):
            actor = self._actor(ctx, target)
            if actor is None:
                return None
            return floor(actor.x), floor(actor.y), actor.h
        return None

    def _object_label(self, ctx: ActionContext, row: ObjectRow) -> str:
        kind = ctx.grid.catalog[row.kind]
        return f"{kind.name} ×{row.quantity}" if row.quantity > 1 else kind.name

    def _strike(self, ctx: ActionContext, target: ObjectTarget | None) -> float:
        if target is None:
            return strike_energy(None)
        row = self._object(ctx, target)
        tool = ctx.grid.catalog[row.kind].tool if row is not None else None
        if row is None or tool is None or tool.strike_speed_m_s is None:
            return strike_energy(None)
        return strike_energy(
            object_total_mass(ctx.session, ctx.grid.catalog, row), tool.strike_speed_m_s
        )


class PushHandler(PhysicsHandler):
    op: ClassVar[str] = "push"

    def __init__(self) -> None:
        super().__init__(self.op)


class PullHandler(PhysicsHandler):
    op: ClassVar[str] = "pull"

    def __init__(self) -> None:
        super().__init__(self.op)


class DragHandler(PhysicsHandler):
    op: ClassVar[str] = "drag"

    def __init__(self) -> None:
        super().__init__(self.op)


class ThrowHandler(PhysicsHandler):
    op: ClassVar[str] = "throw"

    def __init__(self) -> None:
        super().__init__(self.op)


class HitHandler(PhysicsHandler):
    op: ClassVar[str] = "hit"

    def __init__(self) -> None:
        super().__init__(self.op)


class BreakHandler(PhysicsHandler):
    op: ClassVar[str] = "break"

    def __init__(self) -> None:
        super().__init__(self.op)
