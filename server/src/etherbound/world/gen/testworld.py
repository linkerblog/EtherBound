import json
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

GEN_VERSION = 4
WORLD_CHUNKS = 8
SPAWN_POINT = (121.5, 128.5)


@dataclass(frozen=True, slots=True)
class GeneratedObject:
    """One object the generator places. ``parent`` is the index of its container, if any."""

    kind: str
    x: int
    y: int
    h: int
    quantity: int = 1
    open: bool = False
    parent: int | None = None


def test_objects() -> tuple[GeneratedObject, ...]:
    """The fixed v4 object layout; order is stable, so generated ids are stable."""
    return (
        # 1. Near spawn (grass at h = 2, within four tiles of 121.5, 128.5).
        GeneratedObject(kind="shovel", x=124, y=126, h=2),
        GeneratedObject(kind="backpack", x=125, y=126, h=2),
        GeneratedObject(kind="chest", x=124, y=127, h=2),
        GeneratedObject(kind="apple", x=124, y=127, h=2, quantity=3, parent=2),
        GeneratedObject(kind="bottle", x=124, y=127, h=2, quantity=2, parent=2),
        # 2. Building ground floor (interior at h = 12).
        GeneratedObject(kind="table", x=137, y=133, h=12),
        GeneratedObject(kind="bottle", x=137, y=133, h=14),
        GeneratedObject(kind="chair", x=138, y=133, h=12),
        GeneratedObject(kind="chair", x=137, y=134, h=12),
        GeneratedObject(kind="shelf", x=139, y=133, h=12),
        GeneratedObject(kind="apple", x=139, y=133, h=12, quantity=2, parent=9),
        GeneratedObject(kind="barrel", x=140, y=133, h=12),
        # 3. Climbing test: two chests stacked on open ground at h = 1.
        GeneratedObject(kind="chest", x=126, y=127, h=1),
        GeneratedObject(kind="chest", x=126, y=127, h=3),
    )


