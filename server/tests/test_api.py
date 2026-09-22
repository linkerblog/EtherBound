import json
from pathlib import Path

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
        schema = json.loads((tmp_path / "schema.json").read_text(encoding="utf-8"))
        assert "InputMessage" in schema["components"]["schemas"]
        assert "x-etherbound-websocket-messages" in schema
