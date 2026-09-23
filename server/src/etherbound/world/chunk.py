import sys
from array import array
from collections.abc import Iterable, Sequence
from dataclasses import dataclass

CHUNK_SIZE = 32
CELL_COUNT = CHUNK_SIZE * CHUNK_SIZE
NO_FLOOR = -32768

EDGE_N_DOORWAY = 1
EDGE_N_WINDOW = 2
EDGE_W_DOORWAY = 4
EDGE_W_WINDOW = 8
LEVEL_VOID = 1
LEVEL_CLIMBABLE = 2
LEVEL_ROOF = 4


def _encode(values: Iterable[int], typecode: str, size: int) -> bytes:
    result: array[int] = array(typecode, values)
    if len(result) != size:
        raise ValueError(f"expected {size} values, got {len(result)}")
    if sys.byteorder != "little":
        result.byteswap()
    return result.tobytes()


def _decode(blob: bytes, typecode: str, size: int) -> tuple[int, ...]:
    result: array[int] = array(typecode)
    result.frombytes(blob)
    if sys.byteorder != "little":
        result.byteswap()
    if len(result) != size:
        raise ValueError(f"expected {size} values, got {len(result)}")
    return tuple(result)


def encode_int16(values: Iterable[int]) -> bytes:
    return _encode(values, "h", CELL_COUNT)


def decode_int16(blob: bytes) -> tuple[int, ...]:
    return _decode(blob, "h", CELL_COUNT)


def encode_uint16(values: Iterable[int]) -> bytes:
    return _encode(values, "H", CELL_COUNT)


def decode_uint16(blob: bytes) -> tuple[int, ...]:
    return _decode(blob, "H", CELL_COUNT)


def encode_uint8(values: Iterable[int]) -> bytes:
    return _encode(values, "B", CELL_COUNT)


def decode_uint8(blob: bytes) -> tuple[int, ...]:
    return _decode(blob, "B", CELL_COUNT)


def _cells(values: Sequence[int], name: str) -> tuple[int, ...]:
    if len(values) != CELL_COUNT:
        raise ValueError(f"{name} must contain {CELL_COUNT} values")
    return tuple(int(value) for value in values)


@dataclass(frozen=True, slots=True)
class Chunk:
    cx: int
    cy: int
    ground_h: tuple[int, ...]
    surface_mat: tuple[int, ...]
    strata: tuple[tuple[int, str], ...] = ()
    revision: int = 0
    gen_version: int = 1
    # Half-metres dug out of each tile's original ground; strata depth is measured from there.
    dug: tuple[int, ...] = ()

    def __post_init__(self) -> None:
        object.__setattr__(self, "ground_h", _cells(self.ground_h, "ground_h"))
        object.__setattr__(self, "surface_mat", _cells(self.surface_mat, "surface_mat"))
        dug = _cells(self.dug, "dug") if self.dug else (0,) * CELL_COUNT
        if any(not 0 <= value <= 255 for value in dug):
            raise ValueError("dug values must fit in a byte")
        object.__setattr__(self, "dug", dug)
        if any(depth < 0 for depth, _ in self.strata):
            raise ValueError("strata depths must be non-negative")

    @classmethod
    def flat(
        cls,
        cx: int,
        cy: int,
        ground_h: int,
        material_id: int,
        *,
        strata: tuple[tuple[int, str], ...] = (),
    ) -> Chunk:
        return cls(cx, cy, (ground_h,) * CELL_COUNT, (material_id,) * CELL_COUNT, strata)

    def index(self, x: int, y: int) -> int:
        if not 0 <= x < CHUNK_SIZE or not 0 <= y < CHUNK_SIZE:
            raise IndexError("chunk coordinates must be in [0, 32)")
        return y * CHUNK_SIZE + x

    def at(self, x: int, y: int) -> tuple[int, int]:
        index = self.index(x, y)
        return self.ground_h[index], self.surface_mat[index]

    @property
    def ground_blob(self) -> bytes:
        return encode_int16(self.ground_h)

    @property
    def surface_blob(self) -> bytes:
        return encode_uint16(self.surface_mat)

    @property
    def dug_blob(self) -> bytes:
        return encode_uint8(self.dug)

    @classmethod
    def from_blobs(
        cls,
        cx: int,
        cy: int,
        ground_blob: bytes,
        surface_blob: bytes,
        strata: Iterable[tuple[int, str]] = (),
        revision: int = 0,
        gen_version: int = 1,
        dug_blob: bytes | None = None,
    ) -> Chunk:
        return cls(
            cx,
            cy,
            decode_int16(ground_blob),
            decode_uint16(surface_blob),
            tuple(strata),
            revision,
            gen_version,
            decode_uint8(dug_blob) if dug_blob is not None else (),
        )


@dataclass(frozen=True, slots=True)
class ChunkLevel:
    cx: int
    cy: int
    z: int
    floor_h: tuple[int, ...]
    floor_mat: tuple[int, ...]
    wall_n: tuple[int, ...]
    wall_w: tuple[int, ...]
    edge_flags: tuple[int, ...]
    flags: tuple[int, ...]

    def __post_init__(self) -> None:
        for name in ("floor_h", "floor_mat", "wall_n", "wall_w", "edge_flags", "flags"):
            object.__setattr__(self, name, _cells(getattr(self, name), name))

    @classmethod
    def empty(cls, cx: int, cy: int, z: int) -> ChunkLevel:
        return cls(
            cx,
            cy,
            z,
            (NO_FLOOR,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
            (0,) * CELL_COUNT,
        )

    def index(self, x: int, y: int) -> int:
        if not 0 <= x < CHUNK_SIZE or not 0 <= y < CHUNK_SIZE:
            raise IndexError("chunk coordinates must be in [0, 32)")
        return y * CHUNK_SIZE + x

    @property
    def floor_blob(self) -> bytes:
        return encode_int16(self.floor_h)

    @property
    def floor_mat_blob(self) -> bytes:
        return encode_uint16(self.floor_mat)

    @property
    def wall_n_blob(self) -> bytes:
        return encode_uint16(self.wall_n)

    @property
    def wall_w_blob(self) -> bytes:
        return encode_uint16(self.wall_w)

    @property
    def edge_flags_blob(self) -> bytes:
        return encode_uint8(self.edge_flags)

    @property
    def flags_blob(self) -> bytes:
        return encode_uint8(self.flags)

    @classmethod
    def from_blobs(
        cls,
        cx: int,
        cy: int,
        z: int,
        floor_blob: bytes,
        floor_mat_blob: bytes,
        wall_n_blob: bytes,
        wall_w_blob: bytes,
        edge_flags_blob: bytes,
        flags_blob: bytes,
    ) -> ChunkLevel:
        return cls(
            cx,
            cy,
            z,
            decode_int16(floor_blob),
            decode_uint16(floor_mat_blob),
            decode_uint16(wall_n_blob),
            decode_uint16(wall_w_blob),
            decode_uint8(edge_flags_blob),
            decode_uint8(flags_blob),
        )
