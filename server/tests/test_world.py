from pathlib import Path

from etherbound.world.chunk import (
    CELL_COUNT,
    EDGE_W_DOORWAY,
    Chunk,
    ChunkLevel,
    decode_int16,
    encode_int16,
)
from etherbound.world.gen.testworld import generate_test_world
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry


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
