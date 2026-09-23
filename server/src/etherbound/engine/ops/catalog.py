import re
import tomllib
from dataclasses import dataclass
from functools import cache
from pathlib import Path
from typing import cast

TARGET_KINDS = frozenset({"self", "tile", "edge", "object", "actor"})
_KEY = re.compile(r"^[a-z][a-z0-9_]*$")
CATALOG_PATH = Path(__file__).resolve().parents[1] / "ops.toml"


@dataclass(frozen=True, slots=True)
class OpSpec:
    key: str
    label: str
    group: str
    targets: tuple[str, ...]
    tags: tuple[str, ...]


def _strings(raw: dict[str, object], field: str, key: str) -> tuple[str, ...]:
    value = raw.get(field)
    if not isinstance(value, list):
        raise ValueError(f"op {key}: {field} must be a list")
    return tuple(str(entry) for entry in cast(list[object], value))


def load_catalog(path: Path = CATALOG_PATH) -> tuple[OpSpec, ...]:
    with path.open("rb") as file:
        document = tomllib.load(file)
    definitions = document.get("ops")
    if not isinstance(definitions, list):
        raise ValueError("ops.toml must contain an ops array")
    specs: list[OpSpec] = []
    seen: set[str] = set()
    for value in cast(list[object], definitions):
        if not isinstance(value, dict):
            raise ValueError("each op must be a table")
        raw = cast(dict[str, object], value)
        key = str(raw.get("key", ""))
        if not _KEY.match(key):
            raise ValueError(f"op key must be snake_case ASCII: {key!r}")
        if key in seen:
            raise ValueError(f"duplicate op key: {key}")
        seen.add(key)
        targets = _strings(raw, "targets", key)
        unknown = sorted(set(targets) - TARGET_KINDS)
        if unknown:
            raise ValueError(f"op {key}: unknown target kinds {', '.join(unknown)}")
        tags = _strings(raw, "tags", key)
        if not tags:
            raise ValueError(f"op {key}: tags must not be empty")
        specs.append(
            OpSpec(
                key=key,
                label=str(raw.get("label", key.capitalize())),
                group=str(raw["group"]),
                targets=targets,
                tags=tags,
            )
        )
    return tuple(specs)


@cache
def catalog() -> tuple[OpSpec, ...]:
    return load_catalog()


def spec_for(key: str) -> OpSpec | None:
    return next((spec for spec in catalog() if spec.key == key), None)
