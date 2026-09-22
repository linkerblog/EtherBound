import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, Base
from etherbound.engine.actions import MoveAction
from etherbound.engine.movement import move_in_world
from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.world.chunk import CELL_COUNT, Chunk
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry


@pytest.fixture
def engine() -> WorldEngine:
    database = create_engine("sqlite:///:memory:", connect_args={"check_same_thread": False})
    Base.metadata.create_all(database)
    sessions = sessionmaker(bind=database, expire_on_commit=False, class_=Session)
    world = WorldEngine(sessions)
    world.ensure_world(123)
    return world


def place_actor(engine: WorldEngine, x: float, y: float) -> None:
    """Teleport Niko for a test: the engine loads chunks eagerly, so this is read-only setup."""
    tile_x, tile_y = int(x), int(y)
    surface = max(engine.grid.standing_surfaces(tile_x, tile_y), key=lambda item: item.h)
    with engine.sessions() as session:
        actor = session.get(Actor, PLAYER_ID)
        assert actor is not None
        actor.x, actor.y = x, y
        actor.h, actor.z = surface.h, surface.h // 6
        session.commit()


async def test_move_uses_action_api_and_tracks_height(engine: WorldEngine) -> None:
    result = await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=2)
    assert result.accepted
    assert result.x > 0.5
    assert result.h >= 0


async def test_climbing_the_building_stairs_raises_h_and_z(engine: WorldEngine) -> None:
    place_actor(engine, 137.5, 150.5)
    result = await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=10)
    assert result.accepted
    assert result.h == 18
    assert result.z == 3


def _flat_grid() -> WorldGrid:
    registry = MaterialRegistry.load()
    return WorldGrid([Chunk.flat(0, 0, 4, registry["grass"].id)], registry=registry)


def _sloped_grid() -> WorldGrid:
    registry = MaterialRegistry.load()
    heights = tuple(min(4 + x, 10) for y in range(32) for x in range(32))
    chunk = Chunk(0, 0, heights, (registry["grass"].id,) * CELL_COUNT)
    return WorldGrid([chunk], registry=registry)


def test_uphill_walking_covers_less_distance_than_flat() -> None:
    flat_x, _, _ = move_in_world(0.5, 16.5, 4, 1, 0, 8.0, _flat_grid())
    uphill_x, _, uphill_h = move_in_world(0.5, 16.5, 4, 1, 0, 8.0, _sloped_grid())
    assert flat_x - 0.5 > uphill_x - 0.5
    assert uphill_h > 4


async def test_pause_rejects_movement(engine: WorldEngine) -> None:
    await engine.set_clock(paused=True)
    result = await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=1)
    assert not result.accepted
    assert result.reason == "paused"


async def test_game_minute_persists_for_a_restarted_engine(engine: WorldEngine) -> None:
    await engine.advance_time()
    assert engine.get_state().game_minute == 1

    restarted = WorldEngine(engine.sessions)
    assert restarted.get_state().game_minute == 1


async def test_new_game_wipes_and_regenerates(engine: WorldEngine) -> None:
    state = await engine.new_game(999)
    assert state.seed == 999
    assert engine.grid.chunks
    niko = next(actor for actor in state.actors if actor.id == PLAYER_ID)
    assert engine._stands(niko.x, niko.y, niko.h)
