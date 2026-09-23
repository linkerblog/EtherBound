"""Add per-tile dug depth and actor activities."""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "0004_dig_activity"
down_revision: str | Sequence[str] | None = "0003_event"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.add_column("chunk", sa.Column("dug", sa.LargeBinary(), nullable=True))
    op.add_column("actor", sa.Column("activity", sa.JSON(), nullable=True))


def downgrade() -> None:
    with op.batch_alter_table("actor") as batch:
        batch.drop_column("activity")
    with op.batch_alter_table("chunk") as batch:
        batch.drop_column("dug")
