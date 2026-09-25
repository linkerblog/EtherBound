from dataclasses import replace

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, Base, WallIntegrity
from etherbound.db.models import ChunkLevel as ChunkLevelRow
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import (
    ActorTarget,
    BreakAction,
    EdgeTarget,
    HitAction,
    ObjectTarget,
    PushAction,
    SelfTarget,
    ThrowAction,
)
from etherbound.engine.physics import (
    absorb_energy,
    integrity_capacity,
    kinetic_energy,
    potential_energy,
    strike_energy,
)
from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.world.chunk import CHUNK_SIZE, ChunkLevel
from etherbound.world.materials import MaterialRegistry

PLAYER = (123, 128)


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


def place(engine: WorldEngine, x: int, y: int) -> None:
    ground = engine.grid.ground_at(x, y)
    assert ground is not None
    with engine.sessions() as session:
        actor = session.get(Actor, PLAYER_ID)
        assert actor is not None
        actor.x, actor.y = x + 0.5, y + 0.5
        actor.h = ground[0]
        actor.z = actor.h // 6
        session.commit()


def spawn_object(engine: WorldEngine, kind: str, x: int, y: int) -> int:
    ground = engine.grid.ground_at(x, y)
    assert ground is not None
    with engine.sessions() as session:
        row = ObjectRow(
            kind=kind,
            loc="tile",
            x=x,
            y=y,
            h=ground[0],
            cx=x // CHUNK_SIZE,
            cy=y // CHUNK_SIZE,
            quantity=1,
            state={},
        )
        session.add(row)
        session.commit()
        return row.id


def spawn_tool(engine: WorldEngine, kind: str = "shovel") -> int:
    with engine.sessions() as session:
        row = ObjectRow(
            kind=kind, loc="held", actor_id=PLAYER_ID, slot="left", quantity=1, state={}
        )
        session.add(row)
        session.commit()
        return row.id


def set_wall(
    engine: WorldEngine, x: int, y: int, edge: str, material: str
) -> tuple[int, int, int, int]:
    cx, cy = x // CHUNK_SIZE, y // CHUNK_SIZE
    level = engine.grid.level(cx, cy, 0)
    if level is None:
        level = ChunkLevel.empty(cx, cy, 0)
    index = level.index(x - cx * CHUNK_SIZE, y - cy * CHUNK_SIZE)
    values = list(level.wall_n if edge == "north" else level.wall_w)
    values[index] = engine.registry[material].id
    updated = replace(
        level,
        wall_n=tuple(values) if edge == "north" else level.wall_n,
        wall_w=tuple(values) if edge == "west" else level.wall_w,
    )
    engine.grid.add_level(updated)
    with engine.sessions() as session:
        row = session.get(ChunkLevelRow, (cx, cy, 0))
        if row is None:
            row = ChunkLevelRow(
                cx=cx,
                cy=cy,
                z=0,
                floor_h=updated.floor_blob,
                floor_mat=updated.floor_mat_blob,
                wall_n=updated.wall_n_blob,
                wall_w=updated.wall_w_blob,
                edge_flags=updated.edge_flags_blob,
                flags=updated.flags_blob,
            )
            session.add(row)
        else:
            row.floor_h = updated.floor_blob
            row.floor_mat = updated.floor_mat_blob
        row.wall_n = updated.wall_n_blob
        row.wall_w = updated.wall_w_blob
        row.edge_flags = updated.edge_flags_blob
        row.flags = updated.flags_blob
        session.commit()
    return cx, cy, 0, index


def test_energy_and_integrity_use_si_units() -> None:
    assert kinetic_energy(2, 5) == 25
    assert potential_energy(80, 3.5) == pytest.approx(2746.8)
    assert integrity_capacity(250, 6) == 1500
    assert strike_energy(None) == 25
    assert strike_energy(2) == 225
    absorbed, remaining = absorb_energy(225, 150)
    assert (absorbed, remaining, absorbed + remaining) == (150, 75, 225)


