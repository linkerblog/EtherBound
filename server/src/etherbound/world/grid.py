from collections.abc import Iterable
from dataclasses import dataclass, replace

from etherbound.world.chunk import (
    CHUNK_SIZE,
    EDGE_N_DOORWAY,
    EDGE_W_DOORWAY,
    LEVEL_VOID,
    NO_FLOOR,
    Chunk,
    ChunkLevel,
)
from etherbound.world.materials import MaterialRegistry


@dataclass(frozen=True, slots=True)
class StandingSurface:
    h: int
    material_id: int
    z: int


class WorldGrid:
    """In-memory sparse world cache used by movement and navigation."""

    def __init__(
        self,
        chunks: Iterable[Chunk] | None = None,
        levels: Iterable[ChunkLevel] | None = None,
        registry: MaterialRegistry | None = None,
    ) -> None:
        self.registry = registry or MaterialRegistry.load()
        self._chunks: dict[tuple[int, int], Chunk] = {}
        self._levels: dict[tuple[int, int, int], ChunkLevel] = {}
        for chunk in chunks or ():
            self.add_chunk(chunk)
        for level in levels or ():
            self.add_level(level)

    @property
    def chunks(self) -> dict[tuple[int, int], Chunk]:
        return self._chunks

    @property
    def levels(self) -> dict[tuple[int, int, int], ChunkLevel]:
        return self._levels

    def add_chunk(self, chunk: Chunk) -> None:
        self._chunks[(chunk.cx, chunk.cy)] = chunk

    def add_level(self, level: ChunkLevel) -> None:
        self._levels[(level.cx, level.cy, level.z)] = level

    def chunk(self, cx: int, cy: int) -> Chunk | None:
        return self._chunks.get((cx, cy))

    def level(self, cx: int, cy: int, z: int) -> ChunkLevel | None:
        return self._levels.get((cx, cy, z))

    @staticmethod
    def chunk_coords(x: int, y: int) -> tuple[int, int, int, int]:
        cx, local_x = divmod(x, CHUNK_SIZE)
        cy, local_y = divmod(y, CHUNK_SIZE)
        return cx, cy, local_x, local_y

    def _cell(self, x: int, y: int) -> tuple[Chunk, int, int, int] | None:
        cx, cy, local_x, local_y = self.chunk_coords(x, y)
        chunk = self.chunk(cx, cy)
        if chunk is None:
            return None
        return chunk, local_x, local_y, chunk.index(local_x, local_y)

    def _levels_at(self, x: int, y: int) -> list[tuple[ChunkLevel, int]]:
        cell = self._cell(x, y)
        if cell is None:
            return []
        chunk, local_x, local_y, index = cell
        return [
            (level, index)
            for (cx, cy, _), level in self._levels.items()
            if cx == chunk.cx and cy == chunk.cy and level.index(local_x, local_y) == index
        ]

    def _void_at(self, x: int, y: int, h: int) -> bool:
        z = h // 6
        cell = self._cell(x, y)
        if cell is None:
            return False
        chunk, _, _, index = cell
        level = self.level(chunk.cx, chunk.cy, z)
        return level is not None and bool(level.flags[index] & LEVEL_VOID)

    def _material(self, material_id: int):
        return self.registry.get(material_id)

    def _stratum(self, chunk: Chunk, depth: int) -> int:
        selected = "rock"
        for boundary, key in sorted(chunk.strata):
            if depth >= boundary:
                selected = key
            else:
                break
        material = self.registry.get(selected)
        return material.id if material is not None else 0

    def _underground_material(self, chunk: Chunk, index: int, h: int) -> int:
        # Depth counts from the original ground, or a dug surface would drag the strata down.
        return self._stratum(chunk, chunk.ground_h[index] + chunk.dug[index] - h)

    def stratum_at(self, x: int, y: int, depth_from_original: int) -> int:
        """Material id of the volume ``depth_from_original`` half-metres below the original ground."""
        cell = self._cell(x, y)
        if cell is None:
            return 0
        return self._stratum(cell[0], depth_from_original)

    def ground_at(self, x: int, y: int) -> tuple[int, int, int] | None:
        """``(ground_h, surface_mat, dug)`` of a tile, or None outside the loaded world."""
        cell = self._cell(x, y)
        if cell is None:
            return None
        chunk, _, _, index = cell
        return chunk.ground_h[index], chunk.surface_mat[index], chunk.dug[index]

    def floors_at(self, x: int, y: int) -> tuple[int, ...]:
        """Floor heights of every level slab on a tile."""
        return tuple(
            level.floor_h[index]
            for level, index in self._levels_at(x, y)
            if level.floor_h[index] != NO_FLOOR
        )

    def lower_ground(self, x: int, y: int) -> Chunk:
        """Dig 0.5 m out of a tile's ground. Only the world engine calls this."""
        cell = self._cell(x, y)
        if cell is None:
            raise KeyError(f"no chunk at tile {x},{y}")
        chunk, _, _, index = cell
        ground_h = list(chunk.ground_h)
        surface_mat = list(chunk.surface_mat)
        dug = list(chunk.dug)
        ground_h[index] -= 1
        dug[index] += 1
        # The new surface is the top of the volume below the removed one.
        surface_mat[index] = self._stratum(chunk, dug[index] + 1)
        lowered = replace(
            chunk,
            ground_h=tuple(ground_h),
            surface_mat=tuple(surface_mat),
            dug=tuple(dug),
            revision=chunk.revision + 1,
        )
        self.add_chunk(lowered)
        return lowered

    def solid_at(self, x: int, y: int, h: int) -> bool:
        """Return whether a half-metre volume is occupied by implicit ground."""
        cell = self._cell(x, y)
        if cell is None:
            return True
        chunk, _, _, index = cell
        for level in self._levels.values():
            if level.cx != chunk.cx or level.cy != chunk.cy:
                continue
            if level.floor_h[index] == h:
                material = self._material(level.floor_mat[index])
                return material is None or material.solid
        ground_h = chunk.ground_h[index]
        if h >= ground_h or self._void_at(x, y, h):
            return False
        material_id = self._underground_material(chunk, index, h)
        material = self._material(material_id)
        return material is None or material.solid

    def _headroom(self, x: int, y: int, h: int) -> bool:
        return not any(self.solid_at(x, y, h + offset) for offset in range(1, 5))

    def standing_surfaces(self, x: int, y: int) -> tuple[StandingSurface, ...]:
        cell = self._cell(x, y)
        if cell is None:
            return ()
        chunk, _, _, index = cell
        result: list[StandingSurface] = []
        ground_h = chunk.ground_h[index]
        ground_material = self._material(chunk.surface_mat[index])
        if not self._void_at(x, y, ground_h) and ground_material is not None:
            if ground_material.walkable and self._headroom(x, y, ground_h):
                result.append(StandingSurface(ground_h, ground_material.id, ground_h // 6))
        for level in self._levels.values():
            if level.cx != chunk.cx or level.cy != chunk.cy:
                continue
            floor_h = level.floor_h[index]
            if floor_h == NO_FLOOR or not self._headroom(x, y, floor_h):
                continue
            material = self._material(level.floor_mat[index])
            if material is not None and material.walkable:
                result.append(StandingSurface(floor_h, material.id, level.z))
        return tuple(
            sorted({surface.h: surface for surface in result}.values(), key=lambda item: item.h)
        )

    def _wall_on_edge(self, x: int, y: int, direction: str, h: int) -> bool:
        cell = self._cell(x, y)
        if cell is None:
            return True
        chunk, _, _, index = cell
        z_min = h // 6 - 1
        z_max = (h + 4) // 6 + 1
        for z in range(z_min, z_max + 1):
            level = self.level(chunk.cx, chunk.cy, z)
            if level is None:
                continue
            wall = level.wall_n[index] if direction == "north" else level.wall_w[index]
            if not wall:
                continue
            bottom = level.floor_h[index]
            if bottom == NO_FLOOR:
                bottom = z * 6
            if bottom < h + 4 and bottom + 6 > h:
                flags = level.edge_flags[index]
                doorway = (
                    bool(flags & EDGE_N_DOORWAY)
                    if direction == "north"
                    else bool(flags & EDGE_W_DOORWAY)
                )
                if not doorway:
                    return True
        return False

    def wall_between(self, x1: int, y1: int, x2: int, y2: int, h: int) -> bool:
        """Return whether the shared edge blocks a two-metre body at ``h``."""
        dx, dy = x2 - x1, y2 - y1
        if (dx, dy) == (0, -1):
            return self._wall_on_edge(x1, y1, "north", h)
        if (dx, dy) == (0, 1):
            return self._wall_on_edge(x2, y2, "north", h)
        if (dx, dy) == (-1, 0):
            return self._wall_on_edge(x1, y1, "west", h)
        if (dx, dy) == (1, 0):
            return self._wall_on_edge(x2, y2, "west", h)
        raise ValueError("wall_between requires orthogonally adjacent tiles")

    def can_step(self, x1: int, y1: int, x2: int, y2: int, h: int | None = None) -> bool:
        if max(abs(x2 - x1), abs(y2 - y1)) > 1 or (x1 == x2 and y1 == y2):
            return False
        if abs(x2 - x1) == 1 and abs(y2 - y1) == 1:
            # A diagonal is legal only when both L-shaped detours around the corner
            # are open, so a path never cuts the corner of a wall.
            first_leg = self.can_step(x1, y1, x2, y1, h) and self.can_step(x2, y1, x2, y2, h)
            second_leg = self.can_step(x1, y1, x1, y2, h) and self.can_step(x1, y2, x2, y2, h)
            return first_leg and second_leg
        source = self.standing_surfaces(x1, y1)
        target = self.standing_surfaces(x2, y2)
        if h is not None:
            source = tuple(surface for surface in source if surface.h == h)
        for start in source:
            for end in target:
                if abs(end.h - start.h) <= 1 and not self.wall_between(x1, y1, x2, y2, start.h):
                    return True
        return False

    def bounds(self) -> tuple[int, int, int, int] | None:
        if not self._chunks:
            return None
        xs = [cx for cx, _ in self._chunks]
        ys = [cy for _, cy in self._chunks]
        return (
            min(xs) * CHUNK_SIZE,
            min(ys) * CHUNK_SIZE,
            (max(xs) + 1) * CHUNK_SIZE - 1,
            (max(ys) + 1) * CHUNK_SIZE - 1,
        )
