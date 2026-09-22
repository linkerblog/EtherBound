from functools import lru_cache
from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="ETHERBOUND_", env_file=".env", extra="ignore")

    host: str = "127.0.0.1"
    port: int = Field(default=8000, ge=1, le=65535)
    database_url: str = "sqlite:///../data/etherbound.db"
    time_scale: float = Field(default=1.0, gt=0)
    movement_hz: float = Field(default=20.0, gt=0)
    schema_path: Path = Path("schema.json")

    def resolved_database_url(self) -> str:
        if not self.database_url.startswith("sqlite:///"):
            return self.database_url
        database_path = Path(self.database_url.removeprefix("sqlite:///"))
        if database_path.is_absolute():
            return self.database_url
        project_server = Path(__file__).resolve().parents[2]
        return f"sqlite:///{(project_server / database_path).resolve().as_posix()}"


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    return Settings()