@dataclass(frozen=True, slots=True)
class TestWorld:
    chunks: Mapping[tuple[int, int], Chunk]
    levels: Mapping[tuple[int, int, int], ChunkLevel]
    spawn: tuple[float, float, int]
    gen_version: int = GEN_VERSION
    objects: tuple[GeneratedObject, ...] = ()

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
            cells = [0] * CELL_COUNT
            materials = [grass] * CELL_COUNT
            for local_y in range(CHUNK_SIZE):
                gy = cy * CHUNK_SIZE + local_y
                for local_x in range(CHUNK_SIZE):
                    gx = cx * CHUNK_SIZE + local_x
                    index = local_y * CHUNK_SIZE + local_x
                    relief = noise.fbm(gx / 96.0, gy / 96.0, octaves=3)
                    south_flatten = max(0.0, min(1.0, (gy - 160) / 95.0))
                    height = round(relief * 12.0 * (1.0 - south_flatten))
                    material = grass
                    on_road = 120 <= gx <= 123
                    if on_road:
                        height = 2
                        material = asphalt
                    else:
                        # Keep the bench local so the road spawn remains connected to its surroundings.
                        terrace_lift = 0
                        if 89 <= gy <= 94:
                            terrace_lift = 3
                        elif 95 <= gy <= 96:
                            terrace_lift = 97 - gy
                        if 108 <= gx <= 114 and 89 <= gy <= 90:
                            terrace_lift = gy - 88
                        height += terrace_lift
                    # The park pit is a lowered heightfield, with a one-tile ramp on its east side.
                    if 48 <= gx <= 72 and 176 <= gy <= 200:
                        depth = 4 if gx < 70 else max(0, 4 - (gx - 69))
                        height -= depth
                    # The pond uses material, rather than a special water volume.
                    pond_distance = (gx - 196) ** 2 + (gy - 70) ** 2
                    if pond_distance <= 12**2:
                        height -= 2 if pond_distance > 7**2 else 4
                        material = shallow if pond_distance > 7**2 else deep
                    cells[index] = height
                    materials[index] = material
            ground[(cx, cy)] = cells
            surfaces[(cx, cy)] = materials

    levels: dict[tuple[int, int, int], list[list[int]]] = {}
    # Walls occupy the perimeter edges; only the enclosed interior has floor slabs.
    perimeter = {(x, y) for y in range(132, 161) for x in range(136, 161)}
    interior = {(x, y) for y in range(133, 160) for x in range(137, 160)}
    base_h = 6
    brick = _id(registry, "brick")
    concrete = _id(registry, "concrete")
    wood = _id(registry, "wood_floor")
    roofing = _id(registry, "roofing")
    basement_stairs = {(x, 152): base_h + x - 144 for x in range(145, 151)}
    first_floor_stairs = {(x, 150): base_h + 6 + x - 144 for x in range(145, 151)}
    roof_stairs = {(x, 151): base_h + 12 + x - 144 for x in range(145, 151)}
    for z, floor_material in ((1, concrete), (2, concrete), (3, wood), (4, roofing)):
        for x, y in perimeter:
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
            if (x, y) in interior:
                levels[key][0][index] = base_h + (z - 1) * 6
                levels[key][1][index] = floor_material
            if x == 136:
                levels[key][3][index] = brick
                if y == 146:
                    levels[key][4][index] |= EDGE_W_DOORWAY
            if y == 132:
                levels[key][2][index] = brick
                if x == 146:
                    levels[key][4][index] |= EDGE_N_DOORWAY
                elif z < 4 and x % 4 == 0:
                    levels[key][4][index] |= EDGE_N_WINDOW
            if x == 160:
                levels[key][3][index] = brick
            if y == 160:
                levels[key][2][index] = brick
                if z == 1 and x == 146:
                    levels[key][4][index] |= EDGE_N_DOORWAY
            if z == 4 and (x, y) in interior:
                levels[key][5][index] |= LEVEL_ROOF
            if z in (1, 2) and (x, y) in interior:
                levels[key][5][index] |= LEVEL_VOID

    # The ground approaches the west door from the continuous road at the same standing height.
    for x, y in interior:
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(y, CHUNK_SIZE)
        index = ly * CHUNK_SIZE + lx
        ground[(cx, cy)][index] = base_h + 6
    # Keep the stairwell open while its west roof landing joins both sides of the roof.
    for x in range(136, 161):
        if x == 144:
            continue
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(150, CHUNK_SIZE)
        levels[(cx, cy, 4)][0][ly * CHUNK_SIZE + lx] = NO_FLOOR
    # Extend and grade the approach from the road to the west doorway.
    for x in range(124, 137):
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(146, CHUNK_SIZE)
        index = ly * CHUNK_SIZE + lx
        ground[(cx, cy)][index] = min(base_h + 6, 2 + x - 123)
        if x == 136:
            # The wall tile is floorless but still meets the interior at the ground-floor height.
            ground[(cx, cy)][index] = base_h + 6

    # A south-side stair flight drops from the ground floor into the basement.
    for (x, y), height in basement_stairs.items():
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(y, CHUNK_SIZE)
        index = ly * CHUNK_SIZE + lx
        basement = levels[(cx, cy, 1)]
        ground_level = levels[(cx, cy, 2)]
        basement[0][index] = height
        ground_level[0][index] = NO_FLOOR
        ground_level[5][index] |= LEVEL_VOID

    # Stairs continue from the ground floor to the first floor and up to the roof.
    for (x, y), height in first_floor_stairs.items():
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(y, CHUNK_SIZE)
        index = ly * CHUNK_SIZE + lx
        levels[(cx, cy, 3)][0][index] = height
    for (x, y), height in roof_stairs.items():
        cx, lx = divmod(x, CHUNK_SIZE)
        cy, ly = divmod(y, CHUNK_SIZE)
        index = ly * CHUNK_SIZE + lx
        levels[(cx, cy, 4)][0][index] = height

    # Keep the outdoor landing south of the basement doorway level with its floor.
    for y in range(160, 165):
        for x in range(142, 151):
            cx, lx = divmod(x, CHUNK_SIZE)
            cy, ly = divmod(y, CHUNK_SIZE)
            ground[(cx, cy)][ly * CHUNK_SIZE + lx] = base_h

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
    return TestWorld(chunks, chunk_levels, (*SPAWN_POINT, 0), GEN_VERSION, test_objects())
