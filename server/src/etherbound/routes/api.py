from math import floor
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from pydantic import BaseModel, Field

from etherbound.engine.world import WorldEngine

router = APIRouter(prefix="/api")


class HealthResponse(BaseModel):
    status: str
    game_minute: int


class NewGameRequest(BaseModel):
    seed: int = Field(default=0)


class ActorResponse(BaseModel):
    id: str
    kind: str
    x: float
    y: float
    z: int
    h: int


class StateResponse(BaseModel):
    seed: int
    game_minute: int
    speed: int
    paused: bool
    actors: list[ActorResponse]


class MaterialResponse(BaseModel):
    id: int
    key: str
    name: str
    color: str
    walkable: bool
    walk_cost: float
    solid: bool
    blocks_sight: bool
    diggable: bool
    dig_cost: float
    flammable: bool
    density: float
    resistance: float
    liquid: bool
    tags: list[str]


class ChunkLevelResponse(BaseModel):
    z: int
    floor_h: list[int]
    floor_mat: list[int]
    wall_n: list[int]
    wall_w: list[int]
    edge_flags: list[int]
    flags: list[int]


class ChunkResponse(BaseModel):
    cx: int
    cy: int
    revision: int
    ground_h: list[int]
    surface_mat: list[int]
    levels: list[ChunkLevelResponse]


class MenuResponse(BaseModel):
    x: float
    y: float
    z: int
    target: str
    verbs: list[str]


class EventRecord(BaseModel):
    seq: int
    game_minute: int
    type: str
    actor_id: str | None
    data: dict[str, Any]


class EventsResponse(BaseModel):
    events: list[EventRecord]


def get_engine(request: Request) -> WorldEngine:
    return request.app.state.engine


def state_response(engine: WorldEngine) -> StateResponse:
    state = engine.get_state()
    return StateResponse(
        seed=state.seed,
        game_minute=state.game_minute,
        speed=state.speed,
        paused=state.paused,
        actors=[
            ActorResponse(id=actor.id, kind=actor.kind, x=actor.x, y=actor.y, z=actor.z, h=actor.h)
            for actor in state.actors
        ],
    )


@router.get("/health", response_model=HealthResponse)
def health(engine: WorldEngine = Depends(get_engine)) -> HealthResponse:  # noqa: B008
    return HealthResponse(status="ok", game_minute=engine.get_state().game_minute)


@router.post("/game/new", response_model=StateResponse)
async def new_game(
    body: NewGameRequest,
    engine: WorldEngine = Depends(get_engine),  # noqa: B008
) -> StateResponse:
    await engine.new_game(body.seed)
    return state_response(engine)


@router.get("/game/state", response_model=StateResponse)
def game_state(engine: WorldEngine = Depends(get_engine)) -> StateResponse:  # noqa: B008
    return state_response(engine)


@router.get("/events", response_model=EventsResponse)
def events(
    after_seq: int = Query(0, ge=0),
    limit: int = Query(100, ge=1, le=500),
    event_type: str | None = Query(None, alias="type"),
    actor_id: str | None = Query(None),
    engine: WorldEngine = Depends(get_engine),  # noqa: B008
) -> EventsResponse:
    return EventsResponse(
        events=[
            EventRecord(
                seq=row.seq,
                game_minute=row.game_minute,
                type=row.type,
                actor_id=row.actor_id,
                data=row.data,
            )
            for row in engine.read_events(after_seq, limit, event_type, actor_id)
        ]
    )


@router.get("/materials", response_model=list[MaterialResponse])
def materials(engine: WorldEngine = Depends(get_engine)) -> list[MaterialResponse]:  # noqa: B008
    return [
        MaterialResponse(
            id=material.id,
            key=material.key,
            name=material.name,
            color=material.color,
            walkable=material.walkable,
            walk_cost=material.walk_cost,
            solid=material.solid,
            blocks_sight=material.blocks_sight,
            diggable=material.diggable,
            dig_cost=material.dig_cost,
            flammable=material.flammable,
            density=material.density,
            resistance=material.resistance,
            liquid=material.liquid,
            tags=list(material.tags),
        )
        for material in engine.registry
    ]


@router.get("/world/chunk", response_model=ChunkResponse)
def world_chunk(
    cx: int = Query(...),
    cy: int = Query(...),
    engine: WorldEngine = Depends(get_engine),  # noqa: B008
) -> ChunkResponse:
    payload = engine.chunk_payload(cx, cy)
    if payload is None:
        raise HTTPException(status_code=404, detail="chunk does not exist")
    return ChunkResponse(
        cx=payload.cx,
        cy=payload.cy,
        revision=payload.revision,
        ground_h=list(payload.ground_h),
        surface_mat=list(payload.surface_mat),
        levels=[
            ChunkLevelResponse(
                z=level.z,
                floor_h=list(level.floor_h),
                floor_mat=list(level.floor_mat),
                wall_n=list(level.wall_n),
                wall_w=list(level.wall_w),
                edge_flags=list(level.edge_flags),
                flags=list(level.flags),
            )
            for level in payload.levels
        ],
    )


@router.get("/menu", response_model=MenuResponse)
def menu(
    x: float = Query(...),
    y: float = Query(...),
    z: int = Query(0),
    engine: WorldEngine = Depends(get_engine),  # noqa: B008
) -> MenuResponse:
    tile_x, tile_y = floor(x), floor(y)
    surfaces = engine.grid.standing_surfaces(tile_x, tile_y)
    visible = [surface for surface in surfaces if surface.z == z] or surfaces
    if visible:
        surface = min(visible, key=lambda item: abs(item.z * 6 - z * 6))
        material = engine.registry.get(surface.material_id)
        name = material.name if material is not None else "unknown"
        metres = surface.h * 0.5
        target = f"{name} · {metres:.1f} m".replace(".0 m", " m")
    else:
        target = "nothing"
    return MenuResponse(x=x, y=y, z=z, target=target, verbs=["inspect"])
