import json
import logging
import sys
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from uvicorn.logging import DefaultFormatter

from etherbound.clock import Clock
from etherbound.config import Settings, get_settings
from etherbound.db.migrate import upgrade
from etherbound.db.session import make_engine, make_session_factory
from etherbound.engine.world import WorldEngine
from etherbound.events import (
    ActivityFinished,
    ChunkChanged,
    ClockChanged,
    ClockTicked,
    Event,
    EventBus,
    WorldGenerated,
)
from etherbound.net.schema import export_schema
from etherbound.net.ws import WebSocketHub
from etherbound.routes.api import router

event_logger = logging.getLogger("etherbound.events")


def configure_logging() -> None:
    logger = logging.getLogger("etherbound")
    if not any(getattr(handler, "_etherbound", False) for handler in logger.handlers):
        handler = logging.StreamHandler(sys.stderr)
        handler.setFormatter(DefaultFormatter("%(levelprefix)s %(message)s", use_colors=False))
        handler.__dict__["_etherbound"] = True
        logger.addHandler(handler)
    logger.setLevel(logging.INFO)
    logger.propagate = False


def log_event(event: Event) -> None:
    if not event.logged:
        return
    data = event.model_dump(mode="json", exclude={"seq", "game_minute", "type", "actor_id"})
    event_logger.info(
        "event %d %s %s %s",
        event.seq,
        event.type,
        event.actor_id or "-",
        json.dumps(data, separators=(",", ":")),
    )


def create_app(settings: Settings | None = None) -> FastAPI:
    configure_logging()
    config = settings or get_settings()
    database_url = config.resolved_database_url()
    database_engine = make_engine(database_url)
    sessions = make_session_factory(database_engine)
    bus = EventBus()
    world_engine = WorldEngine(sessions, bus=bus)
    hub = WebSocketHub(world_engine)

    async def on_tick() -> None:
        await world_engine.advance_time()

    clock = Clock(config.time_scale, on_tick=on_tick)
    bus.subscribe(
        ClockChanged,
        lambda event: clock.load(speed=event.speed, paused=event.paused),
        name="clock",
    )
    bus.subscribe(ClockTicked, hub.on_clock, name="websocket.clock_ticked")
    bus.subscribe(ClockChanged, hub.on_clock, name="websocket.clock_changed")
    bus.subscribe(WorldGenerated, hub.on_world_generated, name="websocket.world_generated")
    bus.subscribe(ChunkChanged, hub.on_chunk_changed, name="websocket.chunk_changed")
    bus.subscribe(ActivityFinished, hub.on_activity_finished, name="websocket.activity_finished")

    bus.subscribe(Event, log_event, name="log")

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        upgrade(database_url)
        world_engine.ensure_world()
        await world_engine.bus.drain()
        _, speed, paused = world_engine.clock_state()
        clock.load(speed=speed, paused=paused)
        app.state.engine = world_engine
        app.state.clock = clock
        app.state.hub = hub
        export_schema(app, _schema_path(config.schema_path))
        await clock.start()
        try:
            yield
        finally:
            await clock.stop()
            await hub.close_all()
            database_engine.dispose()

    app = FastAPI(title="EtherBound", version="0.0.1", lifespan=lifespan)
    app.include_router(router)

    @app.websocket("/ws")
    async def websocket_endpoint(websocket: WebSocket) -> None:
        await hub.connect(websocket)
        try:
            while True:
                await hub.handle(websocket, await websocket.receive_json())
        except WebSocketDisconnect:
            hub.disconnect(websocket)

    return app


def _schema_path(path: Path) -> Path:
    if path.is_absolute():
        return path
    return Path(__file__).resolve().parents[2] / path


app = create_app()
