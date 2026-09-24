from pathlib import Path

from alembic import command
from alembic.config import Config
from sqlalchemy import create_engine, inspect, text
from sqlalchemy.orm import Session, sessionmaker

from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.world.chunk import CELL_COUNT, Chunk


def test_upgrade_head_creates_initial_schema(tmp_path: Path) -> None:
    database = tmp_path / "migration.db"
    config = Config(str(Path(__file__).parents[1] / "alembic.ini"))
    config.set_main_option("script_location", str(Path(__file__).parents[1] / "alembic"))
    config.set_main_option("sqlalchemy.url", f"sqlite:///{database.as_posix()}")

    command.upgrade(config, "head")

    tables = inspect(create_engine(f"sqlite:///{database.as_posix()}"))
    assert {"world_meta", "actor", "event", "wall_integrity", "alembic_version"}.issubset(
        tables.get_table_names()
    )


def test_upgrade_existing_world_keeps_rows(tmp_path: Path) -> None:
    database = tmp_path / "existing.db"
    config = Config(str(Path(__file__).parents[1] / "alembic.ini"))
    config.set_main_option("script_location", str(Path(__file__).parents[1] / "alembic"))
    config.set_main_option("sqlalchemy.url", f"sqlite:///{database.as_posix()}")
    command.upgrade(config, "0002_world")
    engine = create_engine(f"sqlite:///{database.as_posix()}")
    with engine.begin() as connection:
        connection.execute(
            text(
                "INSERT INTO world_meta (id, seed, game_minute, speed, paused, gen_version) "
                "VALUES (1, 42, 9, 3, 0, 2)"
            )
        )
        connection.execute(
            text("INSERT INTO actor (id, kind, x, y, z, h) VALUES ('niko', 'player', 8, 9, 1, 6)")
        )

    command.upgrade(config, "head")

    with engine.connect() as connection:
        assert connection.execute(text("SELECT seed, game_minute FROM world_meta")).one() == (42, 9)
        assert connection.execute(text("SELECT id, x, y FROM actor")).one() == ("niko", 8, 9)
        assert connection.execute(text("SELECT mass_kg FROM actor")).scalar_one() == 80
        assert connection.execute(text("SELECT count(*) FROM event")).scalar_one() == 0


def test_upgrade_0004_keeps_rows_and_reads_null_dug_as_zeros(tmp_path: Path) -> None:
    database = tmp_path / "dug.db"
    config = Config(str(Path(__file__).parents[1] / "alembic.ini"))
    config.set_main_option("script_location", str(Path(__file__).parents[1] / "alembic"))
    config.set_main_option("sqlalchemy.url", f"sqlite:///{database.as_posix()}")
    command.upgrade(config, "0003_event")
    chunk = Chunk.flat(0, 0, 2, 1, strata=((0, "topsoil"),))
    engine = create_engine(f"sqlite:///{database.as_posix()}")
    with engine.begin() as connection:
        connection.execute(
            text(
                "INSERT INTO chunk (cx, cy, ground_h, surface_mat, strata, revision, gen_version) "
                "VALUES (0, 0, :ground, :surface, '[[0, \"topsoil\"]]', 3, 2)"
            ),
            {"ground": chunk.ground_blob, "surface": chunk.surface_blob},
        )
        connection.execute(
            text("INSERT INTO actor (id, kind, x, y, z, h) VALUES ('niko', 'player', 1, 1, 0, 2)")
        )

    command.upgrade(config, "head")

    with engine.connect() as connection:
        ground, surface, revision, dug = connection.execute(
            text("SELECT ground_h, surface_mat, revision, dug FROM chunk")
        ).one()
        assert (revision, dug) == (3, None)
        assert connection.execute(text("SELECT activity FROM actor")).scalar_one() is None
    loaded = Chunk.from_blobs(0, 0, ground, surface, dug_blob=dug)
    assert loaded.dug == (0,) * CELL_COUNT
    assert loaded.ground_h == chunk.ground_h


def test_upgrade_0004_keeps_the_actor_and_fills_v5_objects(tmp_path: Path) -> None:
    database = tmp_path / "objects.db"
    config = Config(str(Path(__file__).parents[1] / "alembic.ini"))
    config.set_main_option("script_location", str(Path(__file__).parents[1] / "alembic"))
    config.set_main_option("sqlalchemy.url", f"sqlite:///{database.as_posix()}")
    command.upgrade(config, "0004_dig_activity")
    url = f"sqlite:///{database.as_posix()}"
    engine = create_engine(url)
    with engine.begin() as connection:
        connection.execute(
            text("INSERT INTO actor (id, kind, x, y, z, h) VALUES ('niko', 'player', 1, 1, 0, 2)")
        )

    command.upgrade(config, "head")

    with engine.connect() as connection:
        assert connection.execute(text("SELECT id FROM actor")).one() == ("niko",)
        assert connection.execute(text("SELECT count(*) FROM object")).scalar_one() == 0

    sessions = sessionmaker(bind=engine, expire_on_commit=False, class_=Session)
    world = WorldEngine(sessions)
    world.ensure_world(0)
    with sessions() as session:
        kinds = {row[0] for row in session.execute(text("SELECT DISTINCT kind FROM object"))}
    assert kinds == {
        "shovel",
        "backpack",
        "chest",
        "apple",
        "bottle",
        "table",
        "chair",
        "shelf",
        "barrel",
        "sledgehammer",
    }


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
