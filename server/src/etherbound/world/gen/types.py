import json
from collections.abc import Mapping
from dataclasses import dataclass

from etherbound.world.chunk import Chunk, ChunkLevel


@dataclass(frozen=True, slots=True)
class GeneratedObject:
    """One object a generator places. ``parent`` is the index of its container, if any."""

    kind: str
    x: int
    y: int
    h: int
    quantity: int = 1
    open: bool = False
    parent: int | None = None


@dataclass(frozen=True, slots=True)
class GeneratorBay:
    """A named rectangle of a generator's map, for the debug bay list."""

    key: str
    x: int
    y: int
    width: int
    height: int


@dataclass(frozen=True, slots=True)
class GeneratedWorld:
    """The complete output of one generator run: everything ``_commit_world`` persists."""

    chunks: Mapping[tuple[int, int], Chunk]
    levels: Mapping[tuple[int, int, int], ChunkLevel]
    spawn: tuple[float, float, int]
    gen_version: int
    objects: tuple[GeneratedObject, ...] = ()
    generator: str = "test"

    def blob_bytes(self) -> bytes:
        chunks = b"".join(
            self.chunks[key].ground_blob
            + self.chunks[key].surface_blob
            + self.chunks[key].dug_blob
            + json.dumps(self.chunks[key].strata, separators=(",", ":")).encode("ascii")
            for key in sorted(self.chunks)
        )
        levels = b"".join(
            self.levels[key].floor_blob
            + self.levels[key].floor_mat_blob
            + self.levels[key].wall_n_blob
            + self.levels[key].wall_w_blob
            + self.levels[key].edge_flags_blob
            + self.levels[key].flags_blob
            for key in sorted(self.levels)
        )
        return chunks + levels
