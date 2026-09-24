"""Record which generator made a save and the options it was generated with."""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "0007_generator"
down_revision: str | Sequence[str] | None = "0006_physics"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.add_column(
        "world_meta",
        sa.Column("generator", sa.String(length=32), nullable=False, server_default="test"),
    )
    op.add_column(
        "world_meta",
        sa.Column("gen_options", sa.JSON(), nullable=False, server_default=sa.text("'{}'")),
    )


def downgrade() -> None:
    op.drop_column("world_meta", "gen_options")
    op.drop_column("world_meta", "generator")
