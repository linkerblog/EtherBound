from pathlib import Path

from alembic import command
from alembic.config import Config
from sqlalchemy import create_engine, inspect, text


def test_upgrade_head_creates_initial_schema(tmp_path: Path) -> None:
    database = tmp_path / "migration.db"
    config = Config(str(Path(__file__).parents[1] / "alembic.ini"))
    config.set_main_option("script_location", str(Path(__file__).parents[1] / "alembic"))
    config.set_main_option("sqlalchemy.url", f"sqlite:///{database.as_posix()}")

    command.upgrade(config, "head")

    tables = inspect(create_engine(f"sqlite:///{database.as_posix()}"))
    assert {"world_meta", "actor", "event", "alembic_version"}.issubset(tables.get_table_names())


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
        assert connection.execute(text("SELECT count(*) FROM event")).scalar_one() == 0
