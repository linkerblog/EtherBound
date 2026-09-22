from pathlib import Path

from alembic import command
from alembic.config import Config


def upgrade(database_url: str, alembic_ini: Path | None = None) -> None:
    project_root = Path(__file__).resolve().parents[3]
    ini_path = alembic_ini or project_root / "alembic.ini"
    config = Config(str(ini_path))
    config.set_main_option("script_location", str(project_root / "alembic"))
    config.set_main_option("sqlalchemy.url", database_url)
    command.upgrade(config, "head")
