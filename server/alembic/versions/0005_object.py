"""Create the object table."""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "0005_object"
down_revision: str | Sequence[str] | None = "0004_dig_activity"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "object",
        sa.Column("id", sa.Integer(), primary_key=True, autoincrement=True),
        sa.Column("kind", sa.String(length=64), nullable=False),
        sa.Column("loc", sa.String(length=8), nullable=False),
        sa.Column("x", sa.Integer(), nullable=True),
        sa.Column("y", sa.Integer(), nullable=True),
        sa.Column("h", sa.Integer(), nullable=True),
        sa.Column("cx", sa.Integer(), nullable=True),
        sa.Column("cy", sa.Integer(), nullable=True),
        sa.Column("container_id", sa.Integer(), sa.ForeignKey("object.id"), nullable=True),
        sa.Column("actor_id", sa.String(length=64), sa.ForeignKey("actor.id"), nullable=True),
        sa.Column("slot", sa.String(length=8), nullable=True),
        sa.Column("quantity", sa.Integer(), nullable=False, server_default="1"),
        sa.Column("state", sa.JSON(), nullable=False),
        sa.Column("integrity", sa.Float(), nullable=True),
        sa.Column("owner", sa.String(length=64), nullable=True),
        sa.CheckConstraint("quantity > 0", name="ck_object_quantity"),
        sa.CheckConstraint(
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
    op.create_index("ix_object_cell", "object", ["cx", "cy"])
    op.create_index("ix_object_container_id", "object", ["container_id"])
    op.create_index("ix_object_actor_id", "object", ["actor_id"])


def downgrade() -> None:
    op.drop_index("ix_object_actor_id", table_name="object")
    op.drop_index("ix_object_container_id", table_name="object")
    op.drop_index("ix_object_cell", table_name="object")
    op.drop_table("object")
