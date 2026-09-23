"""The handling ops: take, drop, put, open, close, wear, remove (docs/Dev-012.md [Sec. 5.2])."""

from collections.abc import Sequence
from math import floor
from typing import ClassVar

from sqlalchemy import select

from etherbound.db.models import Actor
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import (
    Action,
    CloseAction,
    DropAction,
    InLoc,
    Location,
    ObjectTarget,
    OpenAction,
    PutAction,
    RemoveAction,
    TakeAction,
    Target,
    TileLoc,
    TileTarget,
    WearAction,
)
from etherbound.engine.objects import (
    bump_chunk,
    chunk_of_location,
    container_contents_bulk,
    free_hands,
    held_objects,
    is_open,
    location_of,
    object_at_cell,
    object_total_mass,
    set_held,
    set_in,
    set_tile,
    set_worn,
    worn_objects,
)
from etherbound.engine.ops.base import ActionContext, Resolution, object_reach, tile_reach
from etherbound.events.models import Event, ObjectChanged, ObjectMoved
from etherbound.world.objects import ObjectKind, two_handed

LIFT_LIMIT_KG = 40.0
MAX_CONTAINER_DEPTH = 32


def _row(ctx: ActionContext, target: ObjectTarget) -> ObjectRow | None:
    row: ObjectRow | None = ctx.session.get(ObjectRow, target.id)
    return row


def _kind(ctx: ActionContext, row: ObjectRow) -> ObjectKind:
    return ctx.grid.catalog[row.kind]


def _subject(ctx: ActionContext, row: ObjectRow) -> str:
    kind = _kind(ctx, row)
    return f"{kind.name} ×{row.quantity}" if row.quantity > 1 else kind.name


def _supported(ctx: ActionContext, row: ObjectRow) -> bool:
    """Another tile object rests on this one's top, or an actor stands on it."""
    kind = _kind(ctx, row)
    if not kind.solid:
        # A non-solid object has no top to rest on: you stand beside it, not on it.
        return False
    if row.x is None or row.y is None or row.h is None:
        return False
    top = row.h + kind.height
    if any(
        other.id != row.id and other.h == top for other in object_at_cell(ctx, row.x, row.y, top)
    ):
        return True
    return any(
        (floor(actor.x), floor(actor.y), actor.h) == (row.x, row.y, top)
        for actor in ctx.session.scalars(select(Actor))
    )


def _cells_free(ctx: ActionContext, x: int, y: int, h: int, height: int) -> bool:
    return not any(ctx.grid.solid_at(x, y, h + offset) for offset in range(1, max(height, 1) + 1))


def _actor_on(ctx: ActionContext, x: int, y: int, h: int) -> bool:
    return any(
        (floor(actor.x), floor(actor.y), actor.h) == (x, y, h)
        for actor in ctx.session.scalars(select(Actor))
    )


def _emit_bumps(
    ctx: ActionContext, from_loc: Location, to_loc: Location, events: list[Event]
) -> None:
    for loc in (from_loc, to_loc):
        cell = chunk_of_location(ctx, loc)
        if cell is not None:
            events.append(bump_chunk(ctx, cell[0], cell[1]))


def _merge_tile(ctx: ActionContext, row: ObjectRow, x: int, y: int, h: int) -> int | None:
    """Merge a stackable row into an identical stack on that tile; returns the survivor id."""
    kind = _kind(ctx, row)
    if not kind.stackable:
        return None
    for other in object_at_cell(ctx, x, y, h):
        if other.id == row.id:
            continue
        if (
            other.kind == row.kind
            and (other.state or {}) == (row.state or {})
            and other.owner == row.owner
            and other.integrity == row.integrity
        ):
            other.quantity += row.quantity
            ctx.session.delete(row)
            return other.id
    return None


