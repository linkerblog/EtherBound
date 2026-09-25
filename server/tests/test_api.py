import io
import json
import logging
from collections.abc import Callable
from pathlib import Path
from typing import Any

import pytest
from fastapi.testclient import TestClient

from etherbound.app import create_app, log_event
from etherbound.config import Settings
from etherbound.engine.actions import MenuEntry
from etherbound.events.models import ClockTicked


def niko(actors: list[dict[str, Any]]) -> dict[str, Any]:
    return next(actor for actor in actors if actor["id"] == "niko")


def test_rest_and_websocket_protocol(tmp_path: Path) -> None:
    settings = Settings(
        database_url=f"sqlite:///{(tmp_path / 'api.db').as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=1000,
    )
    with TestClient(create_app(settings)) as client:
        health = client.get("/api/health")
        assert health.status_code == 200
        assert health.json()["status"] == "ok"
        entries = client.get("/api/menu", params={"x": 2, "y": 2, "z": 0}).json()["ops"]
        assert [MenuEntry.model_validate(entry).op for entry in entries][:1] == ["inspect"]
        objects = client.get("/api/objects")
        assert objects.status_code == 200
        kinds = {kind["key"]: kind for kind in objects.json()}
        assert kinds["chest"]["container_capacity"] == 100.0
        assert kinds["chest"]["solid"] is True
        with client.websocket_connect("/ws") as websocket:
            snapshot = websocket.receive_json()
            assert snapshot["type"] == "snapshot"
            assert snapshot["world"]["chunk_size"] == 32
            assert "h" in snapshot["actors"][0]
            assert "carried" in snapshot["actors"][0]
            assert "load_kg" in snapshot["actors"][0]
            chunks = [websocket.receive_json() for _ in range(25)]
            assert {chunk["type"] for chunk in chunks} == {"chunk"}
            assert any("objects" in chunk for chunk in chunks)
            assert all(isinstance(chunk.get("objects"), list) for chunk in chunks)
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


def test_websocket_input_duration_validation_and_event_logging(
    tmp_path: Path,
) -> None:
    settings = Settings(
        database_url=f"sqlite:///{(tmp_path / 'timed-input.db').as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=1000,
    )
    app = create_app(settings)
    with TestClient(app) as client, client.websocket_connect("/ws") as websocket:

        def until(predicate: Callable[[dict[str, Any]], bool]) -> dict[str, Any]:
            for _ in range(1000):
                message = websocket.receive_json()
                if predicate(message):
                    return message
            raise AssertionError("message never arrived")

        snapshot = until(lambda message: message["type"] == "snapshot")
        for _ in range(25):
            assert websocket.receive_json()["type"] == "chunk"
        start_x = niko(snapshot["actors"])["x"]

        logger = logging.getLogger("etherbound")
        handler = next(
            handler
            for handler in logger.handlers
            if isinstance(handler, logging.StreamHandler) and getattr(handler, "_etherbound", False)
        )
        original_stream = handler.stream
        output = io.StringIO()
        handler.setStream(output)
        try:
            websocket.send_json({"type": "input", "sequence": 1, "dx": 1, "dy": 0, "dt": 0.05})
            short = until(lambda message: message.get("sequence") == 1)
            assert short["type"] == "ack" and short["accepted"]
            short_distance = short["x"] - start_x

            websocket.send_json({"type": "input", "sequence": 2, "dx": 1, "dy": 0, "dt": 0.1})
            long = until(lambda message: message.get("sequence") == 2)
            assert long["type"] == "ack" and long["accepted"]
            long_distance = long["x"] - short["x"]
            assert long_distance == pytest.approx(short_distance * 2, abs=0.02)

            websocket.send_json({"type": "input", "sequence": 3, "dx": 1, "dy": 0})
            defaulted = until(lambda message: message.get("sequence") == 3)
            assert defaulted["type"] == "ack" and defaulted["accepted"]
            assert defaulted["x"] - long["x"] == pytest.approx(short_distance, abs=0.02)

            for sequence in range(4, 9):
                websocket.send_json(
                    {"type": "input", "sequence": sequence, "dx": 1, "dy": 0, "dt": 0.1}
                )
                assert (
                    until(lambda message, seq=sequence: message.get("sequence") == seq)["type"]
                    == "ack"
                )
            log_event(ClockTicked())
            log_output = output.getvalue()
            assert "event " in log_output and "actor.moved niko" in log_output
            assert "clock.ticked" not in log_output
        finally:
            handler.setStream(original_stream)

        position = niko(client.get("/api/game/state").json()["actors"])["x"]
        for sequence, dt in ((9, 0), (10, 0.2)):
            websocket.send_json({"type": "input", "sequence": sequence, "dx": 1, "dy": 0, "dt": dt})
            assert until(lambda message: message["type"] == "error")["type"] == "error"
            assert niko(client.get("/api/game/state").json()["actors"])["x"] == position


