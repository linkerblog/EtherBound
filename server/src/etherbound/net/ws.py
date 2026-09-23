from collections.abc import Iterable
from math import floor

from fastapi import WebSocket
from pydantic import TypeAdapter, ValidationError

from etherbound.engine.actions import ActivityState, MoveAction
from etherbound.engine.world import PLAYER_ID, ChunkPayload, WorldEngine, WorldInfo, WorldState
from etherbound.events.models import (
    ActivityFinished,
    ChunkChanged,
    ClockChanged,
    ClockTicked,
    WorldGenerated,
)
from etherbound.net.messages import (
    AckMessage,
    ActionMessage,
    ActivityMessage,
    ActivitySnapshot,
    ActorSnapshot,
    ChunkLevelMessage,
    ChunkMessage,
    ClientMessage,
    ErrorMessage,
    InputMessage,
    ObjectMessage,
    ResultMessage,
    ServerMessage,
    SnapshotMessage,
    TickMessage,
)
from etherbound.net.messages import (
    WorldInfo as WorldInfoMessage,
)
from etherbound.world.chunk import CHUNK_SIZE

client_message_adapter: TypeAdapter[ClientMessage] = TypeAdapter(ClientMessage)


def _activity(activity: ActivityState | None) -> ActivitySnapshot | None:
    if activity is None:
        return None
    return ActivitySnapshot(
        op=activity.op,
        started_minute=activity.started_minute,
        ends_minute=activity.ends_minute,
    )


def _actors(state: WorldState) -> list[ActorSnapshot]:
    return [
        ActorSnapshot(
            id=actor.id,
            kind=actor.kind,
            x=actor.x,
            y=actor.y,
            z=actor.z,
            h=actor.h,
            activity=_activity(actor.activity),
            carried=list(actor.carried),
            load_kg=actor.load_kg,
        )
        for actor in state.actors
    ]


def _world(info: WorldInfo) -> WorldInfoMessage:
    return WorldInfoMessage(
        chunk_size=info.chunk_size, level_h=info.level_h, bounds=list(info.bounds)
    )


def snapshot(state: WorldState, world: WorldInfo) -> SnapshotMessage:
    return SnapshotMessage(
        type="snapshot",
        seed=state.seed,
        game_minute=state.game_minute,
        speed=state.speed,
        paused=state.paused,
        actors=_actors(state),
        world=_world(world),
        gen_version=state.gen_version,
    )


def tick(state: WorldState, world: WorldInfo) -> TickMessage:
    return TickMessage(
        type="tick",
        game_minute=state.game_minute,
        speed=state.speed,
        paused=state.paused,
        actors=_actors(state),
        world=_world(world),
        gen_version=state.gen_version,
    )


