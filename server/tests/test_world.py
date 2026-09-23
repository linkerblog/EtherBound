from collections.abc import Mapping
from dataclasses import replace
from pathlib import Path
from time import perf_counter

import pytest
from sqlalchemy import create_engine, select
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.migrate import upgrade
from etherbound.db.models import Base, WorldMeta
from etherbound.db.models import Chunk as ChunkRow
from etherbound.db.models import Material as MaterialRow
from etherbound.engine.world import WorldEngine
from etherbound.world.chunk import (
    CELL_COUNT,
    CHUNK_SIZE,
    EDGE_W_DOORWAY,
    NO_FLOOR,
    Chunk,
    ChunkLevel,
    decode_int16,
    encode_int16,
)
from etherbound.world.gen.testworld import GEN_VERSION, generate_test_world
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry
from etherbound.world.nav import find_path


def test_blob_round_trip() -> None:
    values = tuple(index - 512 for index in range(CELL_COUNT))
    assert decode_int16(encode_int16(values)) == values


def test_material_ids_append_to_existing_mapping(tmp_path: Path) -> None:
    path = tmp_path / "materials.toml"
    path.write_text(
        '[[materials]]\nkey = "new"\nname = "New"\ncolor = "#fff"\n'
        "walkable = true\nwalk_cost = 1\nsolid = true\nblocks_sight = false\n"
        "diggable = true\ndig_cost = 1\nflammable = false\ndensity = 1\n"
        "resistance = 1\nliquid = false\ntags = []\n",
        encoding="utf-8",
    )
    first = MaterialRegistry.load(path, {"old": 7})
    second = MaterialRegistry.load(path, {"old": 7, "new": 12})
    assert first["new"].id == 8
    assert second["new"].id == 12


def test_standing_rule_accepts_half_metre_and_rejects_one_metre() -> None:
    registry = MaterialRegistry.load()
    grass = registry["grass"].id
    chunk = Chunk(0, 0, (0,) * CELL_COUNT, (grass,) * CELL_COUNT)
    grid = WorldGrid([chunk], registry=registry)
    grid.add_chunk(Chunk(0, 0, (0, 1) + (0,) * (CELL_COUNT - 2), (grass,) * CELL_COUNT))
    assert grid.can_step(0, 0, 1, 0)
    grid.add_chunk(Chunk(0, 0, (0, 2) + (0,) * (CELL_COUNT - 2), (grass,) * CELL_COUNT))
    assert not grid.can_step(0, 0, 1, 0)


def test_wall_blocks_and_doorway_opens() -> None:
    registry = MaterialRegistry.load()
    grass = registry["grass"].id
    brick = registry["brick"].id
    grid = WorldGrid([Chunk.flat(0, 0, 0, grass)], registry=registry)
    floor = [0] * CELL_COUNT
    material = [grass] * CELL_COUNT
    wall_w = [0] * CELL_COUNT
    flags = [0] * CELL_COUNT
    wall_w[1] = brick
    level = ChunkLevel(
        0,
        0,
        0,
        tuple(floor),
        tuple(material),
        (0,) * CELL_COUNT,
        tuple(wall_w),
        (0,) * CELL_COUNT,
        tuple(flags),
    )
    grid.add_level(level)
    assert grid.wall_between(0, 0, 1, 0, 0)
    flags[1] = EDGE_W_DOORWAY
    grid.add_level(
        ChunkLevel(
            0,
            0,
            0,
            tuple(floor),
            tuple(material),
            (0,) * CELL_COUNT,
            tuple(wall_w),
            tuple(flags),
            tuple(flags),
        )
    )
    assert not grid.wall_between(0, 0, 1, 0, 0)


def test_test_world_is_deterministic_and_eight_by_eight() -> None:
    first = generate_test_world(123)
    second = generate_test_world(123)
    other = generate_test_world(124)
    assert len(first.chunks) == 64
    assert first.blob_bytes() == second.blob_bytes()
    assert first.blob_bytes() != other.blob_bytes()


def test_eight_by_eight_world_generation_stays_under_one_second() -> None:
    started = perf_counter()
    world = generate_test_world(0)
    elapsed = perf_counter() - started

    assert len(world.chunks) == 64
    assert elapsed < 1.0


def test_blob_bytes_cover_every_persisted_chunk_and_level_blob() -> None:
    world = generate_test_world(123)
    chunk_key = sorted(world.chunks)[0]
    chunk = world.chunks[chunk_key]
    dug = (1,) + chunk.dug[1:]
    assert replace(
        world, chunks={**world.chunks, chunk_key: replace(chunk, dug=dug)}
    ).blob_bytes() != (world.blob_bytes())
    changed_strata = ((0, "topsoil"), (8, "rock"))
    assert (
        replace(
            world,
            chunks={**world.chunks, chunk_key: replace(chunk, strata=changed_strata)},
        ).blob_bytes()
        != world.blob_bytes()
    )

    level_key = sorted(world.levels)[0]
    level = world.levels[level_key]
    for field in ("floor_h", "floor_mat", "wall_n", "wall_w", "edge_flags", "flags"):
        values = list(getattr(level, field))
        values[0] = 1 if values[0] != 1 else 2
        changed = replace(level, **{field: tuple(values)})
        assert replace(world, levels={**world.levels, level_key: changed}).blob_bytes() != (
            world.blob_bytes()
        )