def test_resistance_calibration_preserves_reference_impacts() -> None:
    materials = MaterialRegistry.load()
    brick = materials["brick"].resistance
    wall = integrity_capacity(brick, 6)
    sledge_blow = strike_energy(5)
    car_at_50_kmh = kinetic_energy(1500, 50 / 3.6)

    assert strike_energy(None) < brick
    assert sledge_blow * 2 < wall < sledge_blow * 3
    assert car_at_50_kmh > wall


@pytest.mark.asyncio
async def test_push_moves_object_and_returns_authoritative_path(engine: WorldEngine) -> None:
    place(engine, *PLAYER)
    apple_id = spawn_object(engine, "apple", 124, 128)

    result = await engine.submit(
        PLAYER_ID,
        PushAction(target=ObjectTarget(id=apple_id), dx=1, dy=0),
    )

    assert result.accepted
    assert result.trajectory[0].x == 124
    assert result.trajectory[-1].x > 124
    with engine.sessions() as session:
        apple = session.get(ObjectRow, apple_id)
        assert apple is not None
        assert (apple.x, apple.y) == (result.trajectory[-1].x, 128)
    resolved = engine.read_events(event_type="physics.resolved", limit=10)
    assert resolved
    assert resolved[-1].data["trajectory"] == [
        point.model_dump(mode="json") for point in result.trajectory
    ]


def test_physics_actions_are_generated_from_targets(engine: WorldEngine) -> None:
    place(engine, *PLAYER)
    apple_id = spawn_object(engine, "apple", 124, 128)
    entries = engine.menu(PLAYER_ID, 124.5, 128.5, 0).entries
    assert {entry.op for entry in entries} >= {"push", "pull", "drag", "hit", "break"}
    assert any(entry.op == "push" and entry.action.target.id == apple_id for entry in entries)

    tool_id = spawn_tool(engine)
    own_tile_entries = engine.menu(PLAYER_ID, 123.5, 128.5, 0).entries
    throws = [entry.action for entry in own_tile_entries if isinstance(entry.action, ThrowAction)]
    assert len(throws) == 4
    assert all(entry.target.id == tool_id for entry in throws)
    assert {(entry.dx, entry.dy) for entry in throws} == {
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    }
    set_wall(engine, 124, 128, "west", "brick")
    wall_entries = engine.menu(PLAYER_ID, 124.5, 128.5, 0).entries
    assert any(
        entry.op == "break" and isinstance(entry.action.target, EdgeTarget)
        for entry in wall_entries
    )


def _object_ids(entries: object) -> set[int]:
    ids: set[int] = set()
    for entry in entries:  # type: ignore[attr-defined]
        target = getattr(entry.action, "target", None)
        if isinstance(target, ObjectTarget):
            ids.add(target.id)
    return ids


def test_menu_radius_reaches_open_neighbours_and_stops_at_walls(engine: WorldEngine) -> None:
    place(engine, *PLAYER)
    east_id = spawn_object(engine, "apple", PLAYER[0] + 1, PLAYER[1])
    north_id = spawn_object(engine, "apple", PLAYER[0], PLAYER[1] - 1)

    own_tile_only = _object_ids(engine.menu(PLAYER_ID, 123.5, 128.5, 0).entries)
    assert east_id not in own_tile_only
    assert north_id not in own_tile_only

    reachable = _object_ids(engine.menu(PLAYER_ID, 123.5, 128.5, 0, radius=1).entries)
    assert east_id in reachable
    assert north_id in reachable

    set_wall(engine, *PLAYER, "north", "brick")
    blocked_by_wall = _object_ids(engine.menu(PLAYER_ID, 123.5, 128.5, 0, radius=1).entries)
    assert east_id in blocked_by_wall
    assert north_id not in blocked_by_wall


