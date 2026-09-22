from pathlib import Path

from alembic import command
from alembic.config import Config
from sqlalchemy import create_engine, inspect


def test_upgrade_head_creates_initial_schema(tmp_path: Path) -> None:
    database = tmp_path / "migration.db"
    config = Config(str(Path(__file__).parents[1] / "alembic.ini"))
    config.set_main_option("script_location", str(Path(__file__).parents[1] / "alembic"))
    config.set_main_option("sqlalchemy.url", f"sqlite:///{database.as_posix()}")

    command.upgrade(config, "head")

    tables = inspect(create_engine(f"sqlite:///{database.as_posix()}"))
    assert {"world_meta", "actor", "alembic_version"}.issubset(tables.get_table_names())
