"""Add actor mass and persistent wall damage."""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "0006_physics"
down_revision: str | Sequence[str] | None = "0005_object"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.add_column(
        "actor",
        sa.Column("mass_kg", sa.Float(), server_default="80", nullable=False),
    )
    op.create_table(
        "wall_integrity",
        sa.Column("cx", sa.Integer(), primary_key=True),
        sa.Column("cy", sa.Integer(), primary_key=True),
        sa.Column("z", sa.Integer(), primary_key=True),
        sa.Column("cell_index", sa.Integer(), primary_key=True),
        sa.Column("edge", sa.String(length=5), primary_key=True),
        sa.Column("integrity", sa.Float(), nullable=False),
        sa.CheckConstraint("cell_index BETWEEN 0 AND 1023", name="ck_wall_integrity_cell"),
        sa.CheckConstraint("edge IN ('north', 'west')", name="ck_wall_integrity_edge"),
        sa.CheckConstraint("integrity > 0", name="ck_wall_integrity_positive"),
    )


def downgrade() -> None:
    op.drop_table("wall_integrity")
    op.drop_column("actor", "mass_kg")
