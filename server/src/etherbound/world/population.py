"""Who lives in a generated world.

Generators build space; this separate pass decides who stands in it. It is deliberately not part of
``GeneratedWorld``: population is actors, not terrain, and must not touch the test world's bytes.
"""

import tomllib
from dataclasses import dataclass
from functools import lru_cache
from math import floor, hypot
from pathlib import Path
from typing import cast

from etherbound.rng import RNGStreams
from etherbound.world.grid import WorldGrid
from etherbound.world.materials import MaterialRegistry
from etherbound.world.nav import find_path

EXTRA_COUNT = 6
PLACEMENT_RADIUS_M = 15
MIN_SPACING_M = 2.0
MAX_EXPANSIONS = 3000
EXTRA_MASS_KG = 80.0


@dataclass(frozen=True, slots=True)
class GeneratedActor:
    """One seeded Extra: where it starts and the home spot its routine wanders around."""

    id: str
    name: str
    x: float
    y: float
    h: int
    anchor_x: int
    anchor_y: int
    anchor_h: int


@lru_cache(maxsize=1)
def _names() -> tuple[tuple[str, ...], tuple[str, ...]]:
    source = Path(__file__).with_name("names.toml")
    with source.open("rb") as file:
        document = tomllib.load(file)
    given = document.get("given")
    family = document.get("family")
    if not isinstance(given, list) or not isinstance(family, list):
        raise ValueError("names.toml must contain given and family arrays")
    given_names = cast(list[object], given)
    family_names = cast(list[object], family)
    return (
        tuple(str(name) for name in given_names),
        tuple(str(name) for name in family_names),
    )


def _candidates(
    grid: WorldGrid, registry: MaterialRegistry, tile_x: int, tile_y: int
) -> list[tuple[int, int, int]]:
    """Walkable, standing ground tiles within the placement radius, spawn tile excluded."""
    result: list[tuple[int, int, int]] = []
    for dy in range(-PLACEMENT_RADIUS_M, PLACEMENT_RADIUS_M + 1):
        for dx in range(-PLACEMENT_RADIUS_M, PLACEMENT_RADIUS_M + 1):
            if dx == 0 and dy == 0:
                continue
            if hypot(dx, dy) > PLACEMENT_RADIUS_M:
                continue
            x, y = tile_x + dx, tile_y + dy
            ground = grid.ground_at(x, y)
            if ground is None:
                continue
            ground_h, material_id, _ = ground
            material = registry.get(material_id)
            if material is None or not material.walkable:
                continue
            if not any(surface.h == ground_h for surface in grid.standing_surfaces(x, y)):
                continue
            result.append((x, y, ground_h))
    return result


def _spacing_ok(
    candidate: tuple[int, int, int], chosen: list[tuple[int, int, int]], limit: float
) -> bool:
    x, y, _ = candidate
    return all(hypot(x - other[0], y - other[1]) >= limit for other in chosen)


def populate(
    grid: WorldGrid,
    registry: MaterialRegistry,
    spawn: tuple[float, float, int],
    seed: int,
) -> tuple[GeneratedActor, ...]:
    """Seed ``EXTRA_COUNT`` Extras near the spawn, deterministically from one stream."""
    rng = RNGStreams(seed).stream("population")
    given, family = _names()
    names = list(
        zip(
            rng.sample(list(given), EXTRA_COUNT),
            rng.sample(list(family), EXTRA_COUNT),
            strict=True,
        )
    )

    tile_x, tile_y = floor(spawn[0]), floor(spawn[1])
    start = (tile_x, tile_y, spawn[2])
    candidates = _candidates(grid, registry, tile_x, tile_y)
    rng.shuffle(candidates)

    chosen: list[tuple[int, int, int]] = []
    for limit in (MIN_SPACING_M, 1.0, 0.0):
        for candidate in candidates:
            if len(chosen) == EXTRA_COUNT:
                break
            if candidate in chosen or not _spacing_ok(candidate, chosen, limit):
                continue
            if find_path(grid, start, candidate, MAX_EXPANSIONS) is None:
                continue
            chosen.append(candidate)
        if len(chosen) == EXTRA_COUNT:
            break

    return tuple(
        GeneratedActor(
            id=f"extra-{index:03d}",
            name=f"{names[index - 1][0]} {names[index - 1][1]}",
            x=float(x) + 0.5,
            y=float(y) + 0.5,
            h=h,
            anchor_x=x,
            anchor_y=y,
            anchor_h=h,
        )
        for index, (x, y, h) in enumerate(chosen, start=1)
    )
