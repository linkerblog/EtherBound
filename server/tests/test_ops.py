from dataclasses import replace
from pathlib import Path

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, Base
from etherbound.db.models import Event as EventRow
from etherbound.engine.actions import (
    ClimbAction,
    DigAction,
    InspectAction,
    MoveAction,
    TileTarget,
    WaitAction,
)
from etherbound.engine.ops import catalog, register
from etherbound.engine.ops.catalog import load_catalog
from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.events import ChunkChanged
from etherbound.world.chunk import CELL_COUNT, CHUNK_SIZE, LEVEL_VOID, ChunkLevel

# Seed 0: the road (x 120-123) and the grass east of it (x 124) are both at h = 2.
ROAD = (123, 128)
GRASS = (124, 128)


@pytest.fixture
def sessions() -> sessionmaker[Session]:
    database = create_engine("sqlite:///:memory:", connect_args={"check_same_thread": False})
    Base.metadata.create_all(database)
    return sessionmaker(bind=database, expire_on_commit=False, class_=Session)


@pytest.fixture
def engine(sessions: sessionmaker[Session]) -> WorldEngine:
    world = WorldEngine(sessions)
    world.ensure_world(0)
    return world


def place(engine: WorldEngine, x: int, y: int, h: int | None = None) -> None:
    """Put Niko at a tile centre for a test; h defaults to the tile's ground."""
    ground = engine.grid.ground_at(x, y)
    assert ground is not None
    with engine.sessions() as session:
        actor = session.get(Actor, PLAYER_ID)
        assert actor is not None
        actor.x, actor.y = x + 0.5, y + 0.5
        actor.h = ground[0] if h is None else h
        actor.z = actor.h // 6
        session.commit()


def set_ground(engine: WorldEngine, x: int, y: int, h: int, material: str = "grass") -> None:
    """Reshape one tile in memory only, as a synthetic world for a test."""
    cx, cy = x // CHUNK_SIZE, y // CHUNK_SIZE
    chunk = engine.grid.chunk(cx, cy)
    assert chunk is not None
    index = chunk.index(x - cx * CHUNK_SIZE, y - cy * CHUNK_SIZE)
    ground_h, surface_mat = list(chunk.ground_h), list(chunk.surface_mat)
    ground_h[index], surface_mat[index] = h, engine.registry[material].id
    engine.grid.add_chunk(replace(chunk, ground_h=tuple(ground_h), surface_mat=tuple(surface_mat)))


def actor(engine: WorldEngine) -> Actor:
    with engine.sessions() as session:
        found = session.get(Actor, PLAYER_ID)
        assert found is not None
        return found


def stored(engine: WorldEngine, event_type: str) -> list[EventRow]:
    return engine.read_events(event_type=event_type, limit=500)


def menu_ops(engine: WorldEngine, x: int, y: int, z: int = 0) -> dict[str, tuple[bool, str | None]]:
    return {
        entry.op: (entry.available, entry.reason)
        for entry in engine.menu(PLAYER_ID, x + 0.5, y + 0.5, z).entries
    }


async def ticks(engine: WorldEngine, count: int) -> None:
    for _ in range(count):
        await engine.advance_time()


def test_catalog_loads_58_unique_ops_and_rejects_bad_data(tmp_path: Path) -> None:
    keys = [spec.key for spec in catalog()]
    assert len(keys) == 58
    assert len(set(keys)) == 58
    duplicate = tmp_path / "duplicate.toml"
    duplicate.write_text(
        '[[ops]]\nkey = "dig"\ngroup = "matter"\ntargets = ["tile"]\ntags = ["labor"]\n' * 2,
        encoding="utf-8",
    )
    with pytest.raises(ValueError, match="duplicate op key"):
        load_catalog(duplicate)
    unknown = tmp_path / "unknown.toml"
    unknown.write_text(
        '[[ops]]\nkey = "dig"\ngroup = "matter"\ntargets = ["planet"]\ntags = ["labor"]\n',
        encoding="utf-8",
    )
    with pytest.raises(ValueError, match="unknown target kinds"):
        load_catalog(unknown)

    class Stray:
        op = "levitate"

    with pytest.raises(ValueError, match="missing from ops.toml"):
        register(Stray())  # type: ignore[arg-type]


async def test_menu_next_to_grass_offers_actions_submit_accepts(engine: WorldEngine) -> None:
    place(engine, *ROAD)
    entries = {entry.op: entry for entry in engine.menu(PLAYER_ID, 124.5, 128.5, 0).entries}
    assert list(entries) == ["inspect", "dig"]
    assert all(entry.available for entry in entries.values())
    for entry in entries.values():
        result = await engine.submit(PLAYER_ID, entry.action)
        assert result.accepted, entry.op


