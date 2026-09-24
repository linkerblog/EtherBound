from typing import Literal

from pydantic import BaseModel, Field

from etherbound.world.chunk import (
    EDGE_N_DOORWAY,
    EDGE_N_WINDOW,
    EDGE_W_DOORWAY,
    LEVEL_ROOF,
)
from etherbound.world.gen.canvas import GenCanvas
from etherbound.world.gen.features import FEATURES
from etherbound.world.gen.features.relief import ReliefOptions
from etherbound.world.gen.types import GeneratedObject, GeneratedWorld, GeneratorBay
from etherbound.world.materials import MaterialRegistry
from etherbound.world.objects import ObjectCatalog

GEN_VERSION = 1
CHUNKS = 4
BASE_H = 2
PATH_TILES = (0, 1, 2, 3, 40, 41, 42, 43, 80, 81, 82, 83, 120, 121, 122, 123)
BORDER = 124
BAY_COLUMNS = ((4, 39), (44, 79), (84, 119))
BAY_ROWS = ((4, 39), (44, 79), (84, 119))
BAY_KEYS = (
    "steps",
    "materials",
    "water",
    "structure",
    "feature",
    "objects",
    "dig",
    "walls",
    "open",
)

BAY_RECTS: dict[str, tuple[int, int, int, int]] = {}
BAY_POSITION: dict[str, tuple[int, int]] = {}
for _row, (_row0, _row1) in enumerate(BAY_ROWS):
    for _col, (_col0, _col1) in enumerate(BAY_COLUMNS):
        _key = BAY_KEYS[_row * 3 + _col]
        BAY_RECTS[_key] = (_col0, _row0, _col1, _row1)
        BAY_POSITION[_key] = (_col, _row)

LAB_BAYS = tuple(
    GeneratorBay(key=key, x=BAY_RECTS[key][0], y=BAY_RECTS[key][1], width=36, height=36)
    for key in BAY_KEYS
)


class LabOptions(BaseModel):
    feature: Literal["none", "relief"] = "none"
    spawn_bay: Literal[
        "steps",
        "materials",
        "water",
        "structure",
        "feature",
        "objects",
        "dig",
        "walls",
        "open",
    ] = "feature"
    relief: ReliefOptions = Field(default_factory=ReliefOptions)


def _id(registry: MaterialRegistry, key: str) -> int:
    return registry[key].id


def lab_spawn(seed: int, options: LabOptions) -> tuple[float, float, int]:
    col, row = BAY_POSITION[options.spawn_bay]
    x0, x1 = BAY_COLUMNS[col]
    north_path_y = (1.5, 41.5, 81.5)[row]
    return (x0 + x1) / 2 + 0.0, north_path_y, BASE_H


def _paths(canvas: GenCanvas, asphalt: int) -> None:
    for x in range(canvas.width):
        for y in range(canvas.height):
            if x >= BORDER or y >= BORDER:
                continue
            if x in PATH_TILES or y in PATH_TILES:
                canvas.set_ground(x, y, BASE_H)
                canvas.set_surface(x, y, asphalt)


def _steps(canvas: GenCanvas, concrete: int) -> None:
    x0, y0, _, y1 = BAY_RECTS["steps"]
    heights = (0, 1, 3, 6, 10, 16, 24)
    for index, lift in enumerate(heights):
        for y in range(y0, y1 + 1):
            for x in range(x0 + index * 4, x0 + index * 4 + 4):
                canvas.set_ground(x, y, BASE_H + lift)
    # A 1:1 ramp and a six-step stair in the flat eastern part of the bay.
    for y in range(y0, y0 + 4):
        for x in range(x0 + 28, x0 + 35):
            canvas.set_ground(x, y, BASE_H + min(x - (x0 + 28), 6))
    for y in range(y0 + 8, y0 + 12):
        for step in range(6):
            x = x0 + 28 + step
            canvas.set_ground(x, y, BASE_H + step + 1)
            canvas.set_surface(x, y, concrete)


def _materials(canvas: GenCanvas, registry: MaterialRegistry) -> None:
    x0, y0, _, _ = BAY_RECTS["materials"]
    for index, material in enumerate(registry.materials):
        col, row = index % 6, index // 6
        px, py = x0 + col * 6, y0 + row * 6
        for y in range(py, py + 4):
            for x in range(px, px + 4):
                canvas.set_ground(x, y, BASE_H)
                canvas.set_surface(x, y, material.id)
        for y in range(py, py + 2):
            for x in range(px + 4, px + 6):
                canvas.set_ground(x, y, BASE_H + 2)
                canvas.set_surface(x, y, material.id)


def _water(canvas: GenCanvas, shallow: int, deep: int) -> None:
    centre_x, centre_y = 101, 21
    for y in range(BAY_RECTS["water"][1], BAY_RECTS["water"][3] + 1):
        for x in range(BAY_RECTS["water"][0], BAY_RECTS["water"][2] + 1):
            distance = (x - centre_x) ** 2 + (y - centre_y) ** 2
            if distance <= 12**2:
                canvas.set_ground(x, y, BASE_H - (2 if distance > 7**2 else 4))
                canvas.set_surface(x, y, shallow if distance > 7**2 else deep)


