from collections.abc import Sequence
from dataclasses import replace
from math import floor
from typing import Any, ClassVar

from sqlalchemy import select

from etherbound.db.models import Actor, WallIntegrity
from etherbound.db.models import ChunkLevel as ChunkLevelRow
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
    children,
    location_of,
    object_total_mass,
    refresh_chunk_objects,
    set_tile,
)
from etherbound.engine.ops.base import (
    ActionContext,
    Resolution,
    in_close_reach,
    object_reach,
    within_reach_height,
)
from etherbound.engine.physics import (
    MAX_TILE_STEPS,
    absorb_energy,
    integrity_capacity,
    kinetic_energy,
    potential_energy,
    shove_impulse,
    strike_energy,
    throw_speed,
    travel_energy_cost,
)
from etherbound.events.models import (
    ActorMoved,
    Event,
    Impact,
    ObjectChanged,
    ObjectMoved,
    PhysicsResolved,
    TilePos,
)
from etherbound.world.chunk import (
    CHUNK_SIZE,
    EDGE_N_DOORWAY,
    EDGE_W_DOORWAY,
    NO_FLOOR,
    ChunkLevel,
)
from etherbound.world.grid import WorldGrid

_DIRECTIONS = ((0, -1), (1, 0), (0, 1), (-1, 0))
_DIRECTION_NAMES = {(0, -1): "north", (1, 0): "east", (0, 1): "south", (-1, 0): "west"}


def _sign(value: int) -> int:
    return (value > 0) - (value < 0)


def _cardinal(dx: int, dy: int) -> bool:
    return abs(dx) + abs(dy) == 1


def _canonical_edge(target: EdgeTarget) -> EdgeTarget:
    if target.direction == "south":
        return target.model_copy(update={"y": target.y + 1, "direction": "north"})
    if target.direction == "east":
        return target.model_copy(update={"x": target.x + 1, "direction": "west"})
    return target


def _edge_cell(ctx: ActionContext, target: EdgeTarget) -> tuple[ChunkLevel, int, int] | None:
    edge = _canonical_edge(target)
    cx, cy, local_x, local_y = WorldGrid.chunk_coords(edge.x, edge.y)
    level = ctx.grid.level(cx, cy, edge.z)
    if level is None:
        return None
    index = level.index(local_x, local_y)
    material_id = level.wall_n[index] if edge.direction == "north" else level.wall_w[index]
    if material_id == 0:
        return None
    return level, index, material_id


def _wall_bottom(ctx: ActionContext, level: ChunkLevel, index: int) -> int:
    bottom = level.floor_h[index]
    if bottom != NO_FLOOR:
        return bottom
    chunk = ctx.grid.chunk(level.cx, level.cy)
    if chunk is None:
        return level.z * 6
    supports = [chunk.ground_h[index]]
    supports.extend(
        lower.floor_h[index]
        for lower in ctx.grid.levels.values()
        if lower.cx == level.cx
        and lower.cy == level.cy
        and lower.z < level.z
        and lower.floor_h[index] != NO_FLOOR
    )
    below_level = [support for support in supports if support < (level.z + 1) * 6]
    return max(below_level, default=level.z * 6)


def _edge_reachable(ctx: ActionContext, target: EdgeTarget) -> bool:
    edge = _canonical_edge(target)
    record = _edge_cell(ctx, edge)
    if record is None:
        return False
    level, index, _ = record
    bottom = _wall_bottom(ctx, level, index)
    ax, ay = ctx.actor_tile()
    sides = (
        ((edge.x, edge.y), (edge.x, edge.y - 1))
        if edge.direction == "north"
        else ((edge.x, edge.y), (edge.x - 1, edge.y))
    )
    return (
        any((ax, ay) == side for side in sides)
        and bottom <= ctx.actor.h + 3
        and bottom + 6 >= (ctx.actor.h - 2)
    )


def _edge_for_step(x: int, y: int, z: int, dx: int, dy: int) -> EdgeTarget:
    if (dx, dy) == (0, -1):
        return EdgeTarget(x=x, y=y, z=z, direction="north")
    if (dx, dy) == (0, 1):
        return EdgeTarget(x=x, y=y + 1, z=z, direction="north")
    if (dx, dy) == (-1, 0):
        return EdgeTarget(x=x, y=y, z=z, direction="west")
    return EdgeTarget(x=x + 1, y=y, z=z, direction="west")