def test_menu_on_own_tile_adds_wait(engine: WorldEngine) -> None:
    place(engine, *GRASS)
    assert list(menu_ops(engine, *GRASS)) == ["wait", "inspect", "dig"]


def test_menu_hides_or_explains_dig(engine: WorldEngine) -> None:
    place(engine, *ROAD)
    assert "dig" not in menu_ops(engine, 122, 128)
    set_ground(engine, *GRASS, h=2, material="concrete")
    assert menu_ops(engine, *GRASS)["dig"] == (False, "too hard to dig by hand")
    assert menu_ops(engine, 116, 128)["dig"] == (False, "out of reach")


async def test_dig_lifecycle_is_logged_and_survives_restart(
    engine: WorldEngine, sessions: sessionmaker[Session]
) -> None:
    changed: list[ChunkChanged] = []
    engine.bus.subscribe(ChunkChanged, changed.append, name="test.chunk_changed")
    place(engine, *ROAD)
    chunk = engine.grid.chunk(3, 4)
    assert chunk is not None
    revision = chunk.revision

    result = await engine.submit(PLAYER_ID, DigAction(target=TileTarget(x=124, y=128, h=2)))
    assert result.accepted
    assert result.activity is not None
    assert result.activity.ends_minute - result.activity.started_minute == 96
    assert actor(engine).activity is not None
    assert [row.data["op"] for row in stored(engine, "activity.started")] == ["dig"]

    await ticks(engine, 95)
    assert stored(engine, "terrain.dug") == []
    await ticks(engine, 1)
    dug = stored(engine, "terrain.dug")
    assert [(row.data["removed"], row.data["exposed"], row.data["dug"]) for row in dug] == [
        ("topsoil", "topsoil", 1)
    ]
    assert [row.data["outcome"] for row in stored(engine, "activity.finished")] == ["completed"]
    assert stored(engine, "chunk.changed") == []
    assert [(event.cx, event.cy, event.revision) for event in changed] == [(3, 4, revision + 1)]
    assert actor(engine).activity is None
    assert engine.grid.ground_at(*GRASS) == (1, engine.registry["topsoil"].id, 1)

    restarted = WorldEngine(sessions)
    restarted.ensure_world(0)
    assert restarted.grid.ground_at(*GRASS) == (1, engine.registry["topsoil"].id, 1)
    restarted_chunk = restarted.grid.chunk(3, 4)
    assert restarted_chunk is not None
    assert restarted_chunk.revision == revision + 1


async def test_digging_own_tile_lowers_niko_who_can_still_walk(engine: WorldEngine) -> None:
    place(engine, *GRASS)
    assert (
        await engine.submit(PLAYER_ID, DigAction(target=TileTarget(x=124, y=128, h=2)))
    ).accepted
    await ticks(engine, 96)
    niko = actor(engine)
    assert (niko.h, niko.z) == (1, 0)
    moved = stored(engine, "actor.moved")
    assert [(row.data["mode"], row.data["to_tile"]["h"]) for row in moved] == [("lowered", 1)]
    walked = await engine.submit(PLAYER_ID, MoveAction(dx=0, dy=1), delta_seconds=0.05)
    assert walked.accepted
    assert walked.y > niko.y


async def test_strata_stay_anchored_to_the_original_ground(engine: WorldEngine) -> None:
    place(engine, *GRASS)
    for _ in range(7):
        await engine.submit(
            PLAYER_ID, DigAction(target=TileTarget(x=124, y=128, h=actor(engine).h))
        )
        await ticks(engine, 96)
    exposed = [row.data["exposed"] for row in stored(engine, "terrain.dug")]
    # Test-world strata: topsoil from 0 m, dirt from 4 m (depth 8), measured from the original.
    assert exposed == ["topsoil"] * 6 + ["dirt"]
    assert engine.grid.ground_at(*GRASS) == (-5, engine.registry["dirt"].id, 7)
    assert actor(engine).h == -5


async def test_dig_blocked_by_floor_or_hollow_ground(engine: WorldEngine) -> None:
    place(engine, 140, 140, h=12)
    assert menu_ops(engine, 140, 140, 2)["dig"] == (False, "floor in the way")

    place(engine, *ROAD)
    cx, cy = GRASS[0] // CHUNK_SIZE, GRASS[1] // CHUNK_SIZE
    index = (GRASS[1] - cy * CHUNK_SIZE) * CHUNK_SIZE + GRASS[0] - cx * CHUNK_SIZE
    level = ChunkLevel.empty(cx, cy, 0)
    flags = [0] * CELL_COUNT
    flags[index] = LEVEL_VOID
    engine.grid.add_level(replace(level, flags=tuple(flags)))
    result = await engine.submit(PLAYER_ID, DigAction(target=TileTarget(x=124, y=128, h=2)))
    assert (result.accepted, result.reason) == (False, "hollow below")


