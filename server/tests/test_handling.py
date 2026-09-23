from typing import Any

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, Base
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import (
    CloseAction,
    DigAction,
    DropAction,
    InspectAction,
    ObjectTarget,
    OpenAction,
    PutAction,
    RemoveAction,
    TakeAction,
    TileTarget,
    WearAction,
)
from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.world.materials import MaterialRegistry
from etherbound.world.objects import Container, ObjectCatalog, ObjectKind

# The open grass around (128, 128) is flat at h = 1 with no generated objects.
HERE = (128, 128)


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
    ground = engine.grid.ground_at(x, y)
    assert ground is not None
    with engine.sessions() as session:
        actor = session.get(Actor, PLAYER_ID)
        assert actor is not None
        actor.x, actor.y = x + 0.5, y + 0.5
        actor.h = ground[0] if h is None else h
        actor.z = actor.h // 6
        session.commit()


def spawn(
    engine: WorldEngine,
    kind: str,
    x: int,
    y: int,
    h: int,
    quantity: int = 1,
    open_: bool = False,
    container_id: int | None = None,
) -> int:
    with engine.sessions() as session:
        row = ObjectRow(kind=kind, quantity=quantity, state={"open": True} if open_ else {})
        if container_id is not None:
            row.loc = "in"
            row.container_id = container_id
        else:
            row.loc = "tile"
            row.x, row.y, row.h = x, y, h
            row.cx, row.cy = x // 32, y // 32
        session.add(row)
        session.flush()
        engine._load_object_index(session)
        session.commit()
        return row.id


def get(engine: WorldEngine, object_id: int) -> ObjectRow:
    with engine.sessions() as session:
        row = session.get(ObjectRow, object_id)
        assert row is not None
        return row


def moved(engine: WorldEngine) -> list[dict[str, Any]]:
    return [row.data for row in engine.read_events(event_type="object.moved", limit=500)]


def changed(engine: WorldEngine) -> list[dict[str, Any]]:
    return [row.data for row in engine.read_events(event_type="object.changed", limit=500)]


def revision(engine: WorldEngine, x: int, y: int) -> int:
    chunk = engine.grid.chunk(x // 32, y // 32)
    assert chunk is not None
    return chunk.revision


def subjects(engine: WorldEngine, x: int, y: int, z: int = 0) -> list[tuple[str, str | None]]:
    return [
        (entry.op, entry.subject) for entry in engine.menu(PLAYER_ID, x + 0.5, y + 0.5, z).entries
    ]


async def test_take_moves_to_a_hand_and_bumps_the_chunk(engine: WorldEngine) -> None:
    shovel = spawn(engine, "shovel", *HERE, 1)
    place(engine, *HERE)
    before = revision(engine, *HERE)

    result = await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=shovel)))

    assert result.accepted
    assert [(item.kind, item.quantity, item.slot) for item in result.carried] == [
        ("shovel", 1, "right")
    ]
    assert result.load_kg == pytest.approx(2.0)
    row = get(engine, shovel)
    assert (row.loc, row.slot, row.actor_id, row.x) == ("held", "right", PLAYER_ID, None)
    data = moved(engine)[-1]
    assert data["object_id"] == shovel and data["op"] == "take"
    assert engine.read_events(event_type="object.moved", limit=500)[-1].actor_id == PLAYER_ID
    assert data["from"] == {"kind": "tile", "x": 128, "y": 128, "h": 1}
    assert data["to"] == {"kind": "held", "actor_id": PLAYER_ID, "hand": "right"}
    assert data["split_from"] is None and data["merged_into"] is None
    assert revision(engine, *HERE) == before + 1


