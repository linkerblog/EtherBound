from dataclasses import dataclass
from typing import Any

from etherbound.engine.actions import ActivityState, CarriedObject, MenuEntry, Mind
from etherbound.world.gen.registry import DEFAULT_GENERATOR


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