async def test_moving_interrupts_but_zero_moves_and_rejections_do_not(
    engine: WorldEngine,
) -> None:
    place(engine, *ROAD)
    dig = DigAction(target=TileTarget(x=124, y=128, h=2))
    assert (await engine.submit(PLAYER_ID, dig)).accepted
    assert (await engine.submit(PLAYER_ID, MoveAction(dx=0, dy=0))).accepted
    assert actor(engine).activity is not None
    await engine.set_clock(paused=True)
    paused = await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0))
    assert (paused.accepted, paused.reason) == (False, "paused")
    assert actor(engine).activity is not None
    await engine.set_clock(paused=False)

    assert (await engine.submit(PLAYER_ID, MoveAction(dx=0, dy=1))).accepted
    assert actor(engine).activity is None
    finished = stored(engine, "activity.finished")
    assert [(row.data["outcome"], row.data["reason"]) for row in finished] == [
        ("interrupted", "move")
    ]
    await ticks(engine, 30)
    assert stored(engine, "terrain.dug") == []
    assert engine.grid.ground_at(*GRASS) == (2, engine.registry["grass"].id, 0)


async def test_activity_saved_midway_completes_after_restart(
    engine: WorldEngine, sessions: sessionmaker[Session]
) -> None:
    place(engine, *ROAD)
    assert (await engine.submit(PLAYER_ID, WaitAction())).accepted
    await ticks(engine, 5)
    restarted = WorldEngine(sessions)
    restarted.ensure_world(0)
    state = next(a for a in restarted.get_state().actors if a.id == PLAYER_ID)
    assert state.activity is not None and state.activity.op == "wait"
    await ticks(restarted, 10)
    assert [row.data["outcome"] for row in stored(restarted, "activity.finished")] == ["completed"]


async def test_completion_revalidates_and_can_fail(engine: WorldEngine) -> None:
    place(engine, *GRASS)
    set_ground(engine, 125, 128, h=4)
    assert (
        await engine.submit(PLAYER_ID, ClimbAction(target=TileTarget(x=125, y=128, h=4)))
    ).accepted
    set_ground(engine, 125, 128, h=14)
    await ticks(engine, 1)
    finished = stored(engine, "activity.finished")
    assert [(row.data["outcome"], row.data["reason"]) for row in finished] == [
        ("failed", "nothing to stand on")
    ]
    assert (actor(engine).x, actor(engine).h) == (124.5, 2)


async def test_climb_up_a_ledge_and_down_a_drop(engine: WorldEngine) -> None:
    place(engine, *GRASS)
    set_ground(engine, 125, 128, h=4)
    set_ground(engine, 126, 128, h=1)
    set_ground(engine, 124, 129, h=6)
    set_ground(engine, 123, 127, h=3)
    assert menu_ops(engine, 125, 128)["climb"] == (True, None)
    assert menu_ops(engine, 124, 129)["climb"] == (False, "too high to climb")
    assert "climb" not in menu_ops(engine, 123, 127)

    assert (
        await engine.submit(PLAYER_ID, ClimbAction(target=TileTarget(x=125, y=128, h=4)))
    ).accepted
    await ticks(engine, 1)
    assert (actor(engine).x, actor(engine).h) == (125.5, 4)
    assert (
        await engine.submit(PLAYER_ID, ClimbAction(target=TileTarget(x=126, y=128, h=1)))
    ).accepted
    await ticks(engine, 1)
    assert (actor(engine).x, actor(engine).h) == (126.5, 1)
    modes = [row.data["mode"] for row in stored(engine, "actor.moved")]
    assert modes == ["climb", "climb"]


async def test_inspect_returns_text_and_emits_nothing(engine: WorldEngine) -> None:
    place(engine, *ROAD)
    before = len(engine.read_events(limit=500))
    result = await engine.submit(PLAYER_ID, InspectAction(target=TileTarget(x=124, y=128, h=2)))
    assert (result.accepted, result.text) == (True, "Grass · 1 m · diggable, flammable")
    far_ground = engine.grid.ground_at(170, 128)
    assert far_ground is not None
    beyond = await engine.submit(
        PLAYER_ID, InspectAction(target=TileTarget(x=170, y=128, h=far_ground[0]))
    )
    assert (beyond.accepted, beyond.reason) == (False, "too far to see")
    assert len(engine.read_events(limit=500)) == before