def chunk_message(payload: ChunkPayload) -> ChunkMessage:
    return ChunkMessage(
        type="chunk",
        cx=payload.cx,
        cy=payload.cy,
        revision=payload.revision,
        ground_h=list(payload.ground_h),
        surface_mat=list(payload.surface_mat),
        levels=[
            ChunkLevelMessage(
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
            ObjectMessage(
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


class WebSocketHub:
    def __init__(self, engine: WorldEngine) -> None:
        self.engine = engine
        self.connections: set[WebSocket] = set()
        self._known_chunks: dict[WebSocket, dict[tuple[int, int], int]] = {}
        self._known_centres: dict[WebSocket, tuple[int, int]] = {}

    @staticmethod
    def _position_chunk(x: float, y: float) -> tuple[int, int]:
        return floor(x) // CHUNK_SIZE, floor(y) // CHUNK_SIZE

    def _player_chunk(self) -> tuple[int, int]:
        for actor in self.engine.get_state().actors:
            if actor.id == PLAYER_ID:
                return self._position_chunk(actor.x, actor.y)
        return 0, 0

    async def _push_chunks(
        self, websocket: WebSocket, centre: tuple[int, int], radius: int = 2
    ) -> None:
        known = self._known_chunks.setdefault(websocket, {})
        for payload in self.engine.chunks_near(centre[0], centre[1], radius):
            key = (payload.cx, payload.cy)
            if known.get(key) == payload.revision:
                continue
            await websocket.send_json(chunk_message(payload).model_dump(mode="json"))
            known[key] = payload.revision
        self._known_centres[websocket] = centre

    async def reset_world(self) -> None:
        for websocket in tuple(self.connections):
            self._known_chunks[websocket] = {}
            await self._push_chunks(websocket, self._player_chunk())

    async def connect(self, websocket: WebSocket) -> None:
        await websocket.accept()
        self.connections.add(websocket)
        await websocket.send_json(
            snapshot(self.engine.get_state(), self.engine.world_info()).model_dump(mode="json")
        )
        await self._push_chunks(websocket, self._player_chunk())

    def disconnect(self, websocket: WebSocket) -> None:
        self.connections.discard(websocket)
        self._known_chunks.pop(websocket, None)
        self._known_centres.pop(websocket, None)

    async def broadcast(self, message: ServerMessage) -> None:
        payload = message.model_dump(mode="json")
        stale: list[WebSocket] = []
        for connection in tuple(self.connections):
            try:
                await connection.send_json(payload)
            except Exception:
                stale.append(connection)
        for connection in stale:
            self.disconnect(connection)

    async def on_clock(self, event: ClockTicked | ClockChanged) -> None:
        del event
        await self.broadcast(tick(self.engine.get_state(), self.engine.world_info()))

    async def on_world_generated(self, event: WorldGenerated) -> None:
        del event
        await self.broadcast(snapshot(self.engine.get_state(), self.engine.world_info()))
        await self.reset_world()

    async def on_chunk_changed(self, event: ChunkChanged) -> None:
        payload = self.engine.chunk_payload(event.cx, event.cy)
        if payload is None:
            return
        key = (event.cx, event.cy)
        for websocket in tuple(self.connections):
            known = self._known_chunks.get(websocket, {})
            # Only connections that already hold the chunk; the rest get it when they walk near,
            # and a revision a connection knows is never replaced by an older one.
            if key not in known or known[key] >= payload.revision:
                continue
            try:
                await websocket.send_json(chunk_message(payload).model_dump(mode="json"))
            except Exception:
                self.disconnect(websocket)
                continue
            known[key] = payload.revision

    async def on_activity_finished(self, event: ActivityFinished) -> None:
        if event.actor_id != PLAYER_ID:
            return
        await self.broadcast(
            ActivityMessage(
                type="activity",
                actor_id=event.actor_id,
                op=event.op,
                outcome=event.outcome,
                reason=event.reason,
            )
        )

    async def handle(self, websocket: WebSocket, message_data: object) -> None:
        try:
            message = client_message_adapter.validate_python(message_data)
        except ValidationError as error:
            await self.broadcast_to_one(websocket, ErrorMessage(type="error", message=str(error)))
            return
        if isinstance(message, InputMessage):
            result = await self.engine.submit(
                PLAYER_ID,
                MoveAction(dx=message.dx, dy=message.dy),
                delta_seconds=message.dt,
            )
            await self.broadcast_to_one(
                websocket,
                AckMessage(
                    type="ack",
                    sequence=message.sequence,
                    accepted=result.accepted,
                    actor_id=result.actor_id,
                    x=result.x,
                    y=result.y,
                    z=result.z,
                    h=result.h,
                    reason=result.reason,
                ),
            )
            centre = self._position_chunk(result.x, result.y)
            if self._known_centres.get(websocket) != centre:
                await self._push_chunks(websocket, centre)
            return
        if isinstance(message, ActionMessage):
            result = await self.engine.submit(PLAYER_ID, message.action)
            await self.broadcast_to_one(
                websocket,
                ResultMessage(
                    type="result",
                    sequence=message.sequence,
                    accepted=result.accepted,
                    reason=result.reason,
                    text=result.text,
                    activity=_activity(result.activity),
                    carried=result.carried,
                    load_kg=result.load_kg,
                ),
            )
            return
        await self.engine.set_clock(paused=message.paused, speed=message.speed)

    async def broadcast_to_one(self, websocket: WebSocket, message: ServerMessage) -> None:
        await websocket.send_json(message.model_dump(mode="json"))

    async def close_all(self, connections: Iterable[WebSocket] | None = None) -> None:
        for connection in tuple(connections or self.connections):
            await connection.close()
            self.disconnect(connection)
