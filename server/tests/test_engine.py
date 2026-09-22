import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Base
from etherbound.engine.actions import MoveAction
from etherbound.engine.world import PLAYER_ID, WorldEngine


@pytest.fixture
def engine() -> WorldEngine:
    database = create_engine("sqlite:///:memory:", connect_args={"check_same_thread": False})
    Base.metadata.create_all(database)
    sessions = sessionmaker(bind=database, expire_on_commit=False, class_=Session)
    world = WorldEngine(sessions)
    world.ensure_world(123)
    return world


async def test_move_uses_action_api_and_stops_at_collision_map_wall(engine: WorldEngine) -> None:
    result = await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=2)
    assert result.accepted
    assert result.x < 8
    assert result.x > 7


async def test_pause_rejects_movement(engine: WorldEngine) -> None:
    await engine.set_clock(paused=True)
    result = await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=1)
    assert not result.accepted
    assert result.reason == "paused"


async def test_game_minute_persists_for_a_restarted_engine(engine: WorldEngine) -> None:
    await engine.advance_time()
    assert engine.get_state().game_minute == 1

    restarted = WorldEngine(engine.sessions, engine.game_map)
    assert restarted.get_state().game_minute == 1
