from typing import Any

from sqlalchemy import (
    JSON,
    BigInteger,
    Boolean,
    CheckConstraint,
    Float,
    ForeignKey,
    Index,
    Integer,
    LargeBinary,
    String,
)
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
    mass_kg: Mapped[float] = mapped_column(Float, nullable=False, default=80.0, server_default="80")
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


class WallIntegrity(Base):
    __tablename__ = "wall_integrity"

    cx: Mapped[int] = mapped_column(Integer, primary_key=True)
    cy: Mapped[int] = mapped_column(Integer, primary_key=True)
    z: Mapped[int] = mapped_column(Integer, primary_key=True)
    cell_index: Mapped[int] = mapped_column(Integer, primary_key=True)
    edge: Mapped[str] = mapped_column(String(5), primary_key=True)
    integrity: Mapped[float] = mapped_column(Float, nullable=False)

    __table_args__ = (
        CheckConstraint("cell_index BETWEEN 0 AND 1023", name="ck_wall_integrity_cell"),
        CheckConstraint("edge IN ('north', 'west')", name="ck_wall_integrity_edge"),
        CheckConstraint("integrity > 0", name="ck_wall_integrity_positive"),
    )


class Event(Base):
    __tablename__ = "event"

    seq: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=False)
    game_minute: Mapped[int] = mapped_column(Integer, nullable=False, index=True)
    type: Mapped[str] = mapped_column(String(64), nullable=False, index=True)
    actor_id: Mapped[str | None] = mapped_column(String(64), nullable=True, index=True)
    data: Mapped[dict[str, Any]] = mapped_column(JSON, nullable=False)


class Object(Base):
    """One instance of an object kind, at exactly one location (docs/Dev-012.md [Sec. 3])."""

    __tablename__ = "object"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    kind: Mapped[str] = mapped_column(String(64), nullable=False)
    loc: Mapped[str] = mapped_column(String(8), nullable=False)
    x: Mapped[int | None] = mapped_column(Integer, nullable=True)
    y: Mapped[int | None] = mapped_column(Integer, nullable=True)
    h: Mapped[int | None] = mapped_column(Integer, nullable=True)
    cx: Mapped[int | None] = mapped_column(Integer, nullable=True)
    cy: Mapped[int | None] = mapped_column(Integer, nullable=True)
    container_id: Mapped[int | None] = mapped_column(
        ForeignKey("object.id"), nullable=True, index=True
    )
    actor_id: Mapped[str | None] = mapped_column(ForeignKey("actor.id"), nullable=True, index=True)
    slot: Mapped[str | None] = mapped_column(String(8), nullable=True)
    quantity: Mapped[int] = mapped_column(Integer, nullable=False, default=1)
    state: Mapped[dict[str, Any]] = mapped_column(JSON, nullable=False, default=dict)
    integrity: Mapped[float | None] = mapped_column(Float, nullable=True)
    owner: Mapped[str | None] = mapped_column(String(64), nullable=True)

    __table_args__ = (
        Index("ix_object_cell", "cx", "cy"),
        CheckConstraint("quantity > 0", name="ck_object_quantity"),
        CheckConstraint(
            "(loc = 'tile' AND x IS NOT NULL AND y IS NOT NULL AND h IS NOT NULL"
            " AND cx IS NOT NULL AND cy IS NOT NULL AND container_id IS NULL"
            " AND actor_id IS NULL AND slot IS NULL)"
            " OR (loc = 'in' AND x IS NULL AND y IS NULL AND h IS NULL"
            " AND cx IS NULL AND cy IS NULL AND container_id IS NOT NULL"
            " AND actor_id IS NULL AND slot IS NULL)"
            " OR (loc = 'held' AND x IS NULL AND y IS NULL AND h IS NULL"
            " AND cx IS NULL AND cy IS NULL AND container_id IS NULL"
            " AND actor_id IS NOT NULL AND slot IN ('left', 'right', 'both'))"
            " OR (loc = 'worn' AND x IS NULL AND y IS NULL AND h IS NULL"
            " AND cx IS NULL AND cy IS NULL AND container_id IS NULL"
            " AND actor_id IS NOT NULL AND slot = 'back')",
            name="ck_object_location",
        ),
    )