def _structure(canvas: GenCanvas, registry: MaterialRegistry) -> None:
    x0, y0 = 14, 54
    x1, y1 = x0 + 9, y0 + 7
    brick = _id(registry, "brick")
    concrete = _id(registry, "concrete")
    wood = _id(registry, "wood_floor")
    roofing = _id(registry, "roofing")
    floor_materials = (concrete, concrete, wood, roofing)
    for z, floor_material in enumerate(floor_materials[:3]):
        level = canvas.level(z)
        floor_h = BASE_H + z * 6
        for y in range(y0 + 1, y1):
            for x in range(x0 + 1, x1):
                level.set_floor(x, y, floor_h, floor_material)
        for y in range(y0, y1 + 1):
            level.set_wall(x0, y, "west", brick)
            level.set_wall(x1, y, "west", brick)
        for x in range(x0, x1 + 1):
            level.set_wall(x, y0, "north", brick)
            level.set_wall(x, y1, "north", brick)
        if z == 0:
            level.set_edge(x0, (y0 + y1) // 2, EDGE_W_DOORWAY)
        else:
            for x in range(x0 + 1, x1):
                if x % 3 == 0:
                    level.set_edge(x, y0, EDGE_N_WINDOW)
    roof = canvas.level(2)
    for y in range(y0 + 1, y1):
        for x in range(x0 + 1, x1):
            roof.set_flag(x, y, LEVEL_ROOF)
    # Ground-to-first and first-to-roof stairs, with open stairwells in the slabs above.
    for index in range(6):
        x = x0 + 1 + index
        stair_h = BASE_H + 1 + index
        canvas.level(stair_h // 6).set_floor(x, y0 + 1, stair_h, concrete)
        if index < 3:
            canvas.level(1).clear_floor(x, y0 + 1)
    for index in range(6):
        x = x0 + 1 + index
        stair_h = BASE_H + 7 + index
        canvas.level(stair_h // 6).set_floor(x, y0 + 2, stair_h, concrete)
        if index < 3:
            canvas.level(2).clear_floor(x, y0 + 2)


def _objects(canvas: GenCanvas, registry: MaterialRegistry) -> list[GeneratedObject]:
    x0, y0, _, _ = BAY_RECTS["objects"]
    catalog = ObjectCatalog.load(registry=registry)
    row_y = y0 + 2
    objects: list[GeneratedObject] = []
    for index, kind in enumerate(catalog.kinds):
        x = x0 + 2 + index * 3
        objects.append(GeneratedObject(kind=kind.key, x=x, y=row_y, h=BASE_H))
        if kind.container is not None:
            objects.append(GeneratedObject(kind="apple", x=x, y=row_y, h=BASE_H, parent=index))
    stack_x, stack_y = x0 + 2, y0 + 8
    objects.append(GeneratedObject(kind="chest", x=stack_x, y=stack_y, h=BASE_H))
    objects.append(GeneratedObject(kind="chest", x=stack_x, y=stack_y, h=BASE_H + 2))
    return objects


def _dig(canvas: GenCanvas) -> None:
    pit_x0, pit_y0 = 16, 98
    for y in range(pit_y0, pit_y0 + 6):
        for x in range(pit_x0, pit_x0 + 6):
            canvas.set_ground(x, y, BASE_H - 4)
    for index, step in enumerate(range(4)):
        canvas.set_ground(pit_x0 + 6 + index, pit_y0 + 2, BASE_H - 4 + step + 1)


def _walls(canvas: GenCanvas, registry: MaterialRegistry) -> list[GeneratedObject]:
    x0, y0, _, _ = BAY_RECTS["walls"]
    level = canvas.level(0)
    objects: list[GeneratedObject] = []
    building = [material for material in registry.materials if "building" in material.tags]
    for index, material in enumerate(building):
        segment_y = y0 + 2 + index * 4
        for offset in range(6):
            x = x0 + 2 + offset
            level.set_wall(x, segment_y, "north", material.id)
        level.set_edge(x0 + 3, segment_y, EDGE_N_DOORWAY)
        level.set_edge(x0 + 5, segment_y, EDGE_N_WINDOW)
        objects.append(GeneratedObject(kind="chest", x=x0 + 4, y=segment_y + 1, h=BASE_H))
    return objects


def generate_lab(
    seed: int, options: LabOptions, registry: MaterialRegistry | None = None
) -> GeneratedWorld:
    registry = registry or MaterialRegistry.load()
    canvas = GenCanvas(
        CHUNKS,
        CHUNKS,
        BASE_H,
        _id(registry, "grass"),
        generator="lab",
        gen_version=GEN_VERSION,
    )
    _paths(canvas, _id(registry, "asphalt"))
    _steps(canvas, _id(registry, "concrete"))
    _materials(canvas, registry)
    _water(canvas, _id(registry, "water_shallow"), _id(registry, "water_deep"))
    _structure(canvas, registry)
    objects = _objects(canvas, registry)
    _dig(canvas)
    objects.extend(_walls(canvas, registry))
    if options.feature != "none":
        x0, y0, x1, y1 = BAY_RECTS["feature"]
        objects.extend(FEATURES[options.feature](canvas, (x0, y0, x1, y1), seed, options.relief))
    world = canvas.build(lab_spawn(seed, options))
    return GeneratedWorld(
        world.chunks, world.levels, world.spawn, world.gen_version, tuple(objects), "lab"
    )