async def test_take_splits_a_stack_and_drop_merges_it_back(engine: WorldEngine) -> None:
    stack = spawn(engine, "apple", *HERE, 1, quantity=3)
    place(engine, *HERE)

    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=stack)))).accepted
    assert get(engine, stack).quantity == 2
    carried = next(item for item in engine.get_state().actors[0].carried if item.kind == "apple")
    assert moved(engine)[-1]["split_from"] == stack

    assert (await engine.submit(PLAYER_ID, DropAction(target=ObjectTarget(id=carried.id)))).accepted
    data = moved(engine)[-1]
    assert data["op"] == "drop" and data["merged_into"] == stack
    assert get(engine, stack).quantity == 3
    with engine.sessions() as session:
        rows = session.query(ObjectRow).filter_by(kind="apple", x=128, y=128).all()
    assert [row.id for row in rows] == [stack]


async def test_take_refusals(engine: WorldEngine) -> None:
    place(engine, 131, 128)  # Open grass, three tiles away.
    distant = spawn(engine, "shovel", *HERE, 1)
    result = await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=distant)))
    assert (result.accepted, result.reason) == (False, "out of reach")

    chest = spawn(engine, "chest", 129, 128, 1)
    apple = spawn(engine, "apple", 129, 128, 1, container_id=chest)
    place(engine, *HERE)
    result = await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=apple)))
    assert (result.accepted, result.reason) == (False, "closed")

    table = spawn(engine, "table", 127, 128, 1)
    spawn(engine, "bottle", 127, 128, 3)
    result = await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=table)))
    assert (result.accepted, result.reason) == (False, "something is on it")

    shovel = spawn(engine, "shovel", *HERE, 1)
    backpack = spawn(engine, "backpack", *HERE, 1)
    apple = spawn(engine, "apple", *HERE, 1)
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=shovel)))).accepted
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=backpack)))).accepted
    result = await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=apple)))
    assert (result.accepted, result.reason) == (False, "hands full")


async def test_a_solid_object_cannot_be_dropped_at_the_feet(engine: WorldEngine) -> None:
    place(engine, *HERE)
    chest = spawn(engine, "chest", 127, 128, 1)
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=chest)))).accepted
    result = await engine.submit(PLAYER_ID, DropAction(target=ObjectTarget(id=chest)))
    assert (result.accepted, result.reason) == (False, "no room")


async def test_open_close_state_and_hidden_contents(engine: WorldEngine) -> None:
    place(engine, 124, 128, 2)
    chest = next(obj.id for obj in engine.grid.objects_at(124, 127) if obj.kind == "chest")
    payload = engine.chunk_payload(3, 3)
    kinds = [obj.kind for obj in payload.objects]
    assert set(kinds) <= {"shovel", "backpack", "chest"}
    assert "apple" not in kinds and "bottle" not in kinds

    opened = await engine.submit(PLAYER_ID, OpenAction(target=ObjectTarget(id=chest)))
    assert opened.accepted
    assert get(engine, chest).state["open"] is True
    assert changed(engine)[-1]["changes"] == {"open": True}
    assert ("take", "Apple ×3") in subjects(engine, 124, 127)
    assert ("take", "Bottle ×2") in subjects(engine, 124, 127)

    again = await engine.submit(PLAYER_ID, OpenAction(target=ObjectTarget(id=chest)))
    assert (again.accepted, again.reason) == (False, "already open")
    closed = await engine.submit(PLAYER_ID, CloseAction(target=ObjectTarget(id=chest)))
    assert closed.accepted and get(engine, chest).state["open"] is False
    again = await engine.submit(PLAYER_ID, CloseAction(target=ObjectTarget(id=chest)))
    assert (again.accepted, again.reason) == (False, "already closed")
    closed_subjects = [subject for op, subject in subjects(engine, 124, 127) if op == "take"]
    assert not any(
        subject and ("Apple" in subject or "Bottle" in subject) for subject in closed_subjects
    )


async def test_open_refused_when_something_is_on_the_lid(engine: WorldEngine) -> None:
    place(engine, *HERE)
    chest = spawn(engine, "chest", 129, 128, 1)
    spawn(engine, "bottle", 129, 128, 3)
    result = await engine.submit(PLAYER_ID, OpenAction(target=ObjectTarget(id=chest)))
    assert (result.accepted, result.reason) == (False, "something is on it")


