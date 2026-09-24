from dataclasses import dataclass
from typing import Any

from etherbound.engine.actions import ActivityState, CarriedObject, MenuEntry, Mind
from etherbound.world.gen.registry import DEFAULT_GENERATOR
from etherbound.world.grid import WorldGrid


@dataclass(frozen=True, slots=True)
class ActorState:
    id: str
    kind: str
    x: float
    y: float
    z: int
    h: int
    activity: ActivityState | None = None
    carried: tuple[CarriedObject, ...] = ()
    load_kg: float = 0.0
    name: str | None = None
    mind: Mind | None = None


@dataclass(frozen=True, slots=True)
class WorldState:
    seed: int
    game_minute: int
    speed: int
    paused: bool
    actors: tuple[ActorState, ...]
    gen_version: int
    generator: str = DEFAULT_GENERATOR
    gen_options: dict[str, Any] | None = None


@dataclass(frozen=True, slots=True)
class ChunkLevelPayload:
    z: int
    floor_h: tuple[int, ...]
    floor_mat: tuple[int, ...]
    wall_n: tuple[int, ...]
    wall_w: tuple[int, ...]
    edge_flags: tuple[int, ...]
    flags: tuple[int, ...]


@dataclass(frozen=True, slots=True)
class ObjectPayload:
    id: int
    kind: str
    x: int
    y: int
    h: int
    quantity: int
    open: bool | None


@dataclass(frozen=True, slots=True)
class ChunkPayload:
    cx: int
    cy: int
    revision: int
    ground_h: tuple[int, ...]
    surface_mat: tuple[int, ...]
    levels: tuple[ChunkLevelPayload, ...]
    objects: tuple[ObjectPayload, ...] = ()


@dataclass(frozen=True, slots=True)
class MenuPayload:
    x: float
    y: float
    z: int
    target: str
    entries: tuple[MenuEntry, ...]


@dataclass(frozen=True, slots=True)
class WorldInfo:
    chunk_size: int
    level_h: int
    bounds: tuple[int, int, int, int]


def chunk_payload(grid: WorldGrid, cx: int, cy: int) -> ChunkPayload | None:
    chunk = grid.chunk(cx, cy)
    if chunk is None:
        return None
    levels = tuple(
        ChunkLevelPayload(
            z=level.z,
            floor_h=level.floor_h,
            floor_mat=level.floor_mat,
            wall_n=level.wall_n,
            wall_w=level.wall_w,
            edge_flags=level.edge_flags,
            flags=level.flags,
        )
        for (level_cx, level_cy, _), level in sorted(grid.levels.items())
        if level_cx == cx and level_cy == cy
    )
    return ChunkPayload(
        cx=cx,
        cy=cy,
        revision=chunk.revision,
        ground_h=chunk.ground_h,
        surface_mat=chunk.surface_mat,
        levels=levels,
        objects=tuple(
            ObjectPayload(
                id=obj.id,
                kind=obj.kind,
                x=obj.x,
                y=obj.y,
                h=obj.h,
                quantity=obj.quantity,
                open=obj.open,
            )
            for obj in grid.objects_at_chunk(cx, cy)
        ),
    )
