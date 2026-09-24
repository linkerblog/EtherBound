from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, Request
from pydantic import BaseModel, Field, ValidationError

from etherbound.engine.actions import MenuEntry
from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.world.gen.registry import DEFAULT_GENERATOR, GENERATORS, option_fields

router = APIRouter(prefix="/api")


class HealthResponse(BaseModel):
    status: str
    game_minute: int


class NewGameRequest(BaseModel):
    seed: int = Field(default=0)
    generator: str | None = None
    options: dict[str, Any] | None = None


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
    generator: str
    gen_version: int
    gen_options: dict[str, Any]


class OptionFieldResponse(BaseModel):
    path: str
    label: str
    kind: str
    default: Any
    min: float | None
    max: float | None
    step: float | None
    choices: list[str]
    group: str | None


class GeneratorBayResponse(BaseModel):
    key: str
    x: int
    y: int
    width: int
    height: int


class GeneratorInfoResponse(BaseModel):
    key: str
    name: str
    version: int
    fields: list[OptionFieldResponse]
    bays: list[GeneratorBayResponse]


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


class ObjectResponse(BaseModel):
    id: int
    kind: str
    x: int
    y: int
    h: int
    quantity: int
    open: bool | None


class ObjectKindResponse(BaseModel):
    key: str
    name: str
    material: str
    mass: float
    bulk: float
    height: int
    solid: bool
    surface: bool
    openable: bool
    container_capacity: float | None


class ChunkResponse(BaseModel):
    cx: int
    cy: int
    revision: int
    ground_h: list[int]
    surface_mat: list[int]
    levels: list[ChunkLevelResponse]
    objects: list[ObjectResponse]


class MenuResponse(BaseModel):
    x: float
    y: float
    z: int
    target: str
    ops: list[MenuEntry]


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
        generator=state.generator,
        gen_version=state.gen_version,
        gen_options=state.gen_options or {},
    )


@router.get("/health", response_model=HealthResponse)
def health(engine: WorldEngine = Depends(get_engine)) -> HealthResponse:  # noqa: B008
    return HealthResponse(status="ok", game_minute=engine.get_state().game_minute)


@router.post("/game/new", response_model=StateResponse)
async def new_game(
    body: NewGameRequest,
    engine: WorldEngine = Depends(get_engine),  # noqa: B008
) -> StateResponse:
    try:
        await engine.new_game(body.seed, body.generator or DEFAULT_GENERATOR, body.options)
    except (ValueError, ValidationError) as error:
        raise HTTPException(status_code=422, detail=str(error)) from error
    return state_response(engine)


@router.get("/gen", response_model=list[GeneratorInfoResponse])
def generators() -> list[GeneratorInfoResponse]:
    return [
        GeneratorInfoResponse(
            key=spec.key,
            name=spec.name,
            version=spec.version,
            fields=[
                OptionFieldResponse(
                    path=field.path,
                    label=field.label,
                    kind=field.kind,
                    default=field.default,
                    min=field.minimum,
                    max=field.maximum,
                    step=field.step,
                    choices=list(field.choices),
                    group=field.group,
                )
                for field in option_fields(spec.options)
            ],
            bays=[
                GeneratorBayResponse(
                    key=bay.key, x=bay.x, y=bay.y, width=bay.width, height=bay.height
                )
                for bay in spec.bays
            ],
        )
        for spec in GENERATORS.values()
    ]


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
        objects=[
            ObjectResponse(
                id=obj.id,
                kind=obj.kind,
                x=obj.x,
                y=obj.y,
                h=obj.h,
                quantity=obj.quantity,
                open=obj.open,
            )
            for obj in payload.objects
        ],
    )


@router.get("/objects", response_model=list[ObjectKindResponse])
def objects(engine: WorldEngine = Depends(get_engine)) -> list[ObjectKindResponse]:  # noqa: B008
    return [
        ObjectKindResponse(
            key=kind.key,
            name=kind.name,
            material=kind.material,
            mass=kind.mass,
            bulk=kind.bulk,
            height=kind.height,
            solid=kind.solid,
            surface=kind.surface,
            openable=kind.openable,
            container_capacity=kind.container.capacity if kind.container is not None else None,
        )
        for kind in engine.catalog
    ]


@router.get("/menu", response_model=MenuResponse)
def menu(
    x: float = Query(...),
    y: float = Query(...),
    z: int = Query(0),
    engine: WorldEngine = Depends(get_engine),  # noqa: B008
) -> MenuResponse:
    # The client only plays Niko, so menus are computed for him.
    payload = engine.menu(PLAYER_ID, x, y, z)
    return MenuResponse(
        x=payload.x, y=payload.y, z=payload.z, target=payload.target, ops=list(payload.entries)
    )