class TakeHandler:
    op: ClassVar[str] = "take"

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if not isinstance(target, ObjectTarget):
            return False
        row = _row(ctx, target)
        return row is not None and row.loc not in ("held", "worn")

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        assert isinstance(target, ObjectTarget)
        return (TakeAction(target=target),)

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, TakeAction)
        row = _row(ctx, action.target)
        return _subject(ctx, row) if row is not None else None

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, TakeAction)
        row = _row(ctx, action.target)
        if row is None:
            return "nothing there"
        kind = _kind(ctx, row)
        if not object_reach(ctx, row):
            return "out of reach"
        if kind.fixed:
            return "fixed in place"
        if row.loc == "tile" and _supported(ctx, row):
            return "something is on it"
        if object_total_mass(ctx.session, ctx.grid.catalog, row) > LIFT_LIMIT_KG:
            return "too heavy"
        if self._hand(ctx, row) is None:
            return "hands full"
        if row.loc == "in":
            container = ctx.session.get(ObjectRow, row.container_id)
            if container is not None:
                ckind = _kind(ctx, container)
                if ckind.openable and not is_open(container):
                    return "closed"
        return None

    def _hand(self, ctx: ActionContext, row: ObjectRow) -> str | None:
        kind = _kind(ctx, row)
        if two_handed(kind):
            return "both" if not held_objects(ctx.session, ctx.actor.id) else None
        free = free_hands(ctx.session, ctx.actor.id)
        return free[0] if free else None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return 0

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        assert isinstance(action, TakeAction)
        row = _row(ctx, action.target)
        assert row is not None
        hand = self._hand(ctx, row)
        assert hand is not None
        kind = _kind(ctx, row)
        events: list[Event] = []
        from_loc = location_of(row)
        split_from = None
        if kind.stackable and row.quantity > 1:
            split = ObjectRow(
                kind=row.kind,
                quantity=1,
                state=dict(row.state or {}),
                integrity=row.integrity,
                owner=row.owner,
            )
            ctx.session.add(split)
            row.quantity -= 1
            set_held(split, ctx.actor.id, hand)
            ctx.session.flush()
            moved = split
            split_from = row.id
        else:
            set_held(row, ctx.actor.id, hand)
            moved = row
        ctx.session.flush()
        events.append(
            ObjectMoved(
                actor_id=ctx.actor.id,
                object_id=moved.id,
                kind=moved.kind,
                quantity=moved.quantity,
                op="take",
                from_=from_loc,
                to=location_of(moved),
                split_from=split_from,
            )
        )
        _emit_bumps(ctx, from_loc, location_of(moved), events)
        return Resolution(events)

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []


class DropHandler:
    op: ClassVar[str] = "drop"

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if not isinstance(target, ObjectTarget):
            return False
        row = _row(ctx, target)
        return row is not None and row.loc == "held"

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        assert isinstance(target, ObjectTarget)
        return (DropAction(target=target),)

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, DropAction)
        row = _row(ctx, action.target)
        return _subject(ctx, row) if row is not None else None

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, DropAction)
        row = _row(ctx, action.target)
        if row is None or row.loc != "held":
            return "not holding it"
        kind = _kind(ctx, row)
        x, y, h = ctx.actor_tile()[0], ctx.actor_tile()[1], ctx.actor.h
        if kind.solid:
            # The actor's own body fills the cells a solid object would need.
            return "no room"
        if not _cells_free(ctx, x, y, h, kind.height):
            return "no room"
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return 0

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        assert isinstance(action, DropAction)
        row = _row(ctx, action.target)
        assert row is not None
        x, y = ctx.actor_tile()
        from_loc = location_of(row)
        quantity = row.quantity
        events: list[Event] = []
        merged_into = _merge_tile(ctx, row, x, y, ctx.actor.h)
        if merged_into is None:
            set_tile(row, x, y, ctx.actor.h)
            to_loc: Location = location_of(row)
        else:
            to_loc = TileLoc(x=x, y=y, h=ctx.actor.h)
        ctx.session.flush()
        events.append(
            ObjectMoved(
                actor_id=ctx.actor.id,
                object_id=row.id,
                kind=row.kind,
                quantity=quantity,
                op="drop",
                from_=from_loc,
                to=to_loc,
                merged_into=merged_into,
            )
        )
        _emit_bumps(ctx, from_loc, to_loc, events)
        return Resolution(events)

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []


class PutHandler:
    op: ClassVar[str] = "put"

    def _destination(self, ctx: ActionContext, target: Target) -> tuple[str, int | None]:
        """``(label, top_h)`` for a destination; ``top_h`` is None for a container."""
        if isinstance(target, TileTarget):
            return "here", target.h
        if not isinstance(target, ObjectTarget):
            return "nowhere", None
        row = _row(ctx, target)
        if row is None:
            return "nowhere", None
        kind = _kind(ctx, row)
        if kind.container is not None:
            return f"into {kind.name}", None
        assert row.h is not None
        return f"onto {kind.name}", row.h + kind.height

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if isinstance(target, TileTarget):
            return True
        if not isinstance(target, ObjectTarget):
            return False
        row = _row(ctx, target)
        if row is None:
            return False
        kind = _kind(ctx, row)
        return kind.container is not None or kind.surface

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        if isinstance(target, TileTarget):
            into: ObjectTarget | TileTarget = target
        elif isinstance(target, ObjectTarget):
            into = target
        else:
            return ()
        actions: list[Action] = []
        for held in held_objects(ctx.session, ctx.actor.id):
            if isinstance(target, ObjectTarget) and target.id == held.id:
                continue
            actions.append(PutAction(target=ObjectTarget(id=held.id), into=into))
        return tuple(actions)

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, PutAction)
        row = _row(ctx, action.target)
        if row is None:
            return None
        label, _ = self._destination(ctx, action.into)
        return f"{_kind(ctx, row).name} {label}"

    def _cycle(self, ctx: ActionContext, dest_id: int, target_id: int) -> bool:
        current: int | None = dest_id
        for _ in range(MAX_CONTAINER_DEPTH):
            if current is None:
                return False
            if current == target_id:
                return True
            row = ctx.session.get(ObjectRow, current)
            if row is None or row.loc != "in":
                return False
            current = row.container_id
        return True

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, PutAction)
        row = _row(ctx, action.target)
        if row is None or row.loc != "held":
            return "not holding it"
        if isinstance(action.into, ObjectTarget):
            dest = _row(ctx, action.into)
            if dest is None:
                return "nothing there"
            if not object_reach(ctx, dest):
                return "out of reach"
            dest_kind = _kind(ctx, dest)
            if dest_kind.container is not None:
                if dest_kind.openable and not is_open(dest):
                    return "closed"
                if (
                    container_contents_bulk(ctx.session, ctx.grid.catalog, dest.id)
                    + _kind(ctx, row).bulk * row.quantity
                    > dest_kind.container.capacity
                ):
                    return "doesn't fit"
            if self._cycle(ctx, dest.id, row.id):
                return "not into itself"
            if dest_kind.container is None:
                top = dest.h + dest_kind.height if dest.h is not None else 0
                return self._placement(ctx, row, dest.x, dest.y, top)
            return None
        assert isinstance(action.into, TileTarget)
        target = action.into
        if not tile_reach(ctx, target.x, target.y, target.h):
            return "out of reach"
        return self._placement(ctx, row, target.x, target.y, target.h)

    def _placement(
        self, ctx: ActionContext, row: ObjectRow, x: int | None, y: int | None, h: int
    ) -> str | None:
        assert x is not None and y is not None
        kind = _kind(ctx, row)
        if not _cells_free(ctx, x, y, h, kind.height):
            return "no room"
        if _actor_on(ctx, x, y, h):
            return "someone is there"
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return 0

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        assert isinstance(action, PutAction)
        row = _row(ctx, action.target)
        assert row is not None
        from_loc = location_of(row)
        quantity = row.quantity
        events: list[Event] = []
        merged_into = None
        if isinstance(action.into, ObjectTarget):
            dest = _row(ctx, action.into)
            assert dest is not None
            dest_kind = _kind(ctx, dest)
            if dest_kind.container is not None:
                set_in(row, dest.id)
                to_loc: Location = InLoc(object_id=dest.id)
            else:
                assert dest.x is not None and dest.y is not None and dest.h is not None
                top = dest.h + dest_kind.height
                merged_into = _merge_tile(ctx, row, dest.x, dest.y, top)
                if merged_into is None:
                    set_tile(row, dest.x, dest.y, top)
                to_loc = TileLoc(x=dest.x, y=dest.y, h=top)
        else:
            target = action.into
            merged_into = _merge_tile(ctx, row, target.x, target.y, target.h)
            if merged_into is None:
                set_tile(row, target.x, target.y, target.h)
            to_loc = TileLoc(x=target.x, y=target.y, h=target.h)
        ctx.session.flush()
        events.append(
            ObjectMoved(
                actor_id=ctx.actor.id,
                object_id=row.id,
                kind=row.kind,
                quantity=quantity,
                op="put",
                from_=from_loc,
                to=to_loc,
                merged_into=merged_into,
            )
        )
        _emit_bumps(ctx, from_loc, to_loc, events)
        return Resolution(events)

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []


class OpenHandler:
    op: ClassVar[str] = "open"

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if not isinstance(target, ObjectTarget):
            return False
        row = _row(ctx, target)
        return row is not None and _kind(ctx, row).openable and object_reach(ctx, row)

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        assert isinstance(target, ObjectTarget)
        return (OpenAction(target=target),)

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, OpenAction)
        row = _row(ctx, action.target)
        return _kind(ctx, row).name if row is not None else None

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, OpenAction)
        row = _row(ctx, action.target)
        if row is None:
            return "nothing there"
        if not object_reach(ctx, row):
            return "out of reach"
        if row.loc == "tile" and _supported(ctx, row):
            return "something is on it"
        if is_open(row):
            return "already open"
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return 0

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        assert isinstance(action, OpenAction)
        return _set_open(ctx, action, True)

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []


class CloseHandler:
    op: ClassVar[str] = "close"

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if not isinstance(target, ObjectTarget):
            return False
        row = _row(ctx, target)
        return row is not None and _kind(ctx, row).openable and object_reach(ctx, row)

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        assert isinstance(target, ObjectTarget)
        return (CloseAction(target=target),)

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, CloseAction)
        row = _row(ctx, action.target)
        return _kind(ctx, row).name if row is not None else None

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, CloseAction)
        row = _row(ctx, action.target)
        if row is None:
            return "nothing there"
        if not object_reach(ctx, row):
            return "out of reach"
        if not is_open(row):
            return "already closed"
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return 0

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        assert isinstance(action, CloseAction)
        return _set_open(ctx, action, False)

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []


def _set_open(ctx: ActionContext, action: OpenAction | CloseAction, opened: bool) -> Resolution:
    row = _row(ctx, action.target)
    assert row is not None
    row.state = {**(row.state or {}), "open": opened}
    ctx.session.flush()
    events: list[Event] = [
        ObjectChanged(
            actor_id=ctx.actor.id,
            object_id=row.id,
            kind=row.kind,
            op="open" if opened else "close",
            changes={"open": opened},
        )
    ]
    if row.loc == "tile" and row.cx is not None and row.cy is not None:
        events.append(bump_chunk(ctx, row.cx, row.cy))
    return Resolution(events)


class WearHandler:
    op: ClassVar[str] = "wear"

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if not isinstance(target, ObjectTarget):
            return False
        row = _row(ctx, target)
        return row is not None and row.loc == "held" and _kind(ctx, row).wearable is not None

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        assert isinstance(target, ObjectTarget)
        return (WearAction(target=target),)

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, WearAction)
        row = _row(ctx, action.target)
        return _kind(ctx, row).name if row is not None else None

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, WearAction)
        row = _row(ctx, action.target)
        if row is None or row.loc != "held":
            return "not holding it"
        kind = _kind(ctx, row)
        wearable = kind.wearable
        assert wearable is not None
        for worn in worn_objects(ctx.session, ctx.actor.id):
            worn_kind = _kind(ctx, worn)
            if worn_kind.wearable is not None and worn_kind.wearable.slot == wearable.slot:
                return "slot taken"
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return 0

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        assert isinstance(action, WearAction)
        row = _row(ctx, action.target)
        assert row is not None
        kind = _kind(ctx, row)
        assert kind.wearable is not None
        from_loc = location_of(row)
        set_worn(row, ctx.actor.id, kind.wearable.slot)
        ctx.session.flush()
        events: list[Event] = [
            ObjectMoved(
                actor_id=ctx.actor.id,
                object_id=row.id,
                kind=row.kind,
                quantity=row.quantity,
                op="wear",
                from_=from_loc,
                to=location_of(row),
            )
        ]
        _emit_bumps(ctx, from_loc, location_of(row), events)
        return Resolution(events)

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []


class RemoveHandler:
    op: ClassVar[str] = "remove"

    def applies(self, ctx: ActionContext, target: Target) -> bool:
        if not isinstance(target, ObjectTarget):
            return False
        row = _row(ctx, target)
        return row is not None and row.loc == "worn"

    def builds(self, ctx: ActionContext, target: Target) -> Sequence[Action]:
        assert isinstance(target, ObjectTarget)
        return (RemoveAction(target=target),)

    def subject(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, RemoveAction)
        row = _row(ctx, action.target)
        return _kind(ctx, row).name if row is not None else None

    def validate(self, ctx: ActionContext, action: Action) -> str | None:
        assert isinstance(action, RemoveAction)
        row = _row(ctx, action.target)
        if row is None or row.loc != "worn":
            return "not wearing it"
        if not free_hands(ctx.session, ctx.actor.id):
            return "hands full"
        return None

    def duration(self, ctx: ActionContext, action: Action) -> int:
        return 0

    def resolve(self, ctx: ActionContext, action: Action) -> Resolution:
        assert isinstance(action, RemoveAction)
        row = _row(ctx, action.target)
        assert row is not None
        hand = free_hands(ctx.session, ctx.actor.id)[0]
        from_loc = location_of(row)
        set_held(row, ctx.actor.id, hand)
        ctx.session.flush()
        events: list[Event] = [
            ObjectMoved(
                actor_id=ctx.actor.id,
                object_id=row.id,
                kind=row.kind,
                quantity=row.quantity,
                op="remove",
                from_=from_loc,
                to=location_of(row),
            )
        ]
        _emit_bumps(ctx, from_loc, location_of(row), events)
        return Resolution(events)

    def complete(self, ctx: ActionContext, action: Action) -> list[Event]:
        return []
