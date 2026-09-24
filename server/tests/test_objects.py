from pathlib import Path

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, Base
from etherbound.engine.actions import ClimbAction, TileTarget
from etherbound.engine.movement import load_multiplier, move_in_world
from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.world.materials import MaterialRegistry
from etherbound.world.nav import find_path
from etherbound.world.objects import ObjectCatalog, Tool, Wearable

CHEST_TOP = (124, 127)  # Test-world closed chest, resting at h = 2; its top is h = 4.
STACK = (126, 127)  # Test-world two chests stacked on ground h = 1; the upper top is h = 5.
NEIGHBOUR = (125, 127)  # Grass at h = 2, next to the stack.


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


async def ticks(engine: WorldEngine, count: int) -> None:
    for _ in range(count):
        await engine.advance_time()


def actor_h(engine: WorldEngine) -> int:
    with engine.sessions() as session:
        actor = session.get(Actor, PLAYER_ID)
        assert actor is not None
        return actor.h


def load(tmp_path: Path, body: str) -> ObjectCatalog:
    path = tmp_path / "objects.toml"
    path.write_text(body, encoding="utf-8")
    return ObjectCatalog.load(path, MaterialRegistry.load())


VALID = """
[[kinds]]
key = "widget"
name = "Widget"
material = "wood"
mass = 1.0
bulk = 2.0
height = 0
"""


def test_catalog_loads_the_v1_kinds() -> None:
    catalog = ObjectCatalog.load()
    assert catalog.keys() == (
        "chest",
        "barrel",
        "table",
        "chair",
        "shelf",
        "backpack",
        "shovel",
        "sledgehammer",
        "bottle",
        "apple",
        "rubble",
    )
    chest = catalog["chest"]
    assert (chest.mass, chest.bulk, chest.height) == (15.0, 120.0, 2)
    assert chest.solid and chest.surface and chest.openable
    assert chest.container is not None and chest.container.capacity == 100.0
    assert catalog["shelf"].openable is False
    assert catalog["backpack"].wearable == Wearable(slot="back")
    assert catalog["shovel"].tool == Tool(dig=1.0, strike_speed_m_s=15.0)
    assert catalog["sledgehammer"].tool == Tool(strike_speed_m_s=15.0)
    assert catalog["apple"].stackable and not catalog["apple"].solid
    assert catalog["rubble"].material == "rock" and catalog["rubble"].stackable


@pytest.mark.parametrize(
    ("body", "match"),
    [
        (VALID + "\n" + VALID, "unique"),
        (VALID.replace('material = "wood"', 'material = "unobtanium"'), "unregistered material"),
        (VALID.replace("height = 0", "height = 1"), "height must be positive"),
        (VALID.replace("height = 0", "height = 0\nsurface = true"), "surface requires solid"),
        (
            VALID.replace('material = "wood"', 'material = "food"').replace(
                "height = 0", "height = 2\nsolid = true\nsurface = true"
            ),
            "surface material must be walkable",
        ),
        (VALID + "stackable = true\n[kinds.container]\ncapacity = 1.0\n", "stackable excludes"),
        (VALID + "[kinds.openable]\n", "openable requires container"),
        (VALID + '[kinds.wearable]\nslot = "head"\n', "wearable slot"),
        (VALID + "[kinds.tool]\ndig = 0.0\n", "tool.dig must be positive"),
        (VALID + "[kinds.tool]\n", "tool requires a capability"),
        (
            VALID + "[kinds.tool]\nstrike_speed_m_s = 0.0\n",
            "tool.strike_speed_m_s must be positive",
        ),
        (VALID + 'colour = "red"\n', "unknown keys"),
        (VALID + "[kinds.container]\ncapacity = 1.0\nwrong = 1.0\n", "unknown container keys"),
    ],
)
def test_catalog_rejects_broken_kinds(tmp_path: Path, body: str, match: str) -> None:
    with pytest.raises(ValueError, match=match):
        load(tmp_path, body)


def test_grid_volume_makes_ground_unstandable_and_the_top_standable(engine: WorldEngine) -> None:
    assert [obj.kind for obj in engine.grid.objects_at(*CHEST_TOP)] == ["chest"]
    assert engine.grid.solid_at(*CHEST_TOP, 3) and engine.grid.solid_at(*CHEST_TOP, 4)
    assert not engine.grid.solid_at(*CHEST_TOP, 5)
    surfaces = [surface.h for surface in engine.grid.standing_surfaces(*CHEST_TOP)]
    assert surfaces == [4]
    resting = [surface.h for surface in engine.grid.resting_surfaces(*CHEST_TOP)]
    assert 2 in resting and 4 in resting


def test_navigation_goes_around_a_chest(engine: WorldEngine) -> None:
    path = find_path(engine.grid, (123, 127, 2), (125, 127, 2))
    assert path is not None
    assert (124, 127, 2) not in path


async def test_climb_onto_a_chest_and_back_down(engine: WorldEngine) -> None:
    place(engine, 124, 128, 2)
    target = TileTarget(x=124, y=127, h=4)
    assert (await engine.submit(PLAYER_ID, ClimbAction(target=target))).accepted
    await ticks(engine, 1)
    assert actor_h(engine) == 4
    down = TileTarget(x=124, y=128, h=2)
    assert (await engine.submit(PLAYER_ID, ClimbAction(target=down))).accepted
    await ticks(engine, 1)
    assert actor_h(engine) == 2


async def test_stacked_chests_block_walking_and_climb_from_a_neighbour(engine: WorldEngine) -> None:
    surfaces = [surface.h for surface in engine.grid.standing_surfaces(*STACK)]
    # The lower lid is covered by the upper chest, so only the upper top is standable.
    assert surfaces == [5]
    place(engine, *NEIGHBOUR, 2)
    up = TileTarget(x=STACK[0], y=STACK[1], h=5)
    assert (await engine.submit(PLAYER_ID, ClimbAction(target=up))).accepted
    await ticks(engine, 1)
    assert actor_h(engine) == 5
    down = TileTarget(x=NEIGHBOUR[0], y=NEIGHBOUR[1], h=2)
    assert (await engine.submit(PLAYER_ID, ClimbAction(target=down))).accepted
    await ticks(engine, 1)
    assert actor_h(engine) == 2


def test_load_multiplier_and_move_in_world(engine: WorldEngine) -> None:
    assert load_multiplier(10.0) == 1.0
    assert load_multiplier(40.0) == pytest.approx(0.5)
    assert load_multiplier(70.0) == 0.5
    start = (127.5, 128.5, 1)
    free = move_in_world(*start, 1.0, 0.0, 2.0, engine.grid, 0.0)
    loaded = move_in_world(*start, 1.0, 0.0, 2.0, engine.grid, 40.0)
    assert free[0] > loaded[0]
    assert loaded[0] - start[0] == pytest.approx((free[0] - start[0]) * 0.5, abs=0.2)
