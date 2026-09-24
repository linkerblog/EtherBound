"""The Extras routine brain: it proposes, the engine writes, and a restart replays it."""

import shutil
from collections.abc import Iterator
from math import hypot
from pathlib import Path

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, Base
from etherbound.db.models import Event as EventRow
from etherbound.engine.actions import Goal
from etherbound.engine.world import PLAYER_ID, ActorState, WorldEngine
from etherbound.minds.extras import ExtrasBrain

SEED = 0


def make_sessions(path: Path) -> sessionmaker[Session]:
    database = create_engine(
        f"sqlite:///{path.as_posix()}", connect_args={"check_same_thread": False}
    )
    Base.metadata.create_all(database)
    return sessionmaker(bind=database, expire_on_commit=False, class_=Session)


@pytest.fixture
def engine(tmp_path: Path) -> Iterator[WorldEngine]:
    world = WorldEngine(make_sessions(tmp_path / "brain.db"))
    world.ensure_world(SEED)
    yield world


def attach(world: WorldEngine, time_scale: float = 1.0) -> ExtrasBrain:
    brain = ExtrasBrain(world, time_scale=time_scale)
    brain.subscribe(world.bus)
    return brain


def all_events(world: WorldEngine, event_type: str | None = None) -> list[EventRow]:
    rows: list[EventRow] = []
    after = 0
    while True:
        page = world.read_events(after, 500, event_type, None)
        if not page:
            return rows
        rows.extend(page)
        after = page[-1].seq


def extra_state(world: WorldEngine, actor_id: str) -> ActorState:
    return next(actor for actor in world.get_state().actors if actor.id == actor_id)


def teleport(world: WorldEngine, actor_id: str, x: float, y: float, h: int) -> None:
    with world.sessions() as session:
        actor = session.get(Actor, actor_id)
        assert actor is not None
        actor.x, actor.y, actor.h, actor.z = x, y, h, h // 6
        actor.activity = None
        session.commit()


def valid_tile(world: WorldEngine, x: int, y: int) -> int | None:
    surfaces = world.grid.standing_surfaces(x, y)
    return surfaces[0].h if surfaces else None


async def test_set_goal_refuses_invalid_and_logs_valid(engine: WorldEngine) -> None:
    anchor = extra_state(engine, "extra-001").mind.anchor

    with pytest.raises(ValueError):
        await engine.set_goal(PLAYER_ID, Goal(x=anchor.x, y=anchor.y, h=anchor.h), "chosen")
    with pytest.raises(KeyError):
        await engine.set_goal("nobody", None, "arrived")
    with pytest.raises(ValueError):
        await engine.set_goal("extra-001", Goal(x=10**6, y=10**6, h=0), "chosen")

    await engine.set_goal("extra-001", Goal(x=anchor.x, y=anchor.y, h=anchor.h), "chosen")
    events = all_events(engine, "actor.goal_set")
    assert events[-1].actor_id == "extra-001"
    assert events[-1].data["reason"] == "chosen"
    assert events[-1].data["goal"]["x"] == anchor.x

    await engine.set_goal("extra-001", None, "arrived")
    assert all_events(engine, "actor.goal_set")[-1].data == {"goal": None, "reason": "arrived"}


async def test_extras_walk_and_wait_near_their_anchor(engine: WorldEngine) -> None:
    attach(engine)
    for _ in range(120):
        await engine.advance_time()

    extras = [actor for actor in engine.get_state().actors if actor.kind == "extra"]
    walked = {row.actor_id for row in all_events(engine, "actor.moved")}
    waited = {
        row.actor_id for row in all_events(engine, "activity.started") if row.data["op"] == "wait"
    }
    chosen = [row for row in all_events(engine, "actor.goal_set") if row.data["goal"] is not None]
    goals = {row.actor_id: row.data["goal"] for row in chosen}

    for extra in extras:
        assert extra.id in walked, f"{extra.id} never walked"
        assert extra.id in waited, f"{extra.id} never waited"
        anchor = extra.mind.anchor
        goal = goals.get(extra.id)
        assert goal is not None
        assert hypot(goal["x"] - anchor.x, goal["y"] - anchor.y) <= 12
        assert hypot(extra.x - (anchor.x + 0.5), extra.y - (anchor.y + 0.5)) <= 16

    for row in all_events(engine, "actor.moved"):
        source, target = row.data["from_tile"], row.data["to_tile"]
        assert abs(target["x"] - source["x"]) <= 1 and abs(target["y"] - source["y"]) <= 1
        assert any(
            surface.h == target["h"]
            for surface in engine.grid.standing_surfaces(target["x"], target["y"])
        )


