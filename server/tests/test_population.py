"""Seeded Extras: placement is a pure grid pass, reachable, spaced and reproducible."""

from math import floor, hypot
from pathlib import Path

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, Base
from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.world.gen.registry import get_generator
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry
from etherbound.world.nav import find_path
from etherbound.world.objects import ObjectCatalog
from etherbound.world.population import EXTRA_COUNT, PLACEMENT_RADIUS_M, GeneratedActor, populate

GENERATORS = ["test", "lab"]
SEED = 0
MAX_EXPANSIONS = 3000


def _world(generator: str) -> tuple[WorldGrid, tuple[float, float, int]]:
    registry = MaterialRegistry.load()
    catalog = ObjectCatalog.load()
    spec = get_generator(generator)
    assert spec is not None
    options = spec.options()
    generated = spec.generate(SEED, options, registry)
    grid = WorldGrid(
        generated.chunks.values(), generated.levels.values(), registry, catalog=catalog
    )
    return grid, spec.spawn(SEED, options)


@pytest.mark.parametrize("generator", GENERATORS)
def test_populate_is_seeded_reachable_and_spaced(generator: str) -> None:
    grid, spawn = _world(generator)
    registry = MaterialRegistry.load()

    first = populate(grid, registry, spawn, SEED)
    second = populate(grid, registry, spawn, SEED)

    assert first == second, "the same seed twice must give the same Extras"
    assert len(first) == EXTRA_COUNT
    assert len({extra.name for extra in first}) == EXTRA_COUNT
    assert len({extra.id for extra in first}) == EXTRA_COUNT

    start = (floor(spawn[0]), floor(spawn[1]), spawn[2])
    for extra in first:
        assert (extra.anchor_x, extra.anchor_y, extra.anchor_h) != start
        assert hypot(extra.x - spawn[0], extra.y - spawn[1]) <= PLACEMENT_RADIUS_M
        ground = grid.ground_at(extra.anchor_x, extra.anchor_y)
        assert ground is not None and ground[0] == extra.anchor_h
        material = registry.get(ground[1])
        assert material is not None and material.walkable
        assert any(
            surface.h == extra.anchor_h
            for surface in grid.standing_surfaces(extra.anchor_x, extra.anchor_y)
        )
        assert (
            find_path(grid, start, (extra.anchor_x, extra.anchor_y, extra.anchor_h), MAX_EXPANSIONS)
            is not None
        )

    for index, extra in enumerate(first):
        for other in first[index + 1 :]:
            assert hypot(extra.anchor_x - other.anchor_x, extra.anchor_y - other.anchor_y) >= 2.0


def test_populate_touches_no_database() -> None:
    # populate takes a grid and a stream only: building the grid needs no session, so neither
    # does population. A save is written later by the engine.
    grid, spawn = _world("test")
    result: tuple[GeneratedActor, ...] = populate(grid, MaterialRegistry.load(), spawn, SEED)
    assert result


@pytest.fixture
def engine(tmp_path: Path) -> WorldEngine:
    database = create_engine(
        f"sqlite:///{(tmp_path / 'seed.db').as_posix()}", connect_args={"check_same_thread": False}
    )
    Base.metadata.create_all(database)
    sessions = sessionmaker(bind=database, expire_on_commit=False, class_=Session)
    world = WorldEngine(sessions)
    world.ensure_world(3)
    return world


def test_ensure_world_adds_extras_once_to_a_save_without_them(engine: WorldEngine) -> None:
    # A pre-Dev-018 save has no Extras: opening it seeds them and logs only their spawns.
    with engine.sessions() as session:
        session.query(Actor).where(Actor.kind == "extra").delete()
        session.commit()
    marker = engine.read_events(0, 500, None, None)[-1].seq

    engine.ensure_world(3)

    extras = [actor for actor in engine.get_state().actors if actor.kind == "extra"]
    assert len(extras) == EXTRA_COUNT
    fresh = engine.read_events(marker, 500, None, None)
    assert [row.type for row in fresh] == ["actor.spawned"] * EXTRA_COUNT
    assert {row.data["reason"] for row in fresh} == {"created"}
    assert all(row.actor_id != PLAYER_ID for row in fresh)

    marker = engine.read_events(0, 500, None, None)[-1].seq
    engine.ensure_world(3)
    assert engine.read_events(marker, 500, None, None) == []
