from collections.abc import Iterable

from fastapi import WebSocket
from pydantic import TypeAdapter, ValidationError

from etherbound.engine.actions import MoveAction
from etherbound.engine.world import PLAYER_ID, WorldEngine, WorldState
from etherbound.net.messages import (
    AckMessage,
    ActorSnapshot,
    ClientMessage,
    ErrorMessage,
    InputMessage,
    ServerMessage,
    SnapshotMessage,
    TickMessage,
)

client_message_adapter: TypeAdapter[ClientMessage] = TypeAdapter(ClientMessage)


def _actors(state: WorldState) -> list[ActorSnapshot]:
    return [
        ActorSnapshot(id=actor.id, kind=actor.kind, x=actor.x, y=actor.y, z=actor.z)
        for actor in state.actors
    ]


def snapshot(state: WorldState) -> SnapshotMessage:
    return SnapshotMessage(
        type="snapshot",
        seed=state.seed,
        game_minute=state.game_minute,
        speed=state.speed,
        paused=state.paused,
        actors=_actors(state),
    )


def tick(state: WorldState) -> TickMessage:
    return TickMessage(
        type="tick",
        game_minute=state.game_minute,
        speed=state.speed,
        paused=state.paused,
        actors=_actors(state),
    )


class WebSocketHub:
    def __init__(self, engine: WorldEngine) -> None:
        self.engine = engine
        self.connections: set[WebSocket] = set()

    async def connect(self, websocket: WebSocket) -> None:
        await websocket.accept()
        self.connections.add(websocket)
        await websocket.send_json(snapshot(self.engine.get_state()).model_dump(mode="json"))

    def disconnect(self, websocket: WebSocket) -> None:
        self.connections.discard(websocket)

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

    async def handle(self, websocket: WebSocket, message_data: object) -> None:
        try:
            message = client_message_adapter.validate_python(message_data)
        except ValidationError as error:
            await self.broadcast_to_one(websocket, ErrorMessage(type="error", message=str(error)))
            return
        if isinstance(message, InputMessage):
            result = await self.engine.submit(PLAYER_ID, MoveAction(dx=message.dx, dy=message.dy))
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
                    reason=result.reason,
                ),
            )
            return
        state = await self.engine.set_clock(paused=message.paused, speed=message.speed)
        await self.broadcast(tick(state))

    async def broadcast_to_one(self, websocket: WebSocket, message: ServerMessage) -> None:
        await websocket.send_json(message.model_dump(mode="json"))

    async def close_all(self, connections: Iterable[WebSocket] | None = None) -> None:
        for connection in tuple(connections or self.connections):
            await connection.close()
            self.disconnect(connection)