def _wall_blocking_step(
    ctx: ActionContext, x: int, y: int, h: int, dx: int, dy: int
) -> EdgeTarget | None:
    nx, ny = x + dx, y + dy
    if not ctx.grid.wall_between(x, y, nx, ny, h):
        return None
    edge = _canonical_edge(_edge_for_step(x, y, h // 6, dx, dy))
    cx, cy, local_x, local_y = WorldGrid.chunk_coords(edge.x, edge.y)
    chunk = ctx.grid.chunk(cx, cy)
    if chunk is None:
        return edge
    index = local_y * CHUNK_SIZE + local_x
    for level in sorted(
        (level for level in ctx.grid.levels.values() if (level.cx, level.cy) == (cx, cy)),
        key=lambda item: item.z,
    ):
        material_id = level.wall_n[index] if edge.direction == "north" else level.wall_w[index]
        if material_id == 0:
            continue
        flags = level.edge_flags[index]
        doorway_flag = EDGE_N_DOORWAY if edge.direction == "north" else EDGE_W_DOORWAY
        if flags & doorway_flag:
            continue
        bottom = _wall_bottom(ctx, level, index)
        if bottom < h + 4 and bottom + 6 > h:
            return edge.model_copy(update={"z": level.z})
    return edge


def _surface_h(grid: WorldGrid, x: int, y: int, current_h: int, *, body: bool) -> int | None:
    surfaces = grid.standing_surfaces(x, y) if body else grid.resting_surfaces(x, y)
    candidates = [surface.h for surface in surfaces if surface.h <= current_h + 1]
    return max(candidates, default=None)


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
            return _edge_cell(ctx, target) is not None and self.op_name in {"hit", "break"}
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
            dx, dy = _sign(tx - ax), _sign(ty - ay)
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
            label = actor.kind if actor is not None else None
        else:
            record = _edge_cell(ctx, target)
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
            if not _cardinal(action.dx, action.dy):
                return "choose one direction"
            position = self._target_tile(ctx, target)
            if position is None:
                return "nothing there"
            tx, ty, _ = position
            ax, ay = ctx.actor_tile()
            if abs(tx - ax) + abs(ty - ay) != 1:
                return "out of reach"
            expected = (_sign(tx - ax), _sign(ty - ay))
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
            if not _cardinal(action.dx, action.dy):
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
            if _edge_cell(ctx, target) is None:
                return "nothing there"
            return None if _edge_reachable(ctx, target) else "out of reach"
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
            trajectory = self._travel(
                ctx, row, action.dx, action.dy, energy, mass, events, damage, broken, changed_chunks
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
            trajectory = self._travel(
                ctx,
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
                self._damage_wall(ctx, target, energy, events, damage, broken, changed_chunks)
            elif isinstance(target, ObjectTarget):
                row = self._object(ctx, target)
                assert row is not None
                self._damage_object(ctx, row, energy, events, damage, broken, changed_chunks)
            else:
                other = self._actor(ctx, target)
                assert other is not None
                ax, ay = ctx.actor_tile()
                dx, dy = _sign(floor(other.x) - ax), _sign(floor(other.y) - ay)
                trajectory = self._travel(
                    ctx,
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

    def _damage_object(
        self,
        ctx: ActionContext,
        row: ObjectRow,
        energy: float,
        events: list[Event],
        damage: list[dict[str, Any]],
        broken: list[dict[str, Any]],
        changed_chunks: set[tuple[int, int]],
        *,
        emit_impact: bool = True,
    ) -> float:
        kind = ctx.grid.catalog[row.kind]
        material = ctx.grid.registry[kind.material]
        capacity = integrity_capacity(material.resistance, kind.height) * row.quantity
        remaining = row.integrity if row.integrity is not None else capacity
        absorbed, left = absorb_energy(energy, remaining)
        if emit_impact:
            events.append(
                Impact(
                    actor_id=ctx.actor.id, target={"kind": "object", "id": row.id}, energy=energy
                )
            )
        record: dict[str, Any] = {
            "target": {"kind": "object", "id": row.id},
            "absorbed_j": absorbed,
            "remaining_j": max(0.0, remaining - absorbed),
        }
        if left > 0 or energy >= remaining:
            assert (
                row.loc == "tile" and row.x is not None and row.y is not None and row.h is not None
            )
            x, y, h = row.x, row.y, row.h
            if row.kind == "rubble":
                ctx.session.delete(row)
                rubble_id = None
                events.append(
                    ObjectChanged(
                        actor_id=ctx.actor.id,
                        object_id=row.id,
                        kind=row.kind,
                        op=self.op_name,
                        changes={"removed": True},
                    )
                )
            else:
                contents = children(ctx.session, row.id)
                for child in contents:
                    old = location_of(child)
                    set_tile(child, x, y, h)
                    events.append(
                        ObjectMoved(
                            actor_id=ctx.actor.id,
                            object_id=child.id,
                            kind=child.kind,
                            quantity=child.quantity,
                            op=self.op_name,
                            from_=old,
                            to=location_of(child),
                        )
                    )
                row.kind = "rubble"
                row.state = {}
                row.integrity = None
                rubble_id = row.id
                events.append(
                    ObjectChanged(
                        actor_id=ctx.actor.id,
                        object_id=row.id,
                        kind="rubble",
                        op=self.op_name,
                        changes={"kind": "rubble", "integrity": None},
                    )
                )
            broken.append({"kind": "object", "id": row.id, "rubble_id": rubble_id})
            record["remaining_j"] = 0.0
            changed_chunks.add((x // CHUNK_SIZE, y // CHUNK_SIZE))
            ctx.session.flush()
            refresh_chunk_objects(ctx, x // CHUNK_SIZE, y // CHUNK_SIZE)
        else:
            row.integrity = remaining - absorbed
            events.append(
                ObjectChanged(
                    actor_id=ctx.actor.id,
                    object_id=row.id,
                    kind=row.kind,
                    op=self.op_name,
                    changes={"integrity": row.integrity},
                )
            )
            if row.x is not None and row.y is not None:
                changed_chunks.add((row.x // CHUNK_SIZE, row.y // CHUNK_SIZE))
        damage.append(record)
        return left

    def _damage_wall(
        self,
        ctx: ActionContext,
        target: EdgeTarget,
        energy: float,
        events: list[Event],
        damage: list[dict[str, Any]],
        broken: list[dict[str, Any]],
        changed_chunks: set[tuple[int, int]],
    ) -> float:
        edge = _canonical_edge(target)
        record = _edge_cell(ctx, edge)
        if record is None:
            return 0.0
        level, index, material_id = record
        material = ctx.grid.registry.get(material_id)
        if material is None:
            return 0.0
        key = (level.cx, level.cy, level.z, index, edge.direction)
        row = ctx.session.get(WallIntegrity, key)
        capacity = integrity_capacity(material.resistance, 6)
        remaining = row.integrity if row is not None else capacity
        absorbed, left = absorb_energy(energy, remaining)
        events.append(
            Impact(
                actor_id=ctx.actor.id,
                target=edge.model_dump(mode="json"),
                energy=energy,
            )
        )
        data = {
            "target": edge.model_dump(mode="json"),
            "absorbed_j": absorbed,
            "remaining_j": max(0.0, remaining - absorbed),
        }
        if energy >= remaining:
            if row is not None:
                ctx.session.delete(row)
            walls = list(level.wall_n if edge.direction == "north" else level.wall_w)
            walls[index] = 0
            updated = replace(
                level,
                wall_n=tuple(walls) if edge.direction == "north" else level.wall_n,
                wall_w=tuple(walls) if edge.direction == "west" else level.wall_w,
            )
            ctx.grid.add_level(updated)
            db_level = ctx.session.get(ChunkLevelRow, (level.cx, level.cy, level.z))
            if db_level is not None:
                db_level.wall_n = updated.wall_n_blob
                db_level.wall_w = updated.wall_w_blob
            broken.append({"kind": "wall", **edge.model_dump(mode="json")})
            data["remaining_j"] = 0.0
            changed_chunks.add((level.cx, level.cy))
        else:
            if row is None:
                row = WallIntegrity(
                    cx=level.cx,
                    cy=level.cy,
                    z=level.z,
                    cell_index=index,
                    edge=edge.direction,
                    integrity=remaining - absorbed,
                )
                ctx.session.add(row)
            else:
                row.integrity = remaining - absorbed
        damage.append(data)
        return left

    def _travel(
        self,
        ctx: ActionContext,
        mover: ObjectRow | Actor,
        dx: int,
        dy: int,
        energy: float,
        mass: float,
        events: list[Event],
        damage: list[dict[str, Any]],
        broken: list[dict[str, Any]],
        changed_chunks: set[tuple[int, int]],
    ) -> list[PhysicsPosition]:
        is_body = isinstance(mover, Actor)
        if is_body:
            x, y, h = floor(mover.x), floor(mover.y), mover.h
            entity_kind, entity_id = "actor", mover.id
        else:
            assert mover.x is not None and mover.y is not None and mover.h is not None
            x, y, h = mover.x, mover.y, mover.h
            entity_kind, entity_id = "object", mover.id
        path = [PhysicsPosition(kind=entity_kind, id=entity_id, x=x, y=y, h=h)]
        for _ in range(MAX_TILE_STEPS):
            cost = travel_energy_cost(mass)
            if energy < cost:
                break
            nx, ny = x + dx, y + dy
            wall = _wall_blocking_step(ctx, x, y, h, dx, dy)
            if wall is not None:
                energy = self._damage_wall(
                    ctx, wall, energy, events, damage, broken, changed_chunks
                )
                if energy <= 0:
                    break
                if _wall_blocking_step(ctx, x, y, h, dx, dy) is not None:
                    break
            obstacle = self._solid_object_at(ctx, nx, ny, h, mover)
            if obstacle is not None:
                energy = self._damage_object(
                    ctx, obstacle, energy, events, damage, broken, changed_chunks
                )
                if energy <= 0:
                    break
                if self._solid_object_at(ctx, nx, ny, h, mover) is not None:
                    break
            other_actor = self._actor_at(ctx, nx, ny, h, mover)
            if other_actor is not None:
                events.append(
                    Impact(
                        actor_id=ctx.actor.id,
                        target={"kind": "actor", "id": other_actor.id},
                        energy=energy,
                    )
                )
                damage.append(
                    {"target": {"kind": "actor", "id": other_actor.id}, "impact_j": energy}
                )
                break
            mover_height = 3 if is_body else ctx.grid.catalog[mover.kind].height
            if self._terrain_blocks(ctx.grid, nx, ny, h, mover_height):
                events.append(
                    Impact(
                        actor_id=ctx.actor.id,
                        target={"kind": "terrain", "x": nx, "y": ny, "h": h},
                        energy=energy,
                    )
                )
                damage.append(
                    {"target": {"kind": "terrain", "x": nx, "y": ny, "h": h}, "impact_j": energy}
                )
                break
            next_h = _surface_h(ctx.grid, nx, ny, h, body=is_body)
            if next_h is None:
                break
            fall_height_m = max(0.0, (h - next_h) * 0.5)
            x, y, h = nx, ny, next_h
            path.append(PhysicsPosition(kind=entity_kind, id=entity_id, x=x, y=y, h=h))
            energy -= cost
            if fall_height_m:
                fall_energy = potential_energy(mass, fall_height_m)
                if not is_body or fall_height_m > 3.0:
                    events.append(
                        Impact(
                            actor_id=ctx.actor.id,
                            target={"kind": entity_kind, "id": entity_id},
                            energy=fall_energy,
                        )
                    )
                    damage.append(
                        {"target": {"kind": entity_kind, "id": entity_id}, "fall_j": fall_energy}
                    )
                    if not is_body:
                        assert isinstance(mover, ObjectRow)
                        set_tile(mover, x, y, h)
                        ctx.session.flush()
                        changed_chunks.add((x // CHUNK_SIZE, y // CHUNK_SIZE))
                        refresh_chunk_objects(ctx, x // CHUNK_SIZE, y // CHUNK_SIZE)
                        self._damage_object(
                            ctx,
                            mover,
                            fall_energy,
                            events,
                            damage,
                            broken,
                            changed_chunks,
                            emit_impact=False,
                        )
            if energy <= 0:
                break
        return path

    @staticmethod
    def _terrain_blocks(grid: WorldGrid, x: int, y: int, h: int, height: int) -> bool:
        return any(grid.terrain_solid_at(x, y, h + offset) for offset in range(1, height + 1))

    def _solid_object_at(
        self, ctx: ActionContext, x: int, y: int, h: int, mover: ObjectRow | Actor
    ) -> ObjectRow | None:
        for row in ctx.session.scalars(
            select(ObjectRow)
            .where(ObjectRow.loc == "tile", ObjectRow.x == x, ObjectRow.y == y)
            .order_by(ObjectRow.id)
        ):
            if isinstance(mover, ObjectRow) and row.id == mover.id:
                continue
            kind = ctx.grid.catalog[row.kind]
            mover_height = (
                3 if isinstance(mover, Actor) else max(ctx.grid.catalog[mover.kind].height, 1)
            )
            if (
                kind.solid
                and row.h is not None
                and row.h < h + mover_height
                and row.h + kind.height > h
            ):
                return row
        return None

    @staticmethod
    def _actor_at(
        ctx: ActionContext, x: int, y: int, h: int, mover: ObjectRow | Actor
    ) -> Actor | None:
        for actor in ctx.session.scalars(select(Actor).order_by(Actor.id)):
            if isinstance(mover, Actor) and actor.id == mover.id:
                continue
            if (floor(actor.x), floor(actor.y), actor.h) == (x, y, h):
                return actor
        return None


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