async def test_put_into_a_container_and_onto_a_surface(engine: WorldEngine) -> None:
    place(engine, *HERE)
    chest = spawn(engine, "chest", 129, 128, 1, open_=True)
    table = spawn(engine, "table", 127, 128, 1)
    shovel = spawn(engine, "shovel", *HERE, 1)
    bottle = spawn(engine, "bottle", *HERE, 1)

    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=shovel)))).accepted
    assert (
        await engine.submit(
            PLAYER_ID, PutAction(target=ObjectTarget(id=shovel), into=ObjectTarget(id=chest))
        )
    ).accepted
    row = get(engine, shovel)
    assert (row.loc, row.container_id) == ("in", chest)

    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=bottle)))).accepted
    assert (
        await engine.submit(
            PLAYER_ID, PutAction(target=ObjectTarget(id=bottle), into=ObjectTarget(id=table))
        )
    ).accepted
    row = get(engine, bottle)
    assert (row.loc, row.x, row.y, row.h) == ("tile", 127, 128, 3)


async def test_put_refusals(engine: WorldEngine) -> None:
    place(engine, *HERE)
    shovel = spawn(engine, "shovel", *HERE, 1)
    closed = spawn(engine, "chest", 129, 128, 1)
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=shovel)))).accepted
    result = await engine.submit(
        PLAYER_ID, PutAction(target=ObjectTarget(id=shovel), into=ObjectTarget(id=closed))
    )
    assert (result.accepted, result.reason) == (False, "closed")

    far = spawn(engine, "barrel", 135, 128, 1)
    result = await engine.submit(
        PLAYER_ID, PutAction(target=ObjectTarget(id=shovel), into=ObjectTarget(id=far))
    )
    assert (result.accepted, result.reason) == (False, "out of reach")

    open_chest = spawn(engine, "chest", 127, 128, 1, open_=True)
    assert open_chest is not None
    result = await engine.submit(
        PLAYER_ID,
        PutAction(target=ObjectTarget(id=shovel), into=ObjectTarget(id=shovel)),
    )
    assert (result.accepted, result.reason) == (False, "not into itself")

    apple = spawn(engine, "apple", *HERE, 1)
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=apple)))).accepted
    result = await engine.submit(
        PLAYER_ID, PutAction(target=ObjectTarget(id=apple), into=TileTarget(x=128, y=128, h=1))
    )
    assert (result.accepted, result.reason) == (False, "someone is there")

    result = await engine.submit(
        PLAYER_ID, PutAction(target=ObjectTarget(id=apple), into=TileTarget(x=128, y=128, h=-1))
    )
    assert (result.accepted, result.reason) == (False, "no room")


async def test_wear_and_remove(engine: WorldEngine) -> None:
    place(engine, *HERE)
    backpack = spawn(engine, "backpack", *HERE, 1)
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=backpack)))).accepted
    assert (await engine.submit(PLAYER_ID, WearAction(target=ObjectTarget(id=backpack)))).accepted
    row = get(engine, backpack)
    assert (row.loc, row.slot) == ("worn", "back")
    assert engine.get_state().actors[0].load_kg == pytest.approx(1.0)

    shovel = spawn(engine, "shovel", *HERE, 1)
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=shovel)))).accepted
    assert (await engine.submit(PLAYER_ID, RemoveAction(target=ObjectTarget(id=backpack)))).accepted
    assert get(engine, backpack).loc == "held"

    second = spawn(engine, "backpack", *HERE, 1)
    # The first backpack is now in a hand; wear it again and then try the second.
    assert (await engine.submit(PLAYER_ID, WearAction(target=ObjectTarget(id=backpack)))).accepted
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=second)))).accepted
    result = await engine.submit(PLAYER_ID, WearAction(target=ObjectTarget(id=second)))
    assert (result.accepted, result.reason) == (False, "slot taken")


