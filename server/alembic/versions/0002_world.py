"""Add materials and sparse chunked world persistence."""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "0002_world"
down_revision: str | Sequence[str] | None = "0001_initial"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.add_column(
        "world_meta", sa.Column("gen_version", sa.Integer(), nullable=False, server_default="0")
    )
    op.add_column("actor", sa.Column("h", sa.Integer(), nullable=False, server_default="0"))
    op.create_table(
        "material",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("key", sa.String(length=64), nullable=False),
        sa.Column("name", sa.String(length=128), nullable=False),
        sa.Column("color", sa.String(length=16), nullable=False),
        sa.Column("walkable", sa.Boolean(), nullable=False),
        sa.Column("walk_cost", sa.Float(), nullable=False),
        sa.Column("solid", sa.Boolean(), nullable=False),
        sa.Column("blocks_sight", sa.Boolean(), nullable=False),
        sa.Column("diggable", sa.Boolean(), nullable=False),
        sa.Column("dig_cost", sa.Float(), nullable=False),
        sa.Column("flammable", sa.Boolean(), nullable=False),
        sa.Column("density", sa.Float(), nullable=False),
        sa.Column("resistance", sa.Float(), nullable=False),
        sa.Column("liquid", sa.Boolean(), nullable=False),
        sa.Column("tags", sa.JSON(), nullable=False),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("key"),
    )
    op.create_table(
        "chunk",
        sa.Column("cx", sa.Integer(), nullable=False),
        sa.Column("cy", sa.Integer(), nullable=False),
        sa.Column("ground_h", sa.LargeBinary(), nullable=False),
        sa.Column("surface_mat", sa.LargeBinary(), nullable=False),
        sa.Column("strata", sa.JSON(), nullable=False),
        sa.Column("revision", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("gen_version", sa.Integer(), nullable=False),
        sa.PrimaryKeyConstraint("cx", "cy"),
    )
    op.create_table(
        "chunk_level",
        sa.Column("cx", sa.Integer(), nullable=False),
        sa.Column("cy", sa.Integer(), nullable=False),
        sa.Column("z", sa.Integer(), nullable=False),
        sa.Column("floor_h", sa.LargeBinary(), nullable=False),
        sa.Column("floor_mat", sa.LargeBinary(), nullable=False),
        sa.Column("wall_n", sa.LargeBinary(), nullable=False),
        sa.Column("wall_w", sa.LargeBinary(), nullable=False),
        sa.Column("edge_flags", sa.LargeBinary(), nullable=False),
        sa.Column("flags", sa.LargeBinary(), nullable=False),
        sa.PrimaryKeyConstraint("cx", "cy", "z"),
    )


def downgrade() -> None:
    op.drop_table("chunk_level")
    op.drop_table("chunk")
    op.drop_table("material")
    with op.batch_alter_table("actor") as batch:
        batch.drop_column("h")
    with op.batch_alter_table("world_meta") as batch:
        batch.drop_column("gen_version")
