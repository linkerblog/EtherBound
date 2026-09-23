from typing import Any

from sqlalchemy import JSON, BigInteger, Boolean, Float, Integer, LargeBinary, String
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class WorldMeta(Base):
    __tablename__ = "world_meta"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    seed: Mapped[int] = mapped_column(BigInteger, nullable=False)
    game_minute: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    speed: Mapped[int] = mapped_column(Integer, nullable=False, default=1)
    paused: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False)
    gen_version: Mapped[int] = mapped_column(Integer, nullable=False, default=0)


class Actor(Base):
    __tablename__ = "actor"

    id: Mapped[str] = mapped_column(String(64), primary_key=True)
    kind: Mapped[str] = mapped_column(String(32), nullable=False)
    x: Mapped[float] = mapped_column(Float, nullable=False)
    y: Mapped[float] = mapped_column(Float, nullable=False)
    z: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    h: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    activity: Mapped[dict[str, Any] | None] = mapped_column(JSON, nullable=True)


class Material(Base):
    __tablename__ = "material"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    key: Mapped[str] = mapped_column(String(64), nullable=False, unique=True)
    name: Mapped[str] = mapped_column(String(128), nullable=False)
    color: Mapped[str] = mapped_column(String(16), nullable=False)
    walkable: Mapped[bool] = mapped_column(Boolean, nullable=False)
    walk_cost: Mapped[float] = mapped_column(Float, nullable=False)
    solid: Mapped[bool] = mapped_column(Boolean, nullable=False)
    blocks_sight: Mapped[bool] = mapped_column(Boolean, nullable=False)
    diggable: Mapped[bool] = mapped_column(Boolean, nullable=False)
    dig_cost: Mapped[float] = mapped_column(Float, nullable=False)
    flammable: Mapped[bool] = mapped_column(Boolean, nullable=False)
    density: Mapped[float] = mapped_column(Float, nullable=False)
    resistance: Mapped[float] = mapped_column(Float, nullable=False)
    liquid: Mapped[bool] = mapped_column(Boolean, nullable=False)
    tags: Mapped[list[str]] = mapped_column(JSON, nullable=False)


class Chunk(Base):
    __tablename__ = "chunk"

    cx: Mapped[int] = mapped_column(Integer, primary_key=True)
    cy: Mapped[int] = mapped_column(Integer, primary_key=True)
    ground_h: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    surface_mat: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    strata: Mapped[list[list[int | str]]] = mapped_column(JSON, nullable=False)
    revision: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    gen_version: Mapped[int] = mapped_column(Integer, nullable=False)
    # NULL means nothing was ever dug in this chunk.
    dug: Mapped[bytes | None] = mapped_column(LargeBinary, nullable=True)


class ChunkLevel(Base):
    __tablename__ = "chunk_level"

    cx: Mapped[int] = mapped_column(Integer, primary_key=True)
    cy: Mapped[int] = mapped_column(Integer, primary_key=True)
    z: Mapped[int] = mapped_column(Integer, primary_key=True)
    floor_h: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    floor_mat: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    wall_n: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    wall_w: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    edge_flags: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    flags: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)


class Event(Base):
    __tablename__ = "event"

    seq: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=False)
    game_minute: Mapped[int] = mapped_column(Integer, nullable=False, index=True)
    type: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    actor_id: Mapped[str | None] = mapped_column(String(64), nullable=True, index=True)
    data: Mapped[dict[str, Any]] = mapped_column(JSON, nullable=False)
