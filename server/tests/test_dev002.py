from dataclasses import replace
from json import loads
from pathlib import Path
from typing import Any

from alembic import command
from alembic.config import Config
from fastapi.testclient import TestClient
from sqlalchemy import create_engine, inspect, text

from etherbound.app import create_app
from etherbound.config import Settings
from etherbound.engine.world import PLAYER_ID
from etherbound.world.chunk import (
    CELL_COUNT,
    EDGE_W_WINDOW,
    LEVEL_CLIMBABLE,
    NO_FLOOR,
    Chunk,
    ChunkLevel,
)
from etherbound.world.gen.testworld import generate_test_world
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry
from etherbound.world.nav import find_path


def _registry() -> MaterialRegistry:
    return MaterialRegistry.load()


def _shared_parity_levels(
    specifications: list[dict[str, Any]], registry: MaterialRegistry
) -> list[ChunkLevel]:
    levels = []
    index = 33
    for spec in specifications:
        floor_h = [NO_FLOOR] * CELL_COUNT
        floor_mat = [0] * CELL_COUNT
        wall_n = [0] * CELL_COUNT
        wall_w = [0] * CELL_COUNT
        edge_flags = [0] * CELL_COUNT
        flags = [0] * CELL_COUNT
        if "floor_h" in spec:
            floor_h[index] = int(spec["floor_h"])
            floor_mat[index] = registry[str(spec["floor_material"])].id
        if "wall_n" in spec:
            wall_n[index] = registry[str(spec["wall_n"])].id
        if "wall_w" in spec:
            wall_w[index] = registry[str(spec["wall_w"])].id
        edge_flags[index] = int(spec.get("edge_flags", 0))
        flags[index] = int(spec.get("flags", 0))
        levels.append(
            ChunkLevel(
                0,
                0,
                int(spec["z"]),
                tuple(floor_h),
                tuple(floor_mat),
                tuple(wall_n),
                tuple(wall_w),
                tuple(edge_flags),
                tuple(flags),
            )
        )
    return levels


def _world_parity_cases() -> dict[str, list[dict[str, Any]]]:
    return loads(Path(__file__).with_name("world-parity.json").read_text(encoding="utf-8"))


def test_shared_client_server_standing_rule_table() -> None:
    registry = _registry()
    for case in _world_parity_cases()["standing"]:
        ground_h = int(case["ground_h"])
        ground_material = registry[str(case["ground_material"])].id
        chunk = Chunk.flat(0, 0, ground_h, ground_material)
        grid = WorldGrid(
            [chunk],
            _shared_parity_levels(case["levels"], registry),
            registry,
        )
        actor_h = int(case["actor_h"])
        surfaces = [
            surface.h for surface in grid.standing_surfaces(1, 1) if abs(surface.h - actor_h) <= 1
        ]
        actual = min(surfaces, key=lambda height: abs(height - actor_h)) if surfaces else None
        assert actual == case["expected_h"], case["name"]


def test_shared_client_server_wall_rule_table() -> None:
    registry = _registry()
    for case in _world_parity_cases()["walls"]:
        chunk = Chunk.flat(0, 0, int(case["ground_h"]), registry["grass"].id)
        grid = WorldGrid(
            [chunk],
            _shared_parity_levels(case["levels"], registry),
            registry,
        )
        actor_h = int(case["actor_h"])
        if case["direction"] == "north":
            blocked = grid.wall_between(1, 1, 1, 0, actor_h)
        else:
            blocked = grid.wall_between(1, 1, 0, 1, actor_h)
        assert blocked is case["expected_blocked"], case["name"]


