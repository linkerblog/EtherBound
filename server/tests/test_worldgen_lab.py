from time import perf_counter

from etherbound.world.gen.features.relief import ReliefOptions
from etherbound.world.gen.lab import BAY_KEYS, BAY_RECTS, LabOptions, generate_lab, lab_spawn
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry
from etherbound.world.nav import find_path
from etherbound.world.objects import ObjectCatalog

SEED = 7
FEATURE_CHUNKS = {(1, 1), (1, 2), (2, 1), (2, 2)}


def _grid(seed: int = SEED, options: LabOptions | None = None) -> WorldGrid:
    registry = MaterialRegistry.load()
    world = generate_lab(seed, options or LabOptions(), registry)
    return WorldGrid(world.chunks.values(), world.levels.values(), registry)


def _goal(grid: WorldGrid, x: int, y: int) -> tuple[int, int, int] | None:
    surfaces = grid.standing_surfaces(x, y)
    if not surfaces:
        return None
    return x, y, min(surface.h for surface in surfaces)


def test_lab_is_deterministic_and_four_by_four() -> None:
    first = generate_lab(SEED, LabOptions())
    second = generate_lab(SEED, LabOptions())
    assert len(first.chunks) == 16
    assert {key for key in first.chunks} == {(x, y) for x in range(4) for y in range(4)}
    assert first.blob_bytes() == second.blob_bytes()
    # The fixed fixture ignores the seed; a seed-driven feature does not.
    relief = LabOptions(feature="relief")
    assert generate_lab(SEED, relief).blob_bytes() == generate_lab(SEED, relief).blob_bytes()
    assert generate_lab(SEED, relief).blob_bytes() != generate_lab(SEED + 1, relief).blob_bytes()


def test_lab_generation_stays_under_half_a_second() -> None:
    started = perf_counter()
    world = generate_lab(SEED, LabOptions(feature="relief"))
    elapsed = perf_counter() - started
    assert len(world.chunks) == 16
    assert elapsed < 0.5


def test_every_spawn_bay_is_standable() -> None:
    registry = MaterialRegistry.load()
    world = generate_lab(SEED, LabOptions(), registry)
    assert {bay.key for bay in get_bays()} == set(BAY_KEYS)
    for key in BAY_KEYS:
        options = LabOptions(spawn_bay=key)  # type: ignore[arg-type]
        spawn_x, spawn_y, spawn_h = lab_spawn(SEED, options)
        grid = WorldGrid(world.chunks.values(), world.levels.values(), registry)
        surfaces = grid.standing_surfaces(int(spawn_x), int(spawn_y))
        assert any(surface.h == spawn_h for surface in surfaces), key


def get_bays():
    from etherbound.world.gen.registry import get_generator

    spec = get_generator("lab")
    assert spec is not None
    return spec.bays


def test_navigation_reaches_every_bay_from_the_default_spawn() -> None:
    grid = _grid()
    start = (61, 41, 2)
    for key in BAY_KEYS:
        x0, y0, _, _ = BAY_RECTS[key]
        goal = _goal(grid, x0 + 1, y0 + 1)
        assert goal is not None, key
        path = find_path(grid, start, goal)
        assert path is not None, f"cannot reach bay {key}"
        assert path[0] == start and path[-1] == goal


def test_materials_bay_has_a_patch_per_registered_material() -> None:
    registry = MaterialRegistry.load()
    grid = _grid()
    x0, y0, _, _ = BAY_RECTS["materials"]
    for index, material in enumerate(registry.materials):
        col, row = index % 6, index // 6
        px, py = x0 + col * 6, y0 + row * 6
        patch = grid.ground_at(px, py)
        block = grid.ground_at(px + 4, py)
        assert patch is not None and patch[0] == 2 and patch[1] == material.id, material.key
        assert block is not None and block[0] == 4 and block[1] == material.id, material.key


def test_objects_bay_has_every_catalog_kind() -> None:
    registry = MaterialRegistry.load()
    catalog = ObjectCatalog.load(registry=registry)
    world = generate_lab(SEED, LabOptions(), registry)
    x0, y0, x1, y1 = BAY_RECTS["objects"]
    kinds = {obj.kind for obj in world.objects if x0 <= obj.x <= x1 and y0 <= obj.y <= y1}
    assert kinds == set(catalog.keys())


def test_relief_amplitude_changes_only_the_feature_bay_chunks() -> None:
    small = generate_lab(SEED, LabOptions(feature="relief", relief=ReliefOptions(amplitude=8)))
    large = generate_lab(SEED, LabOptions(feature="relief", relief=ReliefOptions(amplitude=20)))
    changed = {
        key
        for key in small.chunks
        if small.chunks[key].ground_blob != large.chunks[key].ground_blob
    }
    assert changed
    assert changed <= FEATURE_CHUNKS


def test_relief_blends_to_flat_at_the_feature_bay_border() -> None:
    registry = MaterialRegistry.load()
    world = generate_lab(SEED, LabOptions(feature="relief", relief=ReliefOptions(amplitude=20)))
    x0, y0, x1, y1 = BAY_RECTS["feature"]
    grid = WorldGrid(world.chunks.values(), world.levels.values(), registry)
    for x in range(x0, x1 + 1):
        for y in (y0, y1):
            tile = grid.ground_at(x, y)
            assert tile is not None and tile[0] == 2, (x, y)
    for y in range(y0, y1 + 1):
        for x in (x0, x1):
            tile = grid.ground_at(x, y)
            assert tile is not None and tile[0] == 2, (x, y)
