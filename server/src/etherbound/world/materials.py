import tomllib
from collections.abc import Iterable, Mapping
from dataclasses import dataclass
from pathlib import Path
from typing import cast


@dataclass(frozen=True, slots=True)
class Material:
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
    tags: tuple[str, ...] = ()
    id: int = 0


class MaterialRegistry:
    """Static material definitions with save-compatible, append-only ids."""

    def __init__(self, materials: Iterable[Material]) -> None:
        values = tuple(materials)
        if not values:
            raise ValueError("at least one material is required")
        if len({material.key for material in values}) != len(values):
            raise ValueError("material keys must be unique")
        ids = [material.id for material in values]
        if any(identifier <= 0 for identifier in ids) or len(set(ids)) != len(ids):
            raise ValueError("material ids must be unique positive integers")
        self.materials = values
        self._by_key = {material.key: material for material in values}
        self._by_id = {material.id: material for material in values}

    @classmethod
    def from_file(
        cls, path: str | Path | None = None, existing_ids: Mapping[str, int] | None = None
    ) -> MaterialRegistry:
        source = Path(path) if path is not None else Path(__file__).with_name("materials.toml")
        with source.open("rb") as file:
            document = tomllib.load(file)
        definitions = document.get("materials")
        if not isinstance(definitions, list):
            raise ValueError("materials.toml must contain a materials array")
        definitions = cast(list[object], definitions)
        used = dict(existing_ids or {})
        next_id = max(used.values(), default=0) + 1
        values: list[Material] = []
        for value in definitions:
            if not isinstance(value, dict):
                raise ValueError("each material must be a table")
            raw = cast(dict[str, object], value)
            key = str(raw["key"])
            identifier = used.get(key)
            if identifier is None:
                identifier = next_id
                next_id += 1
            values.append(
                Material(
                    key=key,
                    name=str(raw["name"]),
                    color=str(raw["color"]),
                    walkable=bool(raw["walkable"]),
                    walk_cost=float(cast(float, raw["walk_cost"])),
                    solid=bool(raw["solid"]),
                    blocks_sight=bool(raw["blocks_sight"]),
                    diggable=bool(raw["diggable"]),
                    dig_cost=float(cast(float, raw["dig_cost"])),
                    flammable=bool(raw["flammable"]),
                    density=float(cast(float, raw["density"])),
                    resistance=float(cast(float, raw["resistance"])),
                    liquid=bool(raw["liquid"]),
                    tags=tuple(str(tag) for tag in cast(list[object], raw.get("tags", []))),
                    id=identifier,
                )
            )
        return cls(values)

    load = from_file

    def __len__(self) -> int:
        return len(self.materials)

    def __iter__(self):
        return iter(self.materials)

    def __getitem__(self, key: str | int) -> Material:
        return self._by_key[key] if isinstance(key, str) else self._by_id[key]

    def get(self, key: str | int, default: Material | None = None) -> Material | None:
        if isinstance(key, str):
            return self._by_key.get(key, default)
        return self._by_id.get(key, default)

    def ids(self) -> dict[str, int]:
        return {material.key: material.id for material in self.materials}
