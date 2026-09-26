"""Write a save made by the Python server at 0008_extra for the C# SaveCompatibility test.

The save holds a dug tile, a damaged wall, carried and moved objects and Extras with goals, so
every table has rows. Next to it, ``python-0008.json`` is the state and event log the C# sim must
read back. Usage: ``uv run python scripts/export_save_fixture.py``.
"""

import asyncio
import json
from pathlib import Path

from export_goldens import ACTIONS, events_dump, place, set_wall, spawn, state_dump
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.migrate import upgrade
from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.minds.extras import ExtrasBrain

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "sim" / "EtherBound.Sim.Tests" / "Fixtures"


async def build(path: Path) -> dict[str, object]:
    if path.exists():
        path.unlink()
    url = f"sqlite:///{path.as_posix()}"
    upgrade(url)
    database = create_engine(url, connect_args={"check_same_thread": False})
    sessions = sessionmaker(bind=database, expire_on_commit=False, class_=Session)
    engine = WorldEngine(sessions)
    engine.ensure_world(0)
    ExtrasBrain(engine).subscribe(engine.bus)
    place(engine, PLAYER_ID, 123, 128)
    shovel = spawn(engine, {"kind": "shovel", "held": "right"})
    chest = spawn(engine, {"kind": "chest", "x": 122, "y": 130})
    set_wall(engine, {"x": 124, "y": 130, "edge": "west", "material": "brick"})
    ground = engine.grid.ground_at(124, 128)
    assert ground is not None

    async def submit(action: dict[str, object]) -> None:
        result = await engine.submit(PLAYER_ID, ACTIONS.validate_python(action), 0.05)
        assert result.accepted, (action, result.reason)

    tool = {"kind": "object", "id": shovel}
    await submit({"op": "dig", "target": {"kind": "tile", "x": 124, "y": 128, "h": ground[0]}})
    for _ in range(40):
        await engine.advance_time()
    place(engine, PLAYER_ID, 123, 130)
    edge = {"kind": "edge", "x": 124, "y": 130, "z": 0, "direction": "west"}
    await submit({"op": "break", "target": edge, "tool": tool})
    await submit({"op": "hit", "target": {"kind": "object", "id": chest}, "tool": tool})
    await submit({"op": "move", "dx": 0.0, "dy": -1.0})
    for _ in range(20):
        await engine.advance_time()
    database.dispose()
    return {"state": state_dump(engine), "events": events_dump(engine, 0)}


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    summary = asyncio.run(build(OUT / "python-0008.db"))
    text = json.dumps(summary, indent=1, sort_keys=True) + "\n"
    (OUT / "python-0008.json").write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {OUT / 'python-0008.db'} and python-0008.json")


if __name__ == "__main__":
    main()
