"""Give actors a name and a persisted mind so Extras keep a goal across restarts."""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "0008_extra"
down_revision: str | Sequence[str] | None = "0007_generator"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.add_column("actor", sa.Column("name", sa.String(length=64), nullable=True))
    op.add_column("actor", sa.Column("mind", sa.JSON(), nullable=True))


def downgrade() -> None:
    op.drop_column("actor", "mind")
    op.drop_column("actor", "name")