def test_building_road_terrace_and_pit_are_reachable_across_seeds() -> None:
    registry = MaterialRegistry.load()
    start = (121, 128, 2)
    for seed in (0, 123, 1, 2, 3, 42):
        world = generate_test_world(seed, registry)
        grid = WorldGrid(world.chunks.values(), world.levels.values(), registry)
        ground_floor = (140, 140, 12)
        first_floor = (140, 140, 18)
        basement = (140, 140, 6)
        roof = (140, 140, 24)
        goals = (
            ground_floor,
            (60, 185, min(surface.h for surface in grid.standing_surfaces(60, 185))),
            (110, 88, min(surface.h for surface in grid.standing_surfaces(110, 88))),
        )
        for goal in goals:
            path = find_path(grid, start, goal)
            assert path is not None, f"seed {seed} cannot reach {goal}"
            assert path[0] == start and path[-1] == goal
        for origin, goal in (
            (ground_floor, first_floor),
            (ground_floor, basement),
            (first_floor, roof),
        ):
            path = find_path(grid, origin, goal)
            assert path is not None, f"seed {seed} cannot reach {goal} from {origin}"
            assert path[0] == origin and path[-1] == goal

        for y in range(255):
            assert grid.can_step(121, y, 121, y + 1, 2), f"seed {seed} road blocked at y={y}"

        for z in range(1, 5):
            for x, y, wall_field in (
                *((160, y, "wall_w") for y in range(132, 161)),
                *((x, 160, "wall_n") for x in range(136, 161)),
            ):
                cx, lx = divmod(x, CHUNK_SIZE)
                cy, ly = divmod(y, CHUNK_SIZE)
                level = world.levels[(cx, cy, z)]
                index = ly * CHUNK_SIZE + lx
                assert level.floor_h[index] == NO_FLOOR
                assert getattr(level, wall_field)[index] == registry["brick"].id


def test_material_registry_ids_survive_database_restart_and_toml_reorder(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    database_url = f"sqlite:///{(tmp_path / 'materials.db').as_posix()}"
    upgrade(database_url)
    database = create_engine(database_url)
    sessions = sessionmaker(bind=database, expire_on_commit=False, class_=Session)
    engine = WorldEngine(sessions)
    engine.ensure_world(123)
    original_ids = engine.registry.ids()
    original_chunks = {
        key: (chunk.ground_h, chunk.surface_mat) for key, chunk in engine.grid.chunks.items()
    }

    source = Path(__file__).parents[1] / "src" / "etherbound" / "world" / "materials.toml"
    updated = tmp_path / "materials.toml"
    new_material = (
        '[[materials]]\nkey = "added_for_test"\nname = "Added"\ncolor = "#fff"\n'
        "walkable = true\nwalk_cost = 1\nsolid = true\nblocks_sight = false\n"
        "diggable = true\ndig_cost = 1\nflammable = false\ndensity = 1\n"
        "resistance = 1\nliquid = false\ntags = []\n\n"
    )
    updated.write_text(new_material + source.read_text(encoding="utf-8"), encoding="utf-8")
    original_load = MaterialRegistry.load

    def load_updated(
        cls: type[MaterialRegistry],
        path: str | Path | None = None,
        existing_ids: Mapping[str, int] | None = None,
    ) -> MaterialRegistry:
        return original_load(path=updated, existing_ids=existing_ids)

    monkeypatch.setattr(MaterialRegistry, "load", classmethod(load_updated))
    restarted = WorldEngine(sessions)
    restarted.ensure_world()

    assert {key: restarted.registry[key].id for key in original_ids} == original_ids
    assert restarted.registry["added_for_test"].id == max(original_ids.values()) + 1
    assert {
        key: (chunk.ground_h, chunk.surface_mat) for key, chunk in restarted.grid.chunks.items()
    } == original_chunks
    road_chunk = restarted.grid.chunk(3, 4)
    assert road_chunk is not None
    assert restarted.registry[road_chunk.surface_mat[25]].key == "asphalt"
    with sessions() as session:
        rows = {row.key: row.id for row in session.scalars(select(MaterialRow))}
        assert rows["added_for_test"] == restarted.registry["added_for_test"].id
        meta = session.get(WorldMeta, 1)
        assert meta is not None and meta.gen_version == GEN_VERSION
    database.dispose()


def test_stale_generator_version_regenerates_existing_chunks(tmp_path: Path) -> None:
    database = create_engine(f"sqlite:///{(tmp_path / 'regen.db').as_posix()}")
    Base.metadata.create_all(database)
    sessions = sessionmaker(bind=database, expire_on_commit=False, class_=Session)
    engine = WorldEngine(sessions)
    engine.ensure_world(123)
    with sessions() as session:
        row = session.get(ChunkRow, (3, 4))
        assert row is not None
        row.ground_h = bytes(CELL_COUNT * 2)
        meta = session.get(WorldMeta, 1)
        assert meta is not None
        meta.gen_version = GEN_VERSION - 1
        session.commit()

    restarted = WorldEngine(sessions)
    restarted.ensure_world()
    restored = restarted.grid.chunk(3, 4)
    assert restored is not None and restored.ground_h[25] == 2
    assert restarted.get_state().gen_version == GEN_VERSION
    database.dispose()
