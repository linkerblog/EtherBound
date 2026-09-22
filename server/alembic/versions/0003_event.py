"""Add the persistent world event log."""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "0003_event"
down_revision: str | Sequence[str] | None = "0002_world"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "event",
        sa.Column("seq", sa.Integer(), autoincrement=False, nullable=False),
        sa.Column("game_minute", sa.Integer(), nullable=False),
        sa.Column("type", sa.String(length=64), nullable=False),
        sa.Column("actor_id", sa.String(length=64), nullable=True),
        sa.Column("data", sa.JSON(), nullable=False),
        sa.PrimaryKeyConstraint("seq"),
    )
    op.create_index("ix_event_game_minute", "event", ["game_minute"], unique=False)
    op.create_index("ix_event_type", "event", ["type"], unique=False)
    op.create_index("ix_event_actor_id", "event", ["actor_id"], unique=False)


def downgrade() -> None:
    op.drop_index("ix_event_actor_id", table_name="event")
    op.drop_index("ix_event_type", table_name="event")
    op.drop_index("ix_event_game_minute", table_name="event")
    op.drop_table("event")
