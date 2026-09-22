from pathlib import Path

from alembic import command
from alembic.config import Config
from fastapi.testclient import TestClient
from sqlalchemy import create_engine, inspect, text

from etherbound.app import create_app
from etherbound.config import Settings
from etherbound.engine.world import PLAYER_ID
from etherbound.world.chunk import CELL_COUNT, EDGE_W_WINDOW, Chunk, ChunkLevel
from etherbound.world.gen.testworld import generate_test_world
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry
from etherbound.world.nav import find_path


def _registry() -> MaterialRegistry:
    return MaterialRegistry.load()


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
        assert payload["verbs"] == ["inspect"]


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