async def test_remove_refused_when_hands_are_full(engine: WorldEngine) -> None:
    place(engine, *HERE)
    backpack = spawn(engine, "backpack", *HERE, 1)
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=backpack)))).accepted
    assert (await engine.submit(PLAYER_ID, WearAction(target=ObjectTarget(id=backpack)))).accepted
    shovel = spawn(engine, "shovel", *HERE, 1)
    bottle = spawn(engine, "bottle", *HERE, 1)
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=shovel)))).accepted
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=bottle)))).accepted
    result = await engine.submit(PLAYER_ID, RemoveAction(target=ObjectTarget(id=backpack)))
    assert (result.accepted, result.reason) == (False, "hands full")


async def test_inspect_on_objects(engine: WorldEngine) -> None:
    place(engine, *HERE)
    shovel = spawn(engine, "shovel", *HERE, 1)
    result = await engine.submit(PLAYER_ID, InspectAction(target=ObjectTarget(id=shovel)))
    assert (result.accepted, result.text) == (True, "Shovel · Metal")

    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=shovel)))).accepted
    result = await engine.submit(PLAYER_ID, InspectAction(target=ObjectTarget(id=shovel)))
    assert result.text is not None and result.text.startswith("Shovel · Metal · 2.0 kg")

    far = spawn(engine, "apple", 160, 128, 0)
    result = await engine.submit(PLAYER_ID, InspectAction(target=ObjectTarget(id=far)))
    assert (result.accepted, result.reason) == (False, "too far to see")


async def test_dig_uses_a_held_tool_and_an_object_blocks_it(engine: WorldEngine) -> None:
    place(engine, 125, 126, 2)
    shovel = next(obj.id for obj in engine.grid.objects_at(124, 126) if obj.kind == "shovel")
    assert (await engine.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=shovel)))).accepted
    result = await engine.submit(PLAYER_ID, DigAction(target=TileTarget(x=124, y=126, h=2)))
    assert result.accepted and result.activity is not None
    assert result.activity.ends_minute - result.activity.started_minute == 24

    apple = spawn(engine, "apple", 125, 127, 2)
    result = await engine.submit(PLAYER_ID, DigAction(target=TileTarget(x=125, y=127, h=2)))
    assert (result.accepted, result.reason) == (False, "something is on it")
    assert apple


def _custom_catalog() -> ObjectCatalog:
    base = ObjectCatalog.load(registry=MaterialRegistry.load())
    extra = (
        ObjectKind(
            key="anvil",
            name="Anvil",
            material="metal",
            mass=50.0,
            bulk=40.0,
            height=1,
            solid=True,
            surface=False,
            fixed=True,
            stackable=False,
        ),
        ObjectKind(
            key="boulder",
            name="Boulder",
            material="metal",
            mass=60.0,
            bulk=10.0,
            height=0,
            solid=False,
            surface=False,
            fixed=False,
            stackable=False,
        ),
        ObjectKind(
            key="pouch",
            name="Pouch",
            material="cloth",
            mass=0.5,
            bulk=2.0,
            height=0,
            solid=False,
            surface=False,
            fixed=False,
            stackable=False,
            container=Container(capacity=1.0),
        ),
    )
    return ObjectCatalog((*base.kinds, *extra))


async def test_custom_kinds_fixed_heavy_and_doesnt_fit(
    sessions: sessionmaker[Session],
) -> None:
    world = WorldEngine(sessions, catalog=_custom_catalog())
    world.ensure_world(0)
    place(world, *HERE)

    anvil = spawn(world, "anvil", 129, 128, 1)
    result = await world.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=anvil)))
    assert (result.accepted, result.reason) == (False, "fixed in place")

    boulder = spawn(world, "boulder", 127, 128, 1)
    result = await world.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=boulder)))
    assert (result.accepted, result.reason) == (False, "too heavy")

    pouch = spawn(world, "pouch", 129, 128, 1)
    shovel = spawn(world, "shovel", *HERE, 1)
    assert (await world.submit(PLAYER_ID, TakeAction(target=ObjectTarget(id=shovel)))).accepted
    result = await world.submit(
        PLAYER_ID, PutAction(target=ObjectTarget(id=shovel), into=ObjectTarget(id=pouch))
    )
    assert (result.accepted, result.reason) == (False, "doesn't fit")
