from fastapi import APIRouter, Depends, HTTPException, Query, Request
from pydantic import BaseModel, Field

from etherbound.engine.world import WorldEngine
from etherbound.net.ws import WebSocketHub, snapshot

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


class StateResponse(BaseModel):
    seed: int
    game_minute: int
    speed: int
    paused: bool
    actors: list[ActorResponse]


class MenuResponse(BaseModel):
    x: float
    y: float
    z: int
    verbs: list[str]


def get_engine(request: Request) -> WorldEngine:
    return request.app.state.engine


def get_hub(request: Request) -> WebSocketHub:
    return request.app.state.hub


def state_response(engine: WorldEngine) -> StateResponse:
    state = engine.get_state()
    return StateResponse(
        seed=state.seed,
        game_minute=state.game_minute,
        speed=state.speed,
        paused=state.paused,
        actors=[
            ActorResponse(id=actor.id, kind=actor.kind, x=actor.x, y=actor.y, z=actor.z)
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
    hub: WebSocketHub = Depends(get_hub),  # noqa: B008
) -> StateResponse:
    state = await engine.new_game(body.seed)
    await hub.broadcast(snapshot(state))
    return state_response(engine)


@router.get("/game/state", response_model=StateResponse)
def game_state(engine: WorldEngine = Depends(get_engine)) -> StateResponse:  # noqa: B008
    return state_response(engine)


@router.get("/menu", response_model=MenuResponse)
def menu(
    x: float = Query(...),
    y: float = Query(...),
    z: int = Query(0),
    engine: WorldEngine = Depends(get_engine),  # noqa: B008
) -> MenuResponse:
    if z != 0:
        raise HTTPException(status_code=400, detail="only z=0 exists in phase 0")
    return MenuResponse(x=x, y=y, z=z, verbs=["inspect"])