def test_create_app_configures_only_one_tagged_logging_handler(tmp_path: Path) -> None:
    settings = Settings(
        database_url=f"sqlite:///{(tmp_path / 'logging.db').as_posix()}",
        schema_path=tmp_path / "schema.json",
    )

    create_app(settings)
    create_app(settings)

    handlers = [
        handler
        for handler in logging.getLogger("etherbound").handlers
        if getattr(handler, "_etherbound", False)
    ]
    assert len(handlers) == 1


def test_event_filters_and_new_game_websocket_refresh(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    settings = Settings(
        database_url=f"sqlite:///{(tmp_path / 'events.db').as_posix()}",
        schema_path=tmp_path / "schema.json",
        # A slow clock: no tick disturbs the event log while the test asserts on it.
        time_scale=1000,
    )
    app = create_app(settings)
    with TestClient(app) as client, client.websocket_connect("/ws") as websocket:
        assert websocket.receive_json()["type"] == "snapshot"
        for _ in range(25):
            assert websocket.receive_json()["type"] == "chunk"

        initial = client.get("/api/events").json()["events"]
        types = [event["type"] for event in initial]
        assert types[0] == "world.generated"
        assert types[-1] == "clock.changed"
        assert types[1:-1] == ["actor.spawned"] * 7
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
        snapshot = websocket.receive_json()
        assert snapshot["type"] == "snapshot"
        assert snapshot["seed"] == 7
        for _ in range(25):
            chunk = websocket.receive_json()
            assert chunk["type"] == "chunk"
            assert chunk["revision"] == 0
        events = client.get("/api/events").json()["events"]
        types = [event["type"] for event in events]
        assert types[0] == "world.generated"
        assert types[-1] == "clock.changed"
        assert types[1:-1] == ["actor.spawned"] * 7

        engine = app.state.engine
        get_state = engine.get_state
        state_reads = 0

        def count_state_reads():
            nonlocal state_reads
            state_reads += 1
            return get_state()

        monkeypatch.setattr(engine, "get_state", count_state_reads)
        pushed_after_crossing = 0
        for sequence in range(200):
            websocket.send_json({"type": "input", "sequence": sequence, "dx": 0, "dy": 1})
            message = websocket.receive_json()
            while message["type"] != "ack":
                if message["type"] == "chunk":
                    pushed_after_crossing += 1
                message = websocket.receive_json()
            assert message["sequence"] == sequence
        assert pushed_after_crossing == 5
        assert state_reads == 0


def test_websocket_clock_change_is_broadcast_by_subscriber_once(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    settings = Settings(
        database_url=f"sqlite:///{(tmp_path / 'clock-events.db').as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=1000,
    )
    app = create_app(settings)
    with TestClient(app) as client, client.websocket_connect("/ws") as websocket:
        assert websocket.receive_json()["type"] == "snapshot"
        for _ in range(25):
            assert websocket.receive_json()["type"] == "chunk"
        hub = app.state.hub
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


def test_websocket_action_result_activity_and_chunk_push(tmp_path: Path) -> None:
    settings = Settings(
        database_url=f"sqlite:///{(tmp_path / 'ops.db').as_posix()}",
        schema_path=tmp_path / "schema.json",
        # A fast clock, so a 24-minute dig finishes in about half a second.
        time_scale=0.02,
    )
    with TestClient(create_app(settings)) as client, client.websocket_connect("/ws") as websocket:

        def until(predicate: Callable[[dict[str, Any]], bool]) -> list[dict[str, Any]]:
            seen: list[dict[str, Any]] = []
            for _ in range(5000):
                message = websocket.receive_json()
                seen.append(message)
                if predicate(message):
                    return seen
            raise AssertionError("message never arrived")

        until(lambda message: message["type"] == "snapshot")
        # Walk east from the road spawn until Niko stands next to the grass at x = 124.
        x = 121.5
        for sequence in range(40):
            websocket.send_json({"type": "input", "sequence": sequence, "dx": 1, "dy": 0})
            ack = until(lambda m, s=sequence: m["type"] == "ack" and m["sequence"] == s)[-1]
            x = ack["x"]
            if x >= 123.3:
                break
        assert 123.3 <= x < 124
        websocket.send_json({"type": "input", "sequence": 99, "dx": 0, "dy": 0})

        entries = client.get("/api/menu", params={"x": 124.5, "y": 128.5, "z": 0}).json()["ops"]
        dig = next(MenuEntry.model_validate(entry) for entry in entries if entry["op"] == "dig")
        assert dig.available
        websocket.send_json(
            {"type": "action", "sequence": 100, "action": dig.action.model_dump(mode="json")}
        )
        result = until(lambda m: m["type"] == "result")[-1]
        assert (result["sequence"], result["accepted"]) == (100, True)
        assert result["activity"]["op"] == "dig"
        until(
            lambda m: (
                m["type"] == "tick" and (niko(m["actors"]).get("activity") or {}).get("op") == "dig"
            )
        )

        seen = until(lambda m: m["type"] == "activity")
        assert (seen[-1]["op"], seen[-1]["outcome"]) == ("dig", "completed")
        pushed = [m for m in seen if m["type"] == "chunk" and (m["cx"], m["cy"]) == (3, 4)]
        assert [m["revision"] for m in pushed] == [1]


def test_menu_target_names_material_and_elevation(tmp_path: Path) -> None:
    database = tmp_path / "menu.db"
    settings = Settings(
        database_url=f"sqlite:///{database.as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=1000,
    )
    with TestClient(create_app(settings)) as client:
        payload = client.get("/api/menu", params={"x": 121.5, "y": 128.5, "z": 0}).json()
        assert payload["target"].startswith("Asphalt")
        assert "1 m" in payload["target"]
        # Niko stands on this road tile: asphalt is never dug, and waiting targets himself.
        assert [(entry["op"], entry["available"]) for entry in payload["ops"]] == [
            ("wait", True),
            ("inspect", True),
        ]


def test_crossing_a_chunk_boundary_pushes_only_new_chunks(tmp_path: Path) -> None:
    database = tmp_path / "push.db"
    settings = Settings(
        database_url=f"sqlite:///{database.as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=1000,
    )
    with TestClient(create_app(settings)) as client:
        with client.websocket_connect("/ws") as websocket:
            assert websocket.receive_json()["type"] == "snapshot"
            first_chunks = [websocket.receive_json()["type"] for _ in range(25)]
            assert set(first_chunks) == {"chunk"}

            def walk(sequence: int, dx: float, dy: float) -> int:
                """Send one input, drain any chunk messages, return 1 when chunks appeared."""
                websocket.send_json({"type": "input", "sequence": sequence, "dx": dx, "dy": dy})
                message = websocket.receive_json()
                pushed = 0
                while message["type"] == "chunk":
                    pushed += 1
                    message = websocket.receive_json()
                assert message["type"] == "ack"
                assert message["sequence"] == sequence
                return pushed

            pushed = 0
            for step in range(1, 41):
                pushed += walk(step, 1, 0)
            # Walking east 8 m from the road crosses exactly one chunk boundary.
            assert pushed == 5
            pushed += walk(100, 0, 0)
            assert pushed == 5  # the stationary input pushes nothing new


def test_generators_endpoint_and_new_game_options(tmp_path: Path) -> None:
    settings = Settings(
        database_url=f"sqlite:///{(tmp_path / 'gen.db').as_posix()}",
        schema_path=tmp_path / "schema.json",
        time_scale=1000,
    )
    with TestClient(create_app(settings)) as client:
        generators = {info["key"]: info for info in client.get("/api/gen").json()}
        assert set(generators) == {"test", "lab"}
        fields = {field["path"]: field for field in generators["lab"]["fields"]}
        assert fields["relief.amplitude"]["group"] == "relief"
        assert fields["relief.amplitude"]["min"] == 0
        assert fields["relief.amplitude"]["max"] == 24
        assert fields["feature"]["choices"] == ["none", "relief"]
        bays = generators["lab"]["bays"]
        assert len(bays) == 9
        assert all(bay["width"] == 36 and bay["height"] == 36 for bay in bays)
        assert generators["test"]["bays"] == []

        default = client.post("/api/game/new", json={"seed": 3})
        assert default.status_code == 200
        assert default.json()["generator"] == "test"

        custom = client.post(
            "/api/game/new",
            json={
                "seed": 5,
                "generator": "lab",
                "options": {"feature": "relief", "relief": {"amplitude": 20}},
            },
        )
        assert custom.status_code == 200
        state = custom.json()
        assert state["generator"] == "lab"
        assert state["gen_version"] == 2
        assert state["gen_options"]["relief"]["amplitude"] == 20

        before = client.get("/api/game/state").json()
        invalid = client.post(
            "/api/game/new",
            json={"seed": 5, "generator": "lab", "options": {"relief": {"amplitude": 999}}},
        )
        assert invalid.status_code == 422
        assert client.get("/api/game/state").json() == before

        unknown = client.post("/api/game/new", json={"seed": 5, "generator": "nope"})
        assert unknown.status_code == 422
        assert client.get("/api/game/state").json() == before