async def test_two_engines_match_and_a_restart_continues(tmp_path: Path) -> None:
    a = WorldEngine(make_sessions(tmp_path / "a.db"))
    a.ensure_world(SEED)
    attach(a)
    b = WorldEngine(make_sessions(tmp_path / "b.db"))
    b.ensure_world(SEED)
    attach(b)
    for _ in range(60):
        await a.advance_time()
        await b.advance_time()
    assert [(row.type, row.data) for row in all_events(a)] == [
        (row.type, row.data) for row in all_events(b)
    ]

    marker = all_events(a)[-1].seq
    shutil.copyfile(tmp_path / "a.db", tmp_path / "c.db")
    c = WorldEngine(make_sessions(tmp_path / "c.db"))
    c.ensure_world(SEED)
    attach(c)
    for _ in range(60):
        await a.advance_time()
        await c.advance_time()

    def tail(world: WorldEngine) -> list[tuple[str, dict[str, object]]]:
        return [(row.type, row.data) for row in all_events(world) if row.seq > marker]

    assert tail(a)
    assert tail(a) == tail(c)


async def test_shoved_extra_recovers_and_a_blocked_goal_sticks(engine: WorldEngine) -> None:
    attach(engine)
    anchor = extra_state(engine, "extra-001").mind.anchor
    goal = Goal(x=anchor.x, y=anchor.y, h=anchor.h)

    # Start ten metres out and give it one tick to walk before the shove, so the goal is still
    # set when the position changes and the cached plan must be thrown away.
    near = (anchor.x + 10, anchor.y)
    assert valid_tile(engine, *near) is not None
    teleport(engine, "extra-001", near[0] + 0.5, near[1] + 0.5, valid_tile(engine, *near) or 0)
    await engine.set_goal("extra-001", goal, "chosen")
    await engine.advance_time()
    assert extra_state(engine, "extra-001").mind.goal is not None

    far = (anchor.x - 8, anchor.y)
    assert valid_tile(engine, *far) is not None
    teleport(engine, "extra-001", far[0] + 0.5, far[1] + 0.5, valid_tile(engine, *far) or 0)
    for _ in range(300):
        await engine.advance_time()
        if extra_state(engine, "extra-001").mind.goal is None:
            break
    arrivals = [
        row
        for row in all_events(engine, "actor.goal_set")
        if row.actor_id == "extra-001" and row.data["reason"] == "arrived"
    ]
    assert arrivals
    arrived = extra_state(engine, "extra-001")
    assert (int(arrived.x), int(arrived.y), arrived.h) == (anchor.x, anchor.y, anchor.h)

    # Blocked goal: teleport into a region with no standing surface, so A* cannot leave it.
    other = "extra-002"
    await engine.set_goal(other, goal, "chosen")
    teleport(engine, other, 1000.5, 1000.5, 0)
    for _ in range(3):
        await engine.advance_time()
    stuck = [
        row
        for row in all_events(engine, "actor.goal_set")
        if row.actor_id == other and row.data["reason"] == "stuck"
    ]
    assert stuck


async def test_paused_clock_moves_no_extra(engine: WorldEngine) -> None:
    attach(engine)
    await engine.set_clock(paused=True)
    before = {(actor.id, actor.x, actor.y) for actor in engine.get_state().actors}
    for _ in range(10):
        await engine.advance_time()
    after = {(actor.id, actor.x, actor.y) for actor in engine.get_state().actors}
    assert after == before
    assert not [row for row in all_events(engine, "actor.moved") if row.actor_id != PLAYER_ID]
