import json
from pathlib import Path
from typing import Any

from fastapi import FastAPI

from etherbound.net.messages import (
    AckMessage,
    ActionMessage,
    ActivityMessage,
    ChunkMessage,
    ClockMessage,
    ErrorMessage,
    InputMessage,
    ResultMessage,
    SnapshotMessage,
    TickMessage,
)

WS_MODELS = (
    InputMessage,
    ClockMessage,
    ActionMessage,
    AckMessage,
    ResultMessage,
    ActivityMessage,
    ErrorMessage,
    SnapshotMessage,
    TickMessage,
    ChunkMessage,
)


def combined_schema(app: FastAPI) -> dict[str, Any]:
    schema = app.openapi()
    components = schema.setdefault("components", {}).setdefault("schemas", {})
    for model in WS_MODELS:
        model_schema = model.model_json_schema(ref_template="#/components/schemas/{model}")
        definitions = model_schema.pop("$defs", {})
        components.update(definitions)
        components[model.__name__] = model_schema
    schema["x-etherbound-websocket-messages"] = {
        "client": [InputMessage.__name__, ClockMessage.__name__, ActionMessage.__name__],
        "server": [
            AckMessage.__name__,
            ResultMessage.__name__,
            ActivityMessage.__name__,
            ErrorMessage.__name__,
            SnapshotMessage.__name__,
            TickMessage.__name__,
        ],
    }
    return schema


def export_schema(app: FastAPI, path: Path) -> dict[str, Any]:
    schema = combined_schema(app)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(schema, indent=2) + "\n", encoding="utf-8")
    return schema


def main() -> None:
    from etherbound.app import create_app
    from etherbound.config import get_settings

    settings = get_settings()
    path = settings.schema_path
    if not path.is_absolute():
        path = Path(__file__).resolve().parents[3] / path
    export_schema(create_app(settings), path)
    print(f"schema written to {path}")
