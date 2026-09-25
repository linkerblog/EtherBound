"""Dump generated worlds to JSON for the Godot look spike (Dev-025 stage 0).

Blobs stay in their stored little-endian layout, base64-encoded, so the reader decodes the same
bytes the database holds. Usage: ``uv run python scripts/export_world.py [out_dir]``.
"""

import base64
import json
import sys
import tomllib
from pathlib import Path
from typing import Any

from etherbound.world.gen.registry import GENERATORS
from etherbound.world.materials import MaterialRegistry

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT = ROOT / "game" / "spike" / "dumps"
OBJECTS_TOML = ROOT / "server" / "src" / "etherbound" / "world" / "objects.toml"
WORLDS = (("test", 7), ("lab", 7))


def _b64(blob: bytes) -> str:
    return base64.b64encode(blob).decode("ascii")


def _kinds() -> dict[str, dict[str, Any]]:
    with OBJECTS_TOML.open("rb") as handle:
        raw = tomllib.load(handle)
    return {
        kind["key"]: {
            "material": kind["material"],
            "height": kind["height"],
            "solid": bool(kind.get("solid", False)),
            "surface": bool(kind.get("surface", False)),
        }
        for kind in raw["kinds"]
    }


def export(key: str, seed: int, registry: MaterialRegistry) -> dict[str, Any]:
    spec = GENERATORS[key]
    world = spec.generate(seed, spec.options(), registry)
    spawn = spec.spawn(seed, spec.options())
    return {
        "generator": key,
        "seed": seed,
        "gen_version": world.gen_version,
        "spawn": {"x": spawn[0], "y": spawn[1], "h": spawn[2]},
        "materials": [
            {"id": m.id, "key": m.key, "color": m.color, "liquid": m.liquid} for m in registry
        ],
        "kinds": _kinds(),
        "chunks": [
            {
                "cx": chunk.cx,
                "cy": chunk.cy,
                "ground_h": _b64(chunk.ground_blob),
                "surface_mat": _b64(chunk.surface_blob),
                "dug": _b64(chunk.dug_blob),
            }
            for _, chunk in sorted(world.chunks.items())
        ],
        "levels": [
            {
                "cx": level.cx,
                "cy": level.cy,
                "z": level.z,
                "floor_h": _b64(level.floor_blob),
                "floor_mat": _b64(level.floor_mat_blob),
                "wall_n": _b64(level.wall_n_blob),
                "wall_w": _b64(level.wall_w_blob),
                "edge_flags": _b64(level.edge_flags_blob),
                "flags": _b64(level.flags_blob),
            }
            for _, level in sorted(world.levels.items())
        ],
        "objects": [
            {"kind": o.kind, "x": o.x, "y": o.y, "h": o.h, "parent": o.parent}
            for o in world.objects
        ],
    }


def main() -> None:
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_OUT
    out.mkdir(parents=True, exist_ok=True)
    registry = MaterialRegistry.load()
    for key, seed in WORLDS:
        path = out / f"{key}-{seed}.json"
        path.write_text(json.dumps(export(key, seed, registry), separators=(",", ":")))
        print(f"wrote {path}")


if __name__ == "__main__":
    main()