def test_menu_radius_reaches_diagonal_neighbours_around_open_corners(engine: WorldEngine) -> None:
    place(engine, *PLAYER)
    diagonal_id = spawn_object(engine, "apple", PLAYER[0] + 1, PLAYER[1] - 1)

    open_corner = _object_ids(engine.menu(PLAYER_ID, 123.5, 128.5, 0, radius=1).entries)
    assert diagonal_id in open_corner

    set_wall(engine, *PLAYER, "north", "brick")
    still_open_via_east = _object_ids(engine.menu(PLAYER_ID, 123.5, 128.5, 0, radius=1).entries)
    assert diagonal_id in still_open_via_east

    set_wall(engine, PLAYER[0] + 1, PLAYER[1], "west", "brick")
    fully_enclosed = _object_ids(engine.menu(PLAYER_ID, 123.5, 128.5, 0, radius=1).entries)
    assert diagonal_id not in fully_enclosed


def test_menu_entries_carry_their_tile_offset(engine: WorldEngine) -> None:
    place(engine, *PLAYER)
    east_id = spawn_object(engine, "apple", PLAYER[0] + 1, PLAYER[1])
    tool_id = spawn_tool(engine)
    set_wall(engine, *PLAYER, "north", "brick")

    menu = engine.menu(PLAYER_ID, 123.5, 128.5, 0, radius=1)
    east = [entry for entry in menu.entries if _object_ids([entry]) == {east_id}]
    assert east
    assert all((entry.tile_dx, entry.tile_dy) == (1, 0) for entry in east)
    # "Put Shovel into Chest" is built from the chest's tile, so only ops on the tool itself count.
    own = [
        entry
        for entry in menu.entries
        if (entry.op in ("inspect", "drop") and _object_ids([entry]) == {tool_id})
        or isinstance(entry.action.target, SelfTarget)
    ]
    assert own
    assert all((entry.tile_dx, entry.tile_dy) == (0, 0) for entry in own)
    offsets = {(place.dx, place.dy) for place in menu.places}
    assert len(offsets) == len(menu.places)
    assert (0, 0) in offsets and (1, 0) in offsets
    # The wall closes the north neighbour, so it is not scanned and has no place.
    assert (0, -1) not in offsets
    assert {(entry.tile_dx, entry.tile_dy) for entry in menu.entries} <= offsets

    exact = engine.menu(PLAYER_ID, 124.5, 128.5, 0)
    assert all((entry.tile_dx, entry.tile_dy) == (0, 0) for entry in exact.entries)
    assert [(place.dx, place.dy) for place in exact.places] == [(0, 0)]


@pytest.mark.asyncio
async def test_throw_releases_held_object_and_returns_trajectory(engine: WorldEngine) -> None:
    place(engine, *PLAYER)
    bottle_id = spawn_tool(engine, "bottle")

    result = await engine.submit(
        PLAYER_ID,
        ThrowAction(target=ObjectTarget(id=bottle_id), dx=1, dy=0),
    )

    assert result.accepted
    assert len(result.trajectory) > 1
    assert result.load_kg == 0
    with engine.sessions() as session:
        bottle = session.get(ObjectRow, bottle_id)
        assert bottle is not None
        assert bottle.loc == "tile"
        assert bottle.x == result.trajectory[-1].x


@pytest.mark.asyncio
async def test_object_integrity_accumulates_then_replaces_container_with_rubble(
    engine: WorldEngine,
) -> None:
    place(engine, *PLAYER)
    chest_id = spawn_object(engine, "chest", 124, 128)
    tool_id = spawn_tool(engine)
    with engine.sessions() as session:
        bottle = ObjectRow(kind="bottle", loc="in", container_id=chest_id, quantity=2, state={})
        session.add(bottle)
        session.commit()
        bottle_id = bottle.id
    action = HitAction(target=ObjectTarget(id=chest_id), tool=ObjectTarget(id=tool_id))

    first = await engine.submit(PLAYER_ID, action)
    assert first.accepted
    with engine.sessions() as session:
        chest = session.get(ObjectRow, chest_id)
        assert chest is not None
        assert chest.integrity == pytest.approx(75)

    second = await engine.submit(PLAYER_ID, action)
    assert second.accepted
    with engine.sessions() as session:
        chest = session.get(ObjectRow, chest_id)
        bottle = session.get(ObjectRow, bottle_id)
        assert chest is not None
        assert bottle is not None
        assert chest.kind == "rubble"
        assert chest.integrity is None
        assert (bottle.loc, bottle.x, bottle.y, bottle.h) == ("tile", 124, 128, 2)


