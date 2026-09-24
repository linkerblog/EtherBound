import tomllib
from collections.abc import Iterable, Mapping
from dataclasses import dataclass
from pathlib import Path
from typing import cast

from etherbound.world.materials import MaterialRegistry

OBJECTS_PATH = Path(__file__).with_name("objects.toml")

WEARABLE_SLOTS = frozenset({"back"})

_KIND_KEYS = frozenset(
    {
        "key",
        "name",
        "material",
        "mass",
        "bulk",
        "height",
        "solid",
        "surface",
        "fixed",
        "stackable",
        "container",
        "openable",
        "wearable",
        "tool",
    }
)
_COMPONENT_KEYS: dict[str, frozenset[str]] = {
    "container": frozenset({"capacity"}),
    "openable": frozenset(),
    "wearable": frozenset({"slot"}),
    "tool": frozenset({"dig", "strike_speed_m_s"}),
}


@dataclass(frozen=True, slots=True)
class Container:
    capacity: float


@dataclass(frozen=True, slots=True)
class Wearable:
    slot: str


@dataclass(frozen=True, slots=True)
class Tool:
    dig: float | None = None
    strike_speed_m_s: float | None = None


@dataclass(frozen=True, slots=True)
class ObjectKind:
    key: str
    name: str
    material: str
    mass: float
    bulk: float
    height: int
    solid: bool
    surface: bool
    fixed: bool
    stackable: bool
    container: Container | None = None
    openable: bool = False
    wearable: Wearable | None = None
    tool: Tool | None = None


def total_bulk(kind: ObjectKind, quantity: int) -> float:
    """Litres of ``quantity`` of a kind; contents do not add bulk."""
    return kind.bulk * quantity


def two_handed(kind: ObjectKind) -> bool:
    """More than 25 l takes both hands."""
    return kind.bulk > 25.0


def total_mass(kind: ObjectKind, quantity: int, contents: float = 0.0) -> float:
    """Kilograms of ``quantity`` of a kind plus the mass of its contents."""
    return kind.mass * quantity + contents


class ObjectCatalog:
    """Static object-kind definitions, loaded and validated from ``objects.toml``."""

    def __init__(self, kinds: Iterable[ObjectKind]) -> None:
        values = tuple(kinds)
        if not values:
            raise ValueError("at least one object kind is required")
        if len({kind.key for kind in values}) != len(values):
            raise ValueError("object kind keys must be unique")
        self.kinds = values
        self._by_key = {kind.key: kind for kind in values}

    @classmethod
    def from_file(
        cls, path: str | Path | None = None, registry: MaterialRegistry | None = None
    ) -> ObjectCatalog:
        source = Path(path) if path is not None else OBJECTS_PATH
        with source.open("rb") as file:
            document = tomllib.load(file)
        definitions = document.get("kinds")
        if not isinstance(definitions, list):
            raise ValueError("objects.toml must contain a kinds array")
        registry = registry or MaterialRegistry.load()
        return cls(
            _kind(cast(dict[str, object], value), registry)
            for value in cast(list[object], definitions)
        )

    load = from_file

    def __len__(self) -> int:
        return len(self.kinds)

    def __iter__(self):
        return iter(self.kinds)

    def __getitem__(self, key: str) -> ObjectKind:
        return self._by_key[key]

    def get(self, key: str, default: ObjectKind | None = None) -> ObjectKind | None:
        return self._by_key.get(key, default)

    def keys(self) -> tuple[str, ...]:
        return tuple(kind.key for kind in self.kinds)


def _table(raw: dict[str, object], name: str, key: str) -> dict[str, object] | None:
    value = raw.get(name)
    if value is None:
        return None
    if not isinstance(value, dict):
        raise ValueError(f"object kind {key}: {name} must be a table")
    table = cast(dict[str, object], value)
    unknown = sorted(set(table) - _COMPONENT_KEYS[name])
    if unknown:
        raise ValueError(f"object kind {key}: unknown {name} keys {', '.join(unknown)}")
    return table


def _kind(raw: dict[str, object], registry: MaterialRegistry) -> ObjectKind:
    unknown = sorted(set(raw) - _KIND_KEYS)
    key = str(raw.get("key", ""))
    if unknown:
        raise ValueError(f"object kind {key}: unknown keys {', '.join(unknown)}")
    if not key:
        raise ValueError("object kind key must not be empty")

    material = str(raw["material"])
    if registry.get(material) is None:
        raise ValueError(f"object kind {key}: unregistered material {material!r}")

    solid = bool(raw.get("solid", False))
    surface = bool(raw.get("surface", False))
    stackable = bool(raw.get("stackable", False))
    height = int(cast(int, raw.get("height", 0)))

    if solid != (height > 0):
        raise ValueError(f"object kind {key}: height must be positive exactly when solid")
    if surface and not solid:
        raise ValueError(f"object kind {key}: surface requires solid")
    if surface and not _walkable_material(registry, material):
        raise ValueError(f"object kind {key}: a surface material must be walkable")
    if stackable and (solid or raw.get("container") is not None or raw.get("openable") is not None):
        raise ValueError(f"object kind {key}: stackable excludes solid, container and openable")

    container_table = _table(raw, "container", key)
    container = (
        Container(capacity=_positive(container_table, "capacity", key, "container"))
        if container_table is not None
        else None
    )

    openable_table = _table(raw, "openable", key)
    openable = openable_table is not None
    if openable and container is None:
        raise ValueError(f"object kind {key}: openable requires container")

    wearable_table = _table(raw, "wearable", key)
    wearable = None
    if wearable_table is not None:
        slot = str(wearable_table.get("slot", ""))
        if slot not in WEARABLE_SLOTS:
            raise ValueError(
                f"object kind {key}: wearable slot must be one of {sorted(WEARABLE_SLOTS)}"
            )
        wearable = Wearable(slot=slot)

    tool_table = _table(raw, "tool", key)
    tool = None
    if tool_table is not None:
        dig = _optional_positive(tool_table, "dig", key, "tool")
        strike_speed = _optional_positive(tool_table, "strike_speed_m_s", key, "tool")
        if dig is None and strike_speed is None:
            raise ValueError(f"object kind {key}: tool requires a capability")
        tool = Tool(dig=dig, strike_speed_m_s=strike_speed)

    return ObjectKind(
        key=key,
        name=str(raw["name"]),
        material=material,
        mass=float(cast(float, raw["mass"])),
        bulk=float(cast(float, raw["bulk"])),
        height=height,
        solid=solid,
        surface=surface,
        fixed=bool(raw.get("fixed", False)),
        stackable=stackable,
        container=container,
        openable=openable,
        wearable=wearable,
        tool=tool,
    )


def _positive(table: Mapping[str, object], field: str, key: str, component: str) -> float:
    value = float(cast(float, table[field]))
    if value <= 0:
        raise ValueError(f"object kind {key}: {component}.{field} must be positive")
    return value


def _optional_positive(
    table: Mapping[str, object], field: str, key: str, component: str
) -> float | None:
    if field not in table:
        return None
    return _positive(table, field, key, component)


def _walkable_material(registry: MaterialRegistry, material: str) -> bool:
    resolved = registry.get(material)
    return resolved is not None and resolved.walkable
