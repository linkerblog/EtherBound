from etherbound.world.chunk import (
    CELL_COUNT,
    CHUNK_SIZE,
    NO_FLOOR,
    Chunk,
    ChunkLevel,
)
from etherbound.world.gen.types import GeneratedWorld

DEFAULT_STRATA: tuple[tuple[int, str], ...] = ((0, "topsoil"), (8, "dirt"), (24, "rock"))

_NORTH = "north"
_WEST = "west"


class CanvasLevel:
    """Floor, wall and flag setters for one ``z`` band of a :class:`GenCanvas`."""

    def __init__(self, canvas: GenCanvas, z: int) -> None:
        self._canvas = canvas
        self._z = z

    def _arrays(self, x: int, y: int) -> list[list[int]]:
        return self._canvas.level_arrays(x, y, self._z)

    def set_floor(self, x: int, y: int, h: int, material: int) -> None:
        arrays = self._arrays(x, y)
        index = self._canvas.index(x, y)
        arrays[0][index] = h
        arrays[1][index] = material

    def clear_floor(self, x: int, y: int) -> None:
        arrays = self._arrays(x, y)
        arrays[0][self._canvas.index(x, y)] = NO_FLOOR

    def set_wall(self, x: int, y: int, direction: str, material: int) -> None:
        if direction not in (_NORTH, _WEST):
            raise ValueError(f"wall direction must be {_NORTH!r} or {_WEST!r}")
        arrays = self._arrays(x, y)
        field = 2 if direction == _NORTH else 3
        arrays[field][self._canvas.index(x, y)] = material

    def set_edge(self, x: int, y: int, flag: int) -> None:
        arrays = self._arrays(x, y)
        arrays[4][self._canvas.index(x, y)] |= flag

    def set_flag(self, x: int, y: int, flag: int) -> None:
        arrays = self._arrays(x, y)
        arrays[5][self._canvas.index(x, y)] |= flag


class GenCanvas:
    """A flat, uniform base map that generators and features stamp into."""

    def __init__(
        self,
        chunks_x: int,
        chunks_y: int,
        base_h: int,
        base_mat: int,
        *,
        generator: str = "lab",
        gen_version: int = 1,
        strata: tuple[tuple[int, str], ...] = DEFAULT_STRATA,
    ) -> None:
        self.chunks_x = chunks_x
        self.chunks_y = chunks_y
        self.generator = generator
        self.gen_version = gen_version
        self.strata = strata
        self._ground: dict[tuple[int, int], list[int]] = {}
        self._surface: dict[tuple[int, int], list[int]] = {}
        self._levels: dict[tuple[int, int, int], list[list[int]]] = {}
        for cy in range(chunks_y):
            for cx in range(chunks_x):
                self._ground[(cx, cy)] = [base_h] * CELL_COUNT
                self._surface[(cx, cy)] = [base_mat] * CELL_COUNT

    @property
    def width(self) -> int:
        return self.chunks_x * CHUNK_SIZE

    @property
    def height(self) -> int:
        return self.chunks_y * CHUNK_SIZE

    def _chunk_key(self, x: int, y: int) -> tuple[int, int]:
        if not 0 <= x < self.width or not 0 <= y < self.height:
            raise IndexError(f"tile {x},{y} is outside the canvas")
        return x // CHUNK_SIZE, y // CHUNK_SIZE

    @staticmethod
    def index(x: int, y: int) -> int:
        return (y % CHUNK_SIZE) * CHUNK_SIZE + (x % CHUNK_SIZE)

    def set_ground(self, x: int, y: int, h: int) -> None:
        key = self._chunk_key(x, y)
        self._ground[key][self.index(x, y)] = h

    def set_surface(self, x: int, y: int, material: int) -> None:
        key = self._chunk_key(x, y)
        self._surface[key][self.index(x, y)] = material

    def get_ground(self, x: int, y: int) -> int:
        return self._ground[self._chunk_key(x, y)][self.index(x, y)]

    def level(self, z: int) -> CanvasLevel:
        return CanvasLevel(self, z)

    def level_arrays(self, x: int, y: int, z: int) -> list[list[int]]:
        key = (*self._chunk_key(x, y), z)
        arrays = self._levels.get(key)
        if arrays is None:
            arrays = [
                [NO_FLOOR] * CELL_COUNT,
                [0] * CELL_COUNT,
                [0] * CELL_COUNT,
                [0] * CELL_COUNT,
                [0] * CELL_COUNT,
                [0] * CELL_COUNT,
            ]
            self._levels[key] = arrays
        return arrays

    def build(self, spawn: tuple[float, float, int]) -> GeneratedWorld:
        chunks = {
            (cx, cy): Chunk(
                cx,
                cy,
                tuple(self._ground[(cx, cy)]),
                tuple(self._surface[(cx, cy)]),
                self.strata,
                gen_version=self.gen_version,
            )
            for cx, cy in sorted(self._ground)
        }
        levels = {
            key: ChunkLevel(key[0], key[1], key[2], *[tuple(values) for values in arrays])
            for key, arrays in self._levels.items()
        }
        return GeneratedWorld(chunks, levels, spawn, self.gen_version, (), self.generator)
