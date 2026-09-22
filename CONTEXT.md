# CONTEXT.md

Current technical context of EtherBound. It records what exists, how it runs and which pitfalls
have been measured. Design lives in `docs/VISION.md`; `Dev-001` (archived in `docs/done/`)
specifies the Phase 0 skeleton this file describes.

## Modules

| Module | Path | Responsibility |
|---|---|---|
| server.app | `server/src/etherbound/app.py`, `config.py`, `routes/api.py` | FastAPI app, lifespan (migrations, clock, schema export), REST routes |
| server.clock | `server/src/etherbound/clock.py` | 1 Hz logic clock: speeds x1/x3/x10, pause, autopause locks |
| server.engine | `server/src/etherbound/engine/` | World engine: the only writer of state. `world.py` (validation, commit), `actions.py` (Action API), `movement.py` (continuous movement + collision) |
| server.net | `server/src/etherbound/net/` | WS hub, Pydantic messages, combined OpenAPI + WS schema export |
| server.db | `server/src/etherbound/db/`, `server/alembic/` | SQLAlchemy models, engine/session factory, Alembic upgrade on start |
| server.rng | `server/src/etherbound/rng.py` | `RNGStreams.stream(system)` — one seeded stream per system |
| server.testmap | `server/src/etherbound/testmap.py` | Throwaway 64x64 collision map; Phase 1 deletes it |
| web.game | `web/src/game/` | Phaser map scene, placeholder Niko, camera, WASD, right-click targeting |
| web.net | `web/src/net/` | WS client, prediction/reconciliation, generated `schema.d.ts`, protocol types |
| web.ui | `web/src/ui/` | React overlay: clock, speeds, pills, meters, feed, input, context menu |
| tooling | `scripts/`, `start.bat`, `stop.bat` | Dev launcher and teardown, Windows-safe |

## Data model

SQLite at `data/etherbound.db` (gitignored). Created by Alembic migration `0001_initial`:

| Table | Columns |
|---|---|
| `world_meta` | `id` (pk, always 1), `seed`, `game_minute`, `speed` (1/3/10), `paused` |
| `actor` | `id` (pk), `kind` (`player`), `x`, `y`, `z` |

`world_meta` is a singleton row: game time, speed and pause persist, so a restart resumes where it
stopped. `POST /api/game/new` wipes game rows and reseeds through the engine, never through DDL.
The only current actor is `niko`, starting at `(2.0, 2.0, z=0)`.

## Contracts

- **Action API.** `WorldEngine.submit(actor_id, action)` is the single mutation entry point.
  Phase 0 has one verb: `MoveAction(dx, dy)`. The player's WASD input becomes this action over the
  WebSocket; no actor has a private path.
- **REST.** `GET /api/health`, `POST /api/game/new {seed}`, `GET /api/game/state`,
  `GET /api/menu?x&y&z` (Phase 0 always returns `["inspect"]`; the client renders, never adds).
- **WebSocket `/ws`.** Server → client: `snapshot` (on connect), `tick`, `ack`, `error`.
  Client → server: `input` (dx, dy, sequence), `clock` (paused, speed). Movement is 20 Hz and
  independent of the 1 Hz game clock. Pause rejects movement with `accepted=false, reason="paused"`.
- **Types.** `combined_schema()` merges OpenAPI with the WS message models and writes
  `server/schema.json`; `web/src/net/schema.d.ts` is generated from it and committed.
- **Emission.** The engine only writes state. The clock listener syncs the `Clock` object; the
  edges (tick callback, WS handler, `new_game` route) broadcast `tick`/`snapshot` explicitly, so a
  state change is never emitted twice.

## Commands

```text
# Run everything (one launcher window, logs in logs/)
start.bat          # server + web, background, output to logs\server.log and logs\web.log
stop.bat           # kills the whole process tree, including the uvicorn reloader child

# Or from the root
npm run dev        # spawns server + web in this terminal
npm run stop

# Schema and types
cd server && uv run etherbound-schema      # writes server/schema.json
cd web && npm run gen:types                # server/schema.json -> src/net/schema.d.ts

# Checks
cd server && uv run ruff check && uv run ruff format --check && uv run pyright && uv run pytest
cd web && npm run gen:types && git diff --exit-code src/net/schema.d.ts && npm run build
```

## Measured pitfalls

- **Uvicorn `--reload` spawns a child process.** Stopping only the parent leaves an orphan holding
  port 8000 and the database file. `scripts/stop.ps1` kills the process tree with
  `taskkill /PID <id> /T /F` and then frees ports 8000 and 5173.
- **The database file stays locked while the server runs.** Stop the server before touching
  `data/etherbound.db`.
- **`schema.json` is not committed.** It is generated (`uv run etherbound-schema`); `gen:types`
  fails if the file is missing. Only `schema.d.ts` is committed.
- **Windows launcher quoting.** Paths contain spaces, so `start.bat` delegates to
  `scripts\run-server.bat` / `scripts\run-web.bat` instead of quoting commands inside `start`.
- **Vite prints its banner to stderr**, so launcher redirection uses `2>&1`.
- **Pytest warnings are third-party** (FastAPI/Starlette/pytest-asyncio deprecations), not project
  issues. `pytest` is 7 tests, all passing.

## Not yet present

Phase 1 items: real world model (chunks, heightmap, floors, underground), the eight primitives,
event bus, witnesses/knowledge, tile physics, NPCs, LLM, Jev. `server/testmap.py` and the
placeholder squares go away then.