@pytest.mark.asyncio
async def test_wall_damage_survives_restart_and_breaks_into_opening(
    sessions: sessionmaker[Session], engine: WorldEngine
) -> None:
    place(engine, *PLAYER)
    tool_id = spawn_tool(engine)
    key = set_wall(engine, 124, 128, "west", "brick")
    edge = {"kind": "edge", "x": 124, "y": 128, "z": 0, "direction": "west"}
    action = BreakAction.model_validate(
        {"op": "break", "target": edge, "tool": {"kind": "object", "id": tool_id}}
    )

    first = await engine.submit(PLAYER_ID, action)
    assert first.accepted
    with sessions() as session:
        row = session.get(WallIntegrity, (key[0], key[1], key[2], key[3], "west"))
        assert row is not None
        assert row.integrity == pytest.approx(1275)

    engine = WorldEngine(sessions)
    engine.ensure_world(0)
    for _ in range(6):
        result = await engine.submit(PLAYER_ID, action)
        assert result.accepted

    assert not engine.grid.wall_between(PLAYER[0], PLAYER[1], PLAYER[0] + 1, PLAYER[1], 2)
    with sessions() as session:
        assert session.get(WallIntegrity, (key[0], key[1], key[2], key[3], "west")) is None
        level = session.get(ChunkLevelRow, (key[0], key[1], key[2]))
        assert level is not None
        assert level.wall_w[key[3]] == 0


@pytest.mark.asyncio
async def test_pushed_object_stops_at_wall_and_carries_its_damage(engine: WorldEngine) -> None:
    place(engine, *PLAYER)
    chest_id = spawn_object(engine, "chest", 124, 128)
    key = set_wall(engine, 125, 128, "west", "brick")

    result = await engine.submit(
        PLAYER_ID,
        PushAction(target=ObjectTarget(id=chest_id), dx=1, dy=0),
    )

    assert result.accepted
    assert result.trajectory[-1].x == 124
    with engine.sessions() as session:
        chest = session.get(ObjectRow, chest_id)
        wall = session.get(WallIntegrity, (key[0], key[1], key[2], key[3], "west"))
        assert chest is not None and chest.x == 124
        assert wall is not None and wall.integrity < 1500
    assert engine.read_events(event_type="impact", limit=10)


@pytest.mark.asyncio
async def test_pushed_body_falls_and_emits_impact_without_health_mutation(
    engine: WorldEngine,
) -> None:
    place(engine, *PLAYER)
    npc = Actor(id="physics_test", kind="human", x=124.5, y=128.5, z=0, h=2)
    with engine.sessions() as session:
        session.add(npc)
        session.commit()
    chunk = engine.grid.chunk(125 // CHUNK_SIZE, 128 // CHUNK_SIZE)
    assert chunk is not None
    index = chunk.index(125 % CHUNK_SIZE, 128 % CHUNK_SIZE)
    ground = list(chunk.ground_h)
    ground[index] = -5
    engine.grid.add_chunk(replace(chunk, ground_h=tuple(ground)))

    result = await engine.submit(
        PLAYER_ID,
        PushAction(target=ActorTarget(id=npc.id), dx=1, dy=0),
    )

    assert result.accepted
    assert result.trajectory[-1].h == -5
    impacts = engine.read_events(event_type="impact", limit=20)
    assert any(event.data.get("energy") == pytest.approx(2746.8) for event in impacts)
    with engine.sessions() as session:
        fallen = session.get(Actor, npc.id)
        assert fallen is not None
        assert (fallen.x, fallen.y, fallen.h, fallen.mass_kg) == (125.5, 128.5, -5, 80)
