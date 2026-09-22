from collections.abc import Mapping
from dataclasses import dataclass

from etherbound.world.chunk import (
    CELL_COUNT,
    CHUNK_SIZE,
    EDGE_N_DOORWAY,
    EDGE_N_WINDOW,
    EDGE_W_DOORWAY,
    LEVEL_ROOF,
    LEVEL_VOID,
    NO_FLOOR,
    Chunk,
    ChunkLevel,
)
from etherbound.world.gen.noise import ValueNoise
from etherbound.world.materials import MaterialRegistry

GEN_VERSION = 2
WORLD_CHUNKS = 8
SPAWN_POINT = (121.5, 128.5)


@dataclass(frozen=True, slots=True)
class TestWorld:
    chunks: Mapping[tuple[int, int], Chunk]
    levels: Mapping[tuple[int, int, int], ChunkLevel]
    spawn: tuple[float, float, int]
    gen_version: int = GEN_VERSION

    def blob_bytes(self) -> bytes:
        chunks = b"".join(
            self.chunks[key].ground_blob + self.chunks[key].surface_blob
            for key in sorted(self.chunks)
        )
        levels = b"".join(
            self.levels[key].floor_blob
            + self.levels[key].wall_n_blob
            + self.levels[key].wall_w_blob
            for key in sorted(self.levels)
        )
        return chunks + levels


def _id(registry: MaterialRegistry, key: str) -> int:
    return registry[key].id


def generate_test_world(seed: int, registry: MaterialRegistry | None = None) -> TestWorld:
    registry = registry or MaterialRegistry.load()
    noise = ValueNoise(seed)
    grass = _id(registry, "grass")
    asphalt = _id(registry, "asphalt")
    shallow = _id(registry, "water_shallow")
    deep = _id(registry, "water_deep")
    ground: dict[tuple[int, int], list[int]] = {}
    surfaces: dict[tuple[int, int], list[int]] = {}

    for cy in range(WORLD_CHUNKS):
        for cx in range(WORLD_CHUNKS):
            for local_y in range(CHUNK_SIZE):
                gy = cy * CHUNK_SIZE + local_y
                for local_x in range(CHUNK_SIZE):
                    gx = cx * CHUNK_SIZE + local_x
                    relief = noise.fbm(gx / 96.0, gy / 96.0, octaves=3)
                    south_flatten = max(0.0, min(1.0, (gy - 160) / 95.0))
                    height = round(relief * 12.0 * (1.0 - south_flatten))
                    material = grass
                    # A road is deliberately level so the movement layer has a reliable spawn.
                    if 120 <= gx <= 123:
                        height = 2
                        material = asphalt
                    # The terrace has a ramp-sized gap in its middle.
                    if 88 <= gy <= 90 and not 108 <= gx <= 114 and not 120 <= gx <= 123:
                        height += 3
                    # The park pit is a lowered heightfield, with a one-tile ramp on its east side.
                    if 48 <= gx <= 72 and 176 <= gy <= 200:
                        depth = 4 if gx < 70 else max(0, 4 - (gx - 69))
                        height -= depth
                    # The pond uses material, rather than a special water volume.
                    pond_distance = (gx - 196) ** 2 + (gy - 70) ** 2
                    if pond_distance <= 12**2:
                        height -= 2 if pond_distance > 7**2 else 4
                        material = shallow if pond_distance > 7**2 else deep
                    ground[(cx, cy)] = ground.get((cx, cy), []) + [height]
                    surfaces[(cx, cy)] = surfaces.get((cx, cy), []) + [material]

    levels: dict[tuple[int, int, int], list[list[int]]] = {}
    # A small levelled building with a basement, stairs, roof and edge walls.
    building = {(x, y) for y in range(132, 161) for x in range(136, 161)}
    base_h = 6
    brick = _id(registry, "brick")
    concrete = _id(registry, "concrete")
    wood = _id(registry, "wood_floor")
    roofing = _id(registry, "roofing")
    for z, floor_material in ((1, concrete), (2, wood), (3, wood), (4, roofing)):
        for x, y in building:
            cx, lx = divmod(x, CHUNK_SIZE)
            cy, ly = divmod(y, CHUNK_SIZE)
            key = (cx, cy, z)
            if key not in levels:
                levels[key] = [
                    [NO_FLOOR] * CELL_COUNT,
                    [0] * CELL_COUNT,
                    [0] * CELL_COUNT,
                    [0] * CELL_COUNT,
                    [0] * CELL_COUNT,
                    [0] * CELL_COUNT,
                ]
            index = ly * CHUNK_SIZE + lx
            levels[key][0][index] = base_h + (z - 1) * 6
            levels[key][1][index] = floor_material if z != 3 else roofing
            if x == 136:
                levels[key][3][index] = brick
                if y == 146:
                    levels[key][4][index] |= EDGE_W_DOORWAY
            if y == 132:
                levels[key][2][index] = brick
                if x == 146:
                    levels[key][4][index] |= EDGE_N_DOORWAY
                elif x % 4 == 0:
                    levels[key][4][index] |= EDGE_N_WINDOW
            if x == 160:
                levels[key][3][index] = brick
            if y == 160:
                levels[key][2][index] = brick
            if z == 4:
                levels[key][5][index] |= LEVEL_ROOF
            if z == 1:
                levels[key][5][index] |= LEVEL_VOID

    # Level the building footprint and make six graded tiles the internal stairs.
    for x, y in building:
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(y, CHUNK_SIZE)
        ground[(cx, cy)][ly * CHUNK_SIZE + lx] = base_h + 6
    for step, x in enumerate(range(145, 151), start=1):
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(150, CHUNK_SIZE)
        level = levels[(cx, cy, 3)]
        level[0][ly * CHUNK_SIZE + lx] = base_h + 6 + step
    # The stairwell cuts the roof open, or the slab blocks the headroom of the steps.
    for x in range(145, 151):
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(150, CHUNK_SIZE)
        levels[(cx, cy, 4)][0][ly * CHUNK_SIZE + lx] = NO_FLOOR
    # Keep the first flight's approach below the roof and provide a second flight one tile south.
    for step, x in enumerate(range(145, 151), start=1):
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(151, CHUNK_SIZE)
        levels[(cx, cy, 3)][0][ly * CHUNK_SIZE + lx] = base_h + 6 + 6 + step
        levels[(cx, cy, 4)][0][ly * CHUNK_SIZE + lx] = NO_FLOOR
    for x in range(136, 161):
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(150, CHUNK_SIZE)
        levels[(cx, cy, 4)][0][ly * CHUNK_SIZE + lx] = NO_FLOOR
    # A graded approach climbs to the west doorway, so the interior is walkable.
    for x in range(124, 136):
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(146, CHUNK_SIZE)
        index = ly * CHUNK_SIZE + lx
        ground[(cx, cy)][index] = base_h + 6 - (136 - x)

    chunks = {
        (cx, cy): Chunk(
            cx,
            cy,
            tuple(ground[(cx, cy)]),
            tuple(surfaces[(cx, cy)]),
            ((0, "topsoil"), (8, "dirt"), (24, "rock")),
            gen_version=GEN_VERSION,
        )
        for cx, cy in sorted(ground)
    }
    chunk_levels = {
        key: ChunkLevel(key[0], key[1], key[2], *[tuple(values) for values in fields])
        for key, fields in levels.items()
    }
    return TestWorld(chunks, chunk_levels, (*SPAWN_POINT, 0))


def generate(seed: int, registry: MaterialRegistry | None = None) -> TestWorld:
    return generate_test_world(seed, registry)
