import json
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from etherbound.app import create_app
from etherbound.config import Settings


def test_rest_and_websocket_protocol(tmp_path: Path) -> None:
    settings = Settings(
        database_url=f"sqlite:///{(tmp_path / 'api.db').as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=10,
    )
    with TestClient(create_app(settings)) as client:
        health = client.get("/api/health")
        assert health.status_code == 200
        assert health.json()["status"] == "ok"
        assert client.get("/api/menu", params={"x": 2, "y": 2, "z": 0}).json()["verbs"] == [
            "inspect"
        ]
        with client.websocket_connect("/ws") as websocket:
            snapshot = websocket.receive_json()
            assert snapshot["type"] == "snapshot"
            assert snapshot["world"]["chunk_size"] == 32
            assert "h" in snapshot["actors"][0]
            chunk_types = {websocket.receive_json()["type"] for _ in range(25)}
            assert chunk_types == {"chunk"}
            websocket.send_json({"type": "input", "sequence": 7, "dx": 1, "dy": 0})
            ack = websocket.receive_json()
            while ack["type"] == "chunk":
                ack = websocket.receive_json()
            assert ack["type"] == "ack"
            assert ack["sequence"] == 7
            assert "h" in ack
        event_response = client.get("/api/events", params={"limit": 500})
        assert event_response.status_code == 200
        assert event_response.json()["events"]
        assert client.get("/api/events", params={"limit": 0}).status_code == 422
        assert client.get("/api/events", params={"limit": 501}).status_code == 422
        assert client.get("/api/events", params={"type": "actor.spawned"}).json()["events"]
        schema = json.loads((tmp_path / "schema.json").read_text(encoding="utf-8"))
        assert "InputMessage" in schema["components"]["schemas"]
        assert "x-etherbound-websocket-messages" in schema


def test_event_filters_and_new_game_websocket_refresh(tmp_path: Path) -> None:
    settings = Settings(
        database_url=f"sqlite:///{(tmp_path / 'events.db').as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=10,
    )
    with TestClient(create_app(settings)) as client, client.websocket_connect("/ws") as websocket:
        assert websocket.receive_json()["type"] == "snapshot"
        for _ in range(25):
            assert websocket.receive_json()["type"] == "chunk"

        initial = client.get("/api/events").json()["events"]
        assert [(event["seq"], event["type"]) for event in initial] == [
            (1, "world.generated"),
            (2, "actor.spawned"),
            (3, "clock.changed"),
        ]
        assert (
            client.get("/api/events", params={"after_seq": 1, "actor_id": "niko"}).json()["events"][
                0
            ]["type"]
            == "actor.spawned"
        )
        assert (
            client.get(
                "/api/events", params={"type": "world.generated", "actor_id": "niko"}
            ).json()["events"]
            == []
        )

        response = client.post("/api/game/new", json={"seed": 7})
        assert response.status_code == 200
        assert websocket.receive_json()["type"] == "snapshot"
        for _ in range(25):
            assert websocket.receive_json()["type"] == "chunk"
        events = client.get("/api/events").json()["events"]
        assert [(event["seq"], event["type"]) for event in events] == [
            (1, "world.generated"),
            (2, "actor.spawned"),
            (3, "clock.changed"),
        ]


def test_websocket_clock_change_is_broadcast_by_subscriber_once(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    settings = Settings(
        database_url=f"sqlite:///{(tmp_path / 'clock-events.db').as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=10,
    )
    with TestClient(create_app(settings)) as client, client.websocket_connect("/ws") as websocket:
        assert websocket.receive_json()["type"] == "snapshot"
        for _ in range(25):
            assert websocket.receive_json()["type"] == "chunk"
        hub = client.app.state.hub
        broadcast = hub.broadcast
        tick_broadcasts: list[object] = []

        async def track_broadcast(message: object) -> None:
            if getattr(message, "type", None) == "tick":
                tick_broadcasts.append(message)
            await broadcast(message)

        monkeypatch.setattr(hub, "broadcast", track_broadcast)
        changed_before = client.get("/api/events", params={"type": "clock.changed"}).json()[
            "events"
        ]
        websocket.send_json({"type": "clock", "paused": True})
        tick = websocket.receive_json()
        assert tick["type"] == "tick"
        assert tick["paused"] is True
        changed = client.get("/api/events", params={"type": "clock.changed"}).json()["events"]
        assert len(changed) == len(changed_before) + 1
        assert len(tick_broadcasts) == 1