def test_low_ceiling_blocks_and_void_unblocks() -> None:
    registry = _registry()
    grass = registry["grass"].id
    concrete = registry["concrete"].id
    # Ground at h=12 buries a basement floor at h=6 unless the band is excavated.
    chunk = Chunk(0, 0, (12,) * CELL_COUNT, (grass,) * CELL_COUNT)
    grid = WorldGrid([chunk], registry=registry)
    buried = ChunkLevel(
        0,
        0,
        1,
        (6,) * CELL_COUNT,
        (concrete,) * CELL_COUNT,
        (0,) * CELL_COUNT,
        (0,) * CELL_COUNT,
        (0,) * CELL_COUNT,
        (0,) * CELL_COUNT,
    )
    grid.add_level(buried)
    assert not grid.can_step(0, 0, 1, 0, 6)
    flags = [0] * CELL_COUNT
    for index in range(CELL_COUNT):
        flags[index] = 1  # LEVEL_VOID
    grid.add_level(
        ChunkLevel(
            0,
            0,
            1,
            (6,) * CELL_COUNT,
            (concrete,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            tuple(flags),
        )
    )
    assert grid.can_step(0, 0, 1, 0, 6)


def test_deep_water_is_not_walkable() -> None:
    registry = _registry()
    grass = registry["grass"].id
    deep = registry["water_deep"].id
    mats = [grass] * CELL_COUNT
    mats[1] = deep
    grid = WorldGrid([Chunk(0, 0, (0,) * CELL_COUNT, tuple(mats))], registry=registry)
    assert not grid.can_step(0, 0, 1, 0, 0)


def test_window_blocks_bodies() -> None:
    registry = _registry()
    grass = registry["grass"].id
    brick = registry["brick"].id
    grid = WorldGrid([Chunk.flat(0, 0, 0, grass)], registry=registry)
    flags = [0] * CELL_COUNT
    flags[1] = EDGE_W_WINDOW
    wall_w = [0] * CELL_COUNT
    wall_w[1] = brick
    grid.add_level(
        ChunkLevel(
            0,
            0,
            0,
            (0,) * CELL_COUNT,
            (grass,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            tuple(wall_w),
            tuple(flags),
            (0,) * CELL_COUNT,
        )
    )
    assert grid.wall_between(0, 0, 1, 0, 0)
    assert not grid.can_step(0, 0, 1, 0, 0)


def test_chunk_border_wall_blocks_from_both_sides() -> None:
    registry = _registry()
    grass = registry["grass"].id
    brick = registry["brick"].id
    grid = WorldGrid([Chunk.flat(0, 0, 0, grass), Chunk.flat(1, 0, 0, grass)], registry=registry)
    flags = [0] * CELL_COUNT
    # The east edge of (31, 0) is the west edge of the neighbouring chunk's (0, 0).
    grid.add_level(
        ChunkLevel(
            1,
            0,
            0,
            (0,) * CELL_COUNT,
            (grass,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            (brick,) + (0,) * (CELL_COUNT - 1),
            tuple(flags),
            (0,) * CELL_COUNT,
        )
    )
    assert grid.wall_between(31, 0, 32, 0, 0)
    assert grid.wall_between(32, 0, 31, 0, 0)


def test_ground_floor_wall_does_not_block_the_floor_above() -> None:
    registry = _registry()
    grass = registry["grass"].id
    brick = registry["brick"].id
    grid = WorldGrid([Chunk.flat(0, 0, 12, grass)], registry=registry)
    wall_w = [0] * CELL_COUNT
    wall_w[1] = brick
    grid.add_level(
        ChunkLevel(
            0,
            0,
            1,
            (12,) * CELL_COUNT,
            (grass,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            tuple(wall_w),
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
        )
    )
    assert grid.wall_between(0, 0, 1, 0, 12)
    assert not grid.wall_between(0, 0, 1, 0, 18)


def test_diagonal_never_cuts_a_wall_corner() -> None:
    registry = _registry()
    grass = registry["grass"].id
    brick = registry["brick"].id
    grid = WorldGrid([Chunk.flat(0, 0, 0, grass)], registry=registry)
    corner = 1 * 32 + 1  # tile (1, 1)
    assert grid.can_step(0, 0, 1, 1, 0)
    # North and west walls on the corner tile close both orthogonal detours.
    walls = [0] * CELL_COUNT
    walls[corner] = brick
    grid.add_level(
        ChunkLevel(
            0,
            0,
            0,
            (0,) * CELL_COUNT,
            (grass,) * CELL_COUNT,
            tuple(walls),
            tuple(walls),
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
        )
    )
    assert grid.wall_between(1, 0, 1, 1, 0)
    assert grid.wall_between(0, 1, 1, 1, 0)
    assert not grid.can_step(0, 0, 1, 1, 0)


def test_two_metre_headroom_allows_touching_ceiling_only() -> None:
    registry = _registry()
    grass = registry["grass"].id
    for ceiling_h, allows_ground in ((10, True), (9, False)):
        grid = WorldGrid([Chunk.flat(0, 0, 6, grass)], registry=registry)
        floor_h = [NO_FLOOR] * CELL_COUNT
        floor_mat = [0] * CELL_COUNT
        floor_h[0] = ceiling_h
        floor_mat[0] = grass
        grid.add_level(
            ChunkLevel(
                0,
                0,
                ceiling_h // 6,
                tuple(floor_h),
                tuple(floor_mat),
                (0,) * CELL_COUNT,
                (0,) * CELL_COUNT,
                (0,) * CELL_COUNT,
                (0,) * CELL_COUNT,
            )
        )
        assert (6 in {surface.h for surface in grid.standing_surfaces(0, 0)}) is allows_ground


def test_standing_surface_band_is_derived_from_height() -> None:
    registry = _registry()
    grass = registry["grass"].id
    grid = WorldGrid([Chunk.flat(0, 0, 0, grass)], registry=registry)
    floor_h = [NO_FLOOR] * CELL_COUNT
    floor_mat = [0] * CELL_COUNT
    floor_h[0] = 17
    floor_mat[0] = grass
    grid.add_level(
        ChunkLevel(
            0,
            0,
            3,
            tuple(floor_h),
            tuple(floor_mat),
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
        )
    )
    surface = next(surface for surface in grid.standing_surfaces(0, 0) if surface.h == 17)
    assert surface.z == 2


def test_levels_are_indexed_by_their_chunk() -> None:
    registry = _registry()
    grass = registry["grass"].id
    grid = WorldGrid([Chunk.flat(-1, 0, 0, grass), Chunk.flat(0, 0, 0, grass)], registry=registry)
    for cx, local_x, height in ((-1, 31, 14), (0, 0, 17)):
        floor_h = [NO_FLOOR] * CELL_COUNT
        floor_mat = [0] * CELL_COUNT
        index = local_x
        floor_h[index] = height
        floor_mat[index] = grass
        grid.add_level(
            ChunkLevel(
                cx,
                0,
                2,
                tuple(floor_h),
                tuple(floor_mat),
                (0,) * CELL_COUNT,
                (0,) * CELL_COUNT,
                (0,) * CELL_COUNT,
                (0,) * CELL_COUNT,
            )
        )
    assert {surface.h for surface in grid.standing_surfaces(-1, 0)} == {0, 14}
    assert {surface.h for surface in grid.standing_surfaces(0, 0)} == {0, 17}


def test_chunk_loader_populates_cache_only_on_first_lookup() -> None:
    registry = _registry()
    chunk = Chunk.flat(-1, 2, 0, registry["grass"].id)
    level = ChunkLevel.empty(-1, 2, 0)
    calls: list[tuple[int, int]] = []

    def load(cx: int, cy: int) -> tuple[Chunk, tuple[ChunkLevel, ...]] | None:
        calls.append((cx, cy))
        return (chunk, (level,)) if (cx, cy) == (-1, 2) else None

    grid = WorldGrid(registry=registry, chunk_loader=load)
    assert grid.chunk(-1, 2) is chunk
    assert grid.level(-1, 2, 0) is level
    assert grid.chunk(-1, 2) is chunk
    assert grid.chunk(5, 5) is None
    assert grid.chunk(5, 5) is None
    assert calls == [(-1, 2), (5, 5)]


def test_diagonal_uses_consistent_leg_heights_and_limits_total_rise() -> None:
    registry = _registry()
    grass = registry["grass"].id
    heights = [0] * CELL_COUNT
    heights[1] = 1
    heights[32] = 1
    heights[33] = 1
    grid = WorldGrid([Chunk(0, 0, tuple(heights), (grass,) * CELL_COUNT)], registry=registry)
    assert grid.can_step(0, 0, 1, 1, 0)

    heights[33] = 2
    steep_grid = WorldGrid([Chunk(0, 0, tuple(heights), (grass,) * CELL_COUNT)], registry=registry)
    assert not steep_grid.can_step(0, 0, 1, 1, 0)
    assert not steep_grid.can_step(0, 0, 1, 1)


def test_floorless_wall_uses_supporting_floor_height() -> None:
    registry = _registry()
    grass = registry["grass"].id
    brick = registry["brick"].id
    grid = WorldGrid([Chunk.flat(0, 0, 14, grass)], registry=registry)
    wall_w = [0] * CELL_COUNT
    wall_w[1] = brick
    empty = ChunkLevel.empty(0, 0, 2)
    grid.add_level(replace(empty, wall_w=tuple(wall_w)))
    assert not grid.wall_between(0, 0, 1, 0, 10)
    assert grid.wall_between(0, 0, 1, 0, 11)


def test_climbable_column_is_an_explicit_navigation_edge() -> None:
    registry = _registry()
    grass = registry["grass"].id
    grid = WorldGrid([Chunk.flat(0, 0, 0, grass)], registry=registry)
    floor_h = [NO_FLOOR] * CELL_COUNT
    floor_mat = [0] * CELL_COUNT
    flags = [0] * CELL_COUNT
    index = 1 * 32 + 1
    floor_h[index] = 12
    floor_mat[index] = grass
    level = ChunkLevel(
        0,
        0,
        2,
        tuple(floor_h),
        tuple(floor_mat),
        (0,) * CELL_COUNT,
        (0,) * CELL_COUNT,
        (0,) * CELL_COUNT,
        tuple(flags),
    )
    grid.add_level(level)
    assert not grid.can_step(1, 1, 1, 1, 0)

    flags[index] = LEVEL_CLIMBABLE
    grid.add_level(replace(level, flags=tuple(flags)))
    assert grid.can_step(1, 1, 1, 1, 0)
    assert find_path(grid, (1, 1, 0), (1, 1, 12)) == [(1, 1, 0), (1, 1, 12)]


def test_nav_does_not_cross_a_wall_barrier() -> None:
    registry = _registry()
    grass = registry["grass"].id
    brick = registry["brick"].id
    grid = WorldGrid([Chunk.flat(0, 0, 0, grass)], registry=registry)
    wall_w = [0] * CELL_COUNT
    for y in range(32):
        wall_w[y * 32 + 16] = brick
    empty = ChunkLevel.empty(0, 0, 0)
    grid.add_level(replace(empty, wall_w=tuple(wall_w)))
    assert find_path(grid, (8, 16, 0), (24, 16, 0)) is None


def test_nav_finds_stairs_path_and_refuses_unreachable() -> None:
    registry = _registry()
    world = generate_test_world(123, registry)
    grid = WorldGrid(world.chunks.values(), world.levels.values(), registry)
    start = (121, 128, 2)
    goal = (140, 140, 18)
    path = find_path(grid, start, goal)
    assert path is not None
    assert path[0] == start
    assert path[-1] == goal
    climbs = any(path[i + 1][2] - path[i][2] == 1 for i in range(len(path) - 1))
    assert climbs
    for i in range(len(path) - 1):
        a, b = path[i], path[i + 1]
        assert grid.can_step(a[0], a[1], b[0], b[1], a[2])
    # The pond centre has no standing surface: no path can reach it.
    assert find_path(grid, start, (196, 70, 0)) is None


def test_menu_target_names_material_and_elevation(tmp_path: Path) -> None:
    database = tmp_path / "menu.db"
    settings = Settings(
        database_url=f"sqlite:///{database.as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=10,
    )
    with TestClient(create_app(settings)) as client:
        payload = client.get("/api/menu", params={"x": 121.5, "y": 128.5, "z": 0}).json()
        assert payload["target"].startswith("Asphalt")
        assert "1 m" in payload["target"]
        # Niko stands on this road tile: asphalt is never dug, and waiting targets himself.
        assert [(entry["op"], entry["available"]) for entry in payload["ops"]] == [
            ("wait", True),
            ("inspect", True),
        ]


def test_crossing_a_chunk_boundary_pushes_only_new_chunks(tmp_path: Path) -> None:
    database = tmp_path / "push.db"
    settings = Settings(
        database_url=f"sqlite:///{database.as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=10,
    )
    with TestClient(create_app(settings)) as client:
        with client.websocket_connect("/ws") as websocket:
            assert websocket.receive_json()["type"] == "snapshot"
            first_chunks = [websocket.receive_json()["type"] for _ in range(25)]
            assert set(first_chunks) == {"chunk"}

            def walk(sequence: int, dx: float, dy: float) -> int:
                """Send one input, drain any chunk messages, return 1 when chunks appeared."""
                websocket.send_json({"type": "input", "sequence": sequence, "dx": dx, "dy": dy})
                message = websocket.receive_json()
                pushed = 0
                while message["type"] == "chunk":
                    pushed += 1
                    message = websocket.receive_json()
                assert message["type"] == "ack"
                assert message["sequence"] == sequence
                return pushed

            pushed = 0
            for step in range(1, 41):
                pushed += walk(step, 1, 0)
            # Walking east 8 m from the road crosses exactly one chunk boundary.
            assert pushed == 5
            pushed += walk(100, 0, 0)
            assert pushed == 5  # the stationary input pushes nothing new


def test_migration_from_phase_zero_keeps_the_actor(tmp_path: Path) -> None:
    database = tmp_path / "upgrade.db"
    root = Path(__file__).parents[1]
    config = Config(str(root / "alembic.ini"))
    config.set_main_option("script_location", str(root / "alembic"))
    config.set_main_option("sqlalchemy.url", f"sqlite:///{database.as_posix()}")

    command.upgrade(config, "0001_initial")
    engine = create_engine(f"sqlite:///{database.as_posix()}")
    with engine.begin() as connection:
        connection.execute(
            text(
                "INSERT INTO world_meta (id, seed, game_minute, speed, paused) VALUES (1, 7, 90, 3, 0)"
            )
        )
        connection.execute(
            text("INSERT INTO actor (id, kind, x, y, z) VALUES ('niko', 'player', 2.0, 2.0, 0)")
        )
    engine.dispose()

    command.upgrade(config, "head")
    engine = create_engine(f"sqlite:///{database.as_posix()}")
    tables = inspect(engine)
    assert {"material", "chunk", "chunk_level"}.issubset(tables.get_table_names())
    with engine.connect() as connection:
        row = connection.execute(
            text("SELECT x, y, h FROM actor WHERE id = :id"), {"id": PLAYER_ID}
        ).one()
        gen_version = connection.execute(text("SELECT gen_version FROM world_meta")).scalar_one()
    assert row == (2.0, 2.0, 0)
    assert gen_version == 0
    engine.dispose()
