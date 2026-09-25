"""Export the goldens the C# sim must reproduce (Dev-025 stage 1).

Writes ``sim/EtherBound.Sim.Tests/Goldens/``: generated worlds, populations, raw RNG draws,
scripted engine scenarios and menus. Every file is a pure function of the code, so running the
export twice gives identical bytes. Usage: ``uv run python scripts/export_goldens.py [out_dir]``.
"""

import asyncio
import hashlib
import json
import shutil
import sys
from pathlib import Path
from typing import Any

from pydantic import TypeAdapter
from sqlalchemy import create_engine, select
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Actor, Base, WallIntegrity, WorldMeta
from etherbound.db.models import Chunk as ChunkRow
from etherbound.db.models import ChunkLevel as ChunkLevelRow
from etherbound.db.models import Object as ObjectRow
from etherbound.engine.actions import Action
from etherbound.engine.payloads import MenuPayload
from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.minds.extras import ExtrasBrain
from etherbound.rng import RNGStreams
from etherbound.world.chunk import CHUNK_SIZE, ChunkLevel
from etherbound.world.gen.registry import GENERATORS
from etherbound.world.materials import MaterialRegistry

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT = ROOT / "sim" / "EtherBound.Sim.Tests" / "Goldens"
SEEDS = (0, 7, 1895070486)
RNG_SEEDS = (0, 7, 1895070486)
RNG_SYSTEMS = ("worldgen", "population", "extras:extra-001:0", "extras:extra-006:119")
ACTIONS: TypeAdapter[Action] = TypeAdapter(Action)
MENU: TypeAdapter[MenuPayload] = TypeAdapter(MenuPayload)
Step = dict[str, Any]


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write(out: Path, name: str, payload: object, *, compact: bool = False) -> None:
    # Bulk draws stay on one line; scenarios and menus are indented so a diff reads.
    if compact:
        text = json.dumps(payload, separators=(",", ":"), sort_keys=True) + "\n"
    else:
        text = json.dumps(payload, indent=1, sort_keys=True) + "\n"
    (out / name).write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {name} ({len(text) // 1024} KiB)")


# --- worlds ------------------------------------------------------------------------------------


def world_goldens(registry: MaterialRegistry) -> list[dict[str, Any]]:
    worlds: list[dict[str, Any]] = []
    for key in sorted(GENERATORS):
        spec = GENERATORS[key]
        for seed in SEEDS:
            options = spec.options()
            world = spec.generate(seed, options, registry)
            worlds.append(
                {
                    "generator": key,
                    "seed": seed,
                    "gen_version": world.gen_version,
                    "spawn": list(spec.spawn(seed, options)),
                    "world_spawn": list(world.spawn),
                    "blob_sha256": sha(world.blob_bytes()),
                    "chunks": {
                        f"{cx},{cy}": {
                            "ground_h": sha(chunk.ground_blob),
                            "surface_mat": sha(chunk.surface_blob),
                            "dug": sha(chunk.dug_blob),
                            "strata": [list(layer) for layer in chunk.strata],
                        }
                        for (cx, cy), chunk in sorted(world.chunks.items())
                    },
                    "levels": {
                        f"{cx},{cy},{z}": {
                            "floor_h": sha(level.floor_blob),
                            "floor_mat": sha(level.floor_mat_blob),
                            "wall_n": sha(level.wall_n_blob),
                            "wall_w": sha(level.wall_w_blob),
                            "edge_flags": sha(level.edge_flags_blob),
                            "flags": sha(level.flags_blob),
                        }
                        for (cx, cy, z), level in sorted(world.levels.items())
                    },
                    "objects": [
                        {
                            "kind": o.kind,
                            "x": o.x,
                            "y": o.y,
                            "h": o.h,
                            "quantity": o.quantity,
                            "open": o.open,
                            "parent": o.parent,
                        }
                        for o in world.objects
                    ],
                }
            )
    return worlds


# --- RNG ---------------------------------------------------------------------------------------


def rng_goldens() -> list[dict[str, Any]]:
    streams: list[dict[str, Any]] = []
    for seed in RNG_SEEDS:
        for system in RNG_SYSTEMS:

            def fresh(seed: int = seed, system: str = system):
                return RNGStreams(seed).stream(system)

            rng = fresh()
            random = [rng.random() for _ in range(1000)]
            rng = fresh()
            randint = [
                [a, b, rng.randint(a, b)]
                for a, b in ((0, 1), (1, 6), (-12, 12), (0, 1023), (0, 10**12))
                for _ in range(200)
            ]
            rng = fresh()
            uniform = [
                [a, b, rng.uniform(a, b)]
                for a, b in ((-1.0, 1.0), (-12.5, 30.0))
                for _ in range(500)
            ]
            rng = fresh()
            shuffle: list[list[int]] = []
            for n in (2, 5, 17, 64, 256):
                for _ in range(20):
                    values = list(range(n))
                    rng.shuffle(values)
                    shuffle.append(values)
            rng = fresh()
            sample = [
                [n, k, rng.sample(list(range(n)), k)]
                for n, k in ((20, 3), (100, 10), (7, 7), (48, 6))
                for _ in range(25)
            ]
            streams.append(
                {
                    "seed": seed,
                    "system": system,
                    "random": random,
                    "randint": randint,
                    "uniform": uniform,
                    "shuffle": shuffle,
                    "sample": sample,
                }
            )
    return streams


# --- engine helpers ----------------------------------------------------------------------------


def memory_sessions() -> sessionmaker[Session]:
    database = create_engine("sqlite:///:memory:", connect_args={"check_same_thread": False})
    Base.metadata.create_all(database)
    return sessionmaker(bind=database, expire_on_commit=False, class_=Session)


def state_dump(engine: WorldEngine) -> dict[str, Any]:
    """Everything a scenario can change, in primary-key order."""
    with engine.sessions() as session:
        meta = session.get(WorldMeta, 1)
        assert meta is not None
        chunk_bytes = b""
        revisions: dict[str, int] = {}
        for row in session.scalars(select(ChunkRow).order_by(ChunkRow.cx, ChunkRow.cy)):
            chunk_bytes += row.ground_h + row.surface_mat + (row.dug or b"")
            if row.revision:
                revisions[f"{row.cx},{row.cy}"] = row.revision
        level_order = (ChunkLevelRow.cx, ChunkLevelRow.cy, ChunkLevelRow.z)
        for row in session.scalars(select(ChunkLevelRow).order_by(*level_order)):
            chunk_bytes += row.floor_h + row.floor_mat + row.wall_n + row.wall_w
            chunk_bytes += row.edge_flags + row.flags
        return {
            "world": {
                "seed": meta.seed,
                "game_minute": meta.game_minute,
                "speed": meta.speed,
                "paused": meta.paused,
                "gen_version": meta.gen_version,
                "generator": meta.generator,
                "gen_options": meta.gen_options,
            },
            "actors": [
                {
                    "id": a.id,
                    "kind": a.kind,
                    "name": a.name,
                    "x": a.x,
                    "y": a.y,
                    "z": a.z,
                    "h": a.h,
                    "mass_kg": a.mass_kg,
                    "activity": a.activity,
                    "mind": a.mind,
                }
                for a in session.scalars(select(Actor).order_by(Actor.id))
            ],
            "objects": [
                {
                    "id": o.id,
                    "kind": o.kind,
                    "loc": o.loc,
                    "x": o.x,
                    "y": o.y,
                    "h": o.h,
                    "cx": o.cx,
                    "cy": o.cy,
                    "container_id": o.container_id,
                    "actor_id": o.actor_id,
                    "slot": o.slot,
                    "quantity": o.quantity,
                    "state": o.state,
                    "integrity": o.integrity,
                    "owner": o.owner,
                }
                for o in session.scalars(select(ObjectRow).order_by(ObjectRow.id))
            ],
            "wall_integrity": [
                [w.cx, w.cy, w.z, w.cell_index, w.edge, w.integrity]
                for w in session.scalars(select(WallIntegrity))
            ],
            "chunk_revisions": revisions,
            "blocks_sha256": sha(chunk_bytes),
        }


def events_dump(engine: WorldEngine, after: int) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    while True:
        page = engine.read_events(after, 500)
        if not page:
            return rows
        for row in page:
            rows.append(
                {
                    "seq": row.seq,
                    "game_minute": row.game_minute,
                    "type": row.type,
                    "actor_id": row.actor_id,
                    "data": row.data,
                }
            )
        after = page[-1].seq


def place(engine: WorldEngine, actor_id: str, x: int, y: int, h: int | None = None) -> None:
    ground = engine.grid.ground_at(x, y)
    assert ground is not None
    with engine.sessions() as session:
        actor = session.get(Actor, actor_id)
        assert actor is not None
        actor.x, actor.y = x + 0.5, y + 0.5
        actor.h = ground[0] if h is None else h
        actor.z = actor.h // 6
        actor.activity = None
        session.commit()


def spawn(engine: WorldEngine, step: Step) -> int:
    with engine.sessions() as session:
        if step.get("held"):
            row = ObjectRow(
                kind=step["kind"],
                loc="held",
                actor_id=PLAYER_ID,
                slot=step["held"],
                quantity=step.get("quantity", 1),
                state={},
            )
        else:
            x, y = step["x"], step["y"]
            ground = engine.grid.ground_at(x, y)
            assert ground is not None
            row = ObjectRow(
                kind=step["kind"],
                loc="tile",
                x=x,
                y=y,
                h=step.get("h", ground[0]),
                cx=x // CHUNK_SIZE,
                cy=y // CHUNK_SIZE,
                quantity=step.get("quantity", 1),
                state=step.get("state", {}),
            )
        session.add(row)
        session.commit()
        object_id = row.id
    # The engine indexes tile objects per chunk; reload so the new row is visible to the grid.
    with engine.sessions() as session:
        engine._load_object_index(session)  # pyright: ignore[reportPrivateUsage]
        engine._refresh_load(session)  # pyright: ignore[reportPrivateUsage]
    return object_id


def set_wall(engine: WorldEngine, step: Step) -> None:
    from dataclasses import replace

    x, y, edge = step["x"], step["y"], step["edge"]
    cx, cy = x // CHUNK_SIZE, y // CHUNK_SIZE
    level = engine.grid.level(cx, cy, 0) or ChunkLevel.empty(cx, cy, 0)
    index = level.index(x - cx * CHUNK_SIZE, y - cy * CHUNK_SIZE)
    values = list(level.wall_n if edge == "north" else level.wall_w)
    values[index] = engine.registry[step["material"]].id
    updated = replace(
        level,
        wall_n=tuple(values) if edge == "north" else level.wall_n,
        wall_w=tuple(values) if edge == "west" else level.wall_w,
    )
    engine.grid.add_level(updated)
    with engine.sessions() as session:
        row = session.get(ChunkLevelRow, (cx, cy, 0))
        if row is None:
            row = ChunkLevelRow(cx=cx, cy=cy, z=0)
            session.add(row)
        row.floor_h = updated.floor_blob
        row.floor_mat = updated.floor_mat_blob
        row.wall_n = updated.wall_n_blob
        row.wall_w = updated.wall_w_blob
        row.edge_flags = updated.edge_flags_blob
        row.flags = updated.flags_blob
        session.commit()


# --- scenarios ---------------------------------------------------------------------------------

P = (123, 128)


def move(dx: float, dy: float, n: int) -> list[Step]:
    return [{"do": "submit", "action": {"op": "move", "dx": dx, "dy": dy}}] * n


def obj(ref: str) -> dict[str, Any]:
    return {"kind": "object", "id": f"${ref}"}


SCENARIOS: dict[str, list[Step]] = {
    "walk": [{"do": "place", "x": P[0], "y": P[1]}, *move(1, 0, 40), *move(0.6, 0.8, 30)],
    "walk-stairs": [
        {"do": "place", "x": 137, "y": 150, "h": 12},
        *move(1, 0, 100),
        *move(-1, 0, 100),
        {"do": "submit", "action": {"op": "move", "dx": 1, "dy": 0}, "delta": 10},
    ],
    "dig": [
        {"do": "place", "x": P[0], "y": P[1]},
        {"do": "spawn", "ref": "shovel", "kind": "shovel", "held": "right"},
        {"do": "submit", "action": {"op": "dig", "target": "$ground:124,128"}},
        {"do": "tick", "n": 40},
        {"do": "submit", "action": {"op": "dig", "target": "$ground:124,128"}},
        {"do": "tick", "n": 40},
    ],
    "handling": [
        {"do": "place", "x": P[0], "y": P[1]},
        {"do": "spawn", "ref": "chest", "kind": "chest", "x": 124, "y": 128},
        {"do": "spawn", "ref": "apple", "kind": "apple", "x": 123, "y": 129},
        {"do": "spawn", "ref": "stack", "kind": "bottle", "x": 123, "y": 128, "quantity": 3},
        {"do": "spawn", "ref": "pack", "kind": "backpack", "x": 122, "y": 128},
        {"do": "submit", "action": {"op": "take", "target": obj("apple")}},
        {"do": "submit", "action": {"op": "open", "target": obj("chest")}},
        {"do": "submit", "action": {"op": "put", "target": obj("apple"), "into": obj("chest")}},
        {"do": "submit", "action": {"op": "close", "target": obj("chest")}},
        {"do": "submit", "action": {"op": "take", "target": obj("pack")}},
        {"do": "submit", "action": {"op": "wear", "target": obj("pack")}},
        {"do": "submit", "action": {"op": "open", "target": obj("chest")}},
        {"do": "submit", "action": {"op": "take", "target": obj("apple")}},
        {"do": "submit", "action": {"op": "drop", "target": obj("apple")}},
        {"do": "submit", "action": {"op": "remove", "target": obj("pack")}},
        {"do": "submit", "action": {"op": "drop", "target": obj("pack")}},
        {"do": "submit", "action": {"op": "inspect", "target": obj("chest")}},
        {"do": "submit", "action": {"op": "take", "target": obj("chest")}},
        {"do": "submit", "action": {"op": "take", "target": obj("stack")}},
        {"do": "submit", "action": {"op": "drop", "target": "$held:bottle"}},
    ],
    "push": [
        {"do": "place", "x": P[0], "y": P[1]},
        {"do": "spawn", "ref": "apple", "kind": "apple", "x": 124, "y": 128},
        {"do": "spawn", "ref": "chest", "kind": "chest", "x": 123, "y": 129},
        {"do": "wall", "x": 123, "y": 131, "edge": "north", "material": "brick"},
        {"do": "submit", "action": {"op": "push", "target": obj("apple"), "dx": 1, "dy": 0}},
        {"do": "submit", "action": {"op": "push", "target": obj("chest"), "dx": 0, "dy": 1}},
        {"do": "place", "x": 123, "y": 129},
        {"do": "submit", "action": {"op": "pull", "target": obj("chest"), "dx": 0, "dy": -1}},
        {"do": "submit", "action": {"op": "drag", "target": obj("chest"), "dx": 0, "dy": 1}},
    ],
    "throw": [
        {"do": "place", "x": P[0], "y": P[1]},
        {"do": "spawn", "ref": "bottle", "kind": "bottle", "held": "right"},
        {"do": "spawn", "ref": "apple", "kind": "apple", "held": "left"},
        {"do": "submit", "action": {"op": "throw", "target": obj("bottle"), "dx": 1, "dy": 0}},
        {"do": "submit", "action": {"op": "throw", "target": obj("apple"), "dx": 0, "dy": 1}},
    ],
    "hit-break": [
        {"do": "place", "x": P[0], "y": P[1]},
        {"do": "spawn", "ref": "hammer", "kind": "sledgehammer", "held": "right"},
        {"do": "spawn", "ref": "chest", "kind": "chest", "x": 123, "y": 129},
        {"do": "wall", "x": 124, "y": 128, "edge": "west", "material": "brick"},
        {"do": "submit", "action": {"op": "hit", "target": obj("chest")}},
        {"do": "submit", "action": {"op": "hit", "target": obj("chest"), "tool": obj("hammer")}},
        *[
            {
                "do": "submit",
                "action": {
                    "op": "break",
                    "target": {"kind": "edge", "x": 124, "y": 128, "z": 0, "direction": "west"},
                    "tool": obj("hammer"),
                },
            }
        ]
        * 8,
        *move(1, 0, 30),
    ],
    "climb": [
        {"do": "place", "x": P[0], "y": P[1]},
        {"do": "spawn", "ref": "chest", "kind": "chest", "x": 124, "y": 128},
        {"do": "submit", "action": {"op": "climb", "target": "$top:124,128,chest"}},
        {"do": "tick", "n": 2},
        {"do": "submit", "action": {"op": "climb", "target": "$ground:123,128"}},
        {"do": "tick", "n": 2},
    ],
    "wait": [
        {"do": "place", "x": P[0], "y": P[1]},
        {"do": "submit", "action": {"op": "wait"}},
        {"do": "tick", "n": 5},
        *move(1, 0, 3),
        {"do": "submit", "action": {"op": "wait"}},
        {"do": "tick", "n": 16},
    ],
    "extras": [{"do": "brain"}, {"do": "tick", "n": 120}],
    "extras-x10": [
        {"do": "brain", "time_scale": 0.1},
        {"do": "clock", "speed": 10},
        {"do": "tick", "n": 120},
    ],
}


async def run_scenario(steps: list[Step]) -> dict[str, Any]:
    engine = WorldEngine(memory_sessions())
    engine.ensure_world(0)
    start = events_dump(engine, 0)
    after = start[-1]["seq"] if start else 0
    refs: dict[str, int] = {}
    results: list[dict[str, Any]] = []

    def resolve(value: Any) -> Any:
        if isinstance(value, dict):
            return {k: resolve(v) for k, v in value.items()}  # pyright: ignore[reportUnknownVariableType]
        if isinstance(value, str) and value.startswith("$ground:"):
            x, y = (int(part) for part in value[8:].split(","))
            ground = engine.grid.ground_at(x, y)
            assert ground is not None
            return {"kind": "tile", "x": x, "y": y, "h": ground[0]}
        if isinstance(value, str) and value.startswith("$top:"):
            x, y, ref = value[5:].split(",")
            with engine.sessions() as session:
                row = session.get(ObjectRow, refs[ref])
                assert row is not None and row.h is not None
                height = engine.catalog[row.kind].height
            return {"kind": "tile", "x": int(x), "y": int(y), "h": row.h + height}
        if isinstance(value, str) and value.startswith("$held:"):
            with engine.sessions() as session:
                held = session.scalars(
                    select(ObjectRow)
                    .where(ObjectRow.loc == "held", ObjectRow.kind == value[6:])
                    .order_by(ObjectRow.id)
                ).first()
                assert held is not None
            return {"kind": "object", "id": held.id}
        if isinstance(value, str) and value.startswith("$"):
            return refs[value[1:]]
        return value

    for step in steps:
        kind = step["do"]
        if kind == "place":
            place(engine, PLAYER_ID, step["x"], step["y"], step.get("h"))
        elif kind == "spawn":
            refs[step["ref"]] = spawn(engine, step)
        elif kind == "wall":
            set_wall(engine, step)
        elif kind == "brain":
            ExtrasBrain(engine, time_scale=step.get("time_scale", 1.0)).subscribe(engine.bus)
        elif kind == "clock":
            await engine.set_clock(speed=step["speed"])
        elif kind == "tick":
            for _ in range(step["n"]):
                await engine.advance_time()
        elif kind == "submit":
            action = ACTIONS.validate_python(resolve(step["action"]))
            result = await engine.submit(
                step.get("actor", PLAYER_ID), action, step.get("delta", 0.05)
            )
            results.append(result.model_dump(mode="json"))
        else:
            raise ValueError(kind)
    return {
        "steps": steps,
        "refs": refs,
        "results": results,
        "events": events_dump(engine, after),
        "state": state_dump(engine),
    }


async def population_goldens() -> list[dict[str, Any]]:
    populations: list[dict[str, Any]] = []
    for key in sorted(GENERATORS):
        for seed in SEEDS:
            engine = WorldEngine(memory_sessions())
            engine.ensure_world(seed)
            if key != "test":
                await engine.new_game(seed, key)
            with engine.sessions() as session:
                extras = [
                    {"id": a.id, "name": a.name, "x": a.x, "y": a.y, "h": a.h, "mind": a.mind}
                    for a in session.scalars(select(Actor).order_by(Actor.id))
                    if a.kind == "extra"
                ]
            populations.append({"generator": key, "seed": seed, "extras": extras})
    return populations


async def menu_goldens() -> list[dict[str, Any]]:
    menus: list[dict[str, Any]] = []
    # Dev-016 tiles on seed 0, with Niko standing next to each.
    engine = WorldEngine(memory_sessions())
    engine.ensure_world(0)
    for label, stand, tile in (
        ("table", (136, 133, 12), (137, 133)),
        ("stacked-chests", (125, 127, None), (126, 127)),
        ("closed-chest", (123, 127, None), (124, 127)),
        ("spawn", (121, 128, None), (121, 128)),
    ):
        place(engine, PLAYER_ID, stand[0], stand[1], stand[2])
        for radius in (0, 1):
            with engine.sessions() as session:
                niko = session.get(Actor, PLAYER_ID)
                assert niko is not None
                z = niko.z
            payload = engine.menu(PLAYER_ID, tile[0], tile[1], z, radius)
            menus.append(
                {
                    "world": "test-0",
                    "label": label,
                    "radius": radius,
                    "menu": MENU.dump_python(payload, mode="json"),
                }
            )
    # Lab bays: Niko at each bay's centre.
    engine = WorldEngine(memory_sessions())
    engine.ensure_world(0)
    await engine.new_game(0, "lab")
    for bay in GENERATORS["lab"].bays:
        x, y = bay.x + bay.width // 2, bay.y + bay.height // 2
        surfaces = engine.grid.standing_surfaces(x, y)
        place(engine, PLAYER_ID, x, y, surfaces[0].h if surfaces else None)
        with engine.sessions() as session:
            niko = session.get(Actor, PLAYER_ID)
            assert niko is not None
            z = niko.z
        for radius in (0, 1):
            payload = engine.menu(PLAYER_ID, x, y, z, radius)
            menus.append(
                {
                    "world": "lab-0",
                    "label": bay.key,
                    "radius": radius,
                    "menu": MENU.dump_python(payload, mode="json"),
                }
            )
    return menus


async def export(out: Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    registry = MaterialRegistry.load()
    write(out, "worlds.json", world_goldens(registry), compact=True)
    write(out, "rng.json", rng_goldens(), compact=True)
    write(out, "populations.json", await population_goldens())
    write(out, "menus.json", await menu_goldens())
    scenarios = {name: await run_scenario(steps) for name, steps in SCENARIOS.items()}
    for name, scenario in scenarios.items():
        accepted = sum(1 for result in scenario["results"] if result["accepted"])
        print(
            f"  {name}: {accepted}/{len(scenario['results'])} accepted, {len(scenario['events'])} events"
        )
    write(out, "scenarios.json", scenarios)
    shutil.copyfile(ROOT / "server" / "tests" / "world-parity.json", out / "world-parity.json")


def main() -> None:
    asyncio.run(export(Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_OUT))


if __name__ == "__main__":
    main()
