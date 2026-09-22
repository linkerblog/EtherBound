# CONTEXT.md

Current technical context of EtherBound. It records what exists, how it runs and which pitfalls
have been measured. Design lives in `docs/VISION.md`; `Dev-001` (archived in `docs/done/`)
specifies the Phase 0 skeleton, `Dev-002` (archived in `docs/done/`) the world model described
here, and `Dev-003` (archived in `docs/done/`) the native launcher.

## Modules

| Module | Path | Responsibility |
|---|---|---|
| server.app | `server/src/etherbound/app.py`, `config.py`, `routes/api.py` | FastAPI app, lifespan (migrations, world, clock, schema export), REST routes |
| server.clock | `server/src/etherbound/clock.py` | 1 Hz logic clock: speeds x1/x3/x10, pause, autopause locks |
| server.engine | `server/src/etherbound/engine/` | World engine: the only writer of state. `world.py` (grid ownership, generation commit, actor state), `actions.py` (Action API), `movement.py` (`move_in_world`, sub-step standing rule, slope and material cost) |
| server.world | `server/src/etherbound/world/` | World data: `materials.py` + `materials.toml` (append-only registry), `chunk.py` (blobs), `grid.py` (solidity, edges, standing), `nav.py` (A*), `gen/` (seeded noise + test world) |
| server.net | `server/src/etherbound/net/` | WS hub with per-connection chunk tracking, Pydantic messages, combined OpenAPI + WS schema export |
| server.db | `server/src/etherbound/db/`, `server/alembic/` | SQLAlchemy models, engine/session factory, Alembic upgrade on start |
| server.rng | `server/src/etherbound/rng.py` | `RNGStreams.stream(system)` — one seeded stream per system (`worldgen` drives generation) |
| web.world | `web/src/world/` | `ChunkStore`, `rules.ts` (prediction mirror of the standing rule), `materials.ts` |
| web.game | `web/src/game/` | Phaser scene: chunk rendering (shading, cliffs, edge walls, openings, cutaway), camera bounds, integer zoom x1–x4 (wheel, `+`/`-`/`0`, saved per browser), WASD, right-click with data-driven `z` |
| web.net | `web/src/net/` | WS client, prediction/reconciliation with `h`, generated `schema.d.ts`, protocol types |
| web.ui | `web/src/ui/` | React overlay: clock, speeds, pills, meters, feed, input, context menu with server `target` line |
| launcher | `launcher/` | `EtherBound.exe`, the dev launcher (C#, .NET 10, Native AOT): starts server + web without shells, each in its own job inside a kill-on-close launcher job, health checks, hot reload by restart, leftover and port handling, UTF-8 logs |
| tooling | root config: `package.json`, `global.json`, `.gitignore`, `.env.example` | Build and check scripts, pinned .NET SDK |

## Data model

SQLite at `data/etherbound.db` (gitignored). Migrations `0001_initial` and `0002_world`:

| Table | Columns |
|---|---|
| `world_meta` | `id` (pk, always 1), `seed`, `game_minute`, `speed` (1/3/10), `paused`, `gen_version` |
| `actor` | `id` (pk), `kind` (`player`), `x`, `y`, `z` (derived `h // 6`), `h` (half-metres) |
| `material` | append-only `id` ↔ `key` mapping plus rendering/physics properties |
| `chunk` | pk `(cx, cy)`; blobs `ground_h` (int16×1024), `surface_mat` (uint16×1024), `strata` JSON, `revision`, `gen_version` |
| `chunk_level` | pk `(cx, cy, z)`; blobs `floor_h`, `floor_mat`, `wall_n`, `wall_w`, `edge_flags`, `flags` |

Spatial units: 1 m tiles in 32×32 chunks; `h` in half-metres; `z` is the absolute 3 m band
`floor(h / 6)`; walls live on tile edges (each tile owns north/west) with doorway/window edge
flags; below the surface everything is implicit strata until a `void` flag excavates it. The test
world (`gen_version = 2`) is 8×8 chunks with hills, a road (spawn at 121.5, 128.5, h=2), a terrace
with a ramp, a building with a graded approach, west doorway, internal stairs under a stairwell
hole, roof, basement void and a pond with a park pit. `new_game` wipes actors, chunks and levels
and regenerates from the seed; `ensure_world` fills an existing Phase 0 save without a wipe.

## Contracts

- **Action API.** `WorldEngine.submit(actor_id, action)` is the single mutation entry point.
  One verb: `MoveAction(dx, dy)`. WASD becomes this action over the WebSocket; climbing is `move`.
- **REST.** `GET /api/health`, `POST /api/game/new {seed}`, `GET /api/game/state`,
  `GET /api/materials`, `GET /api/world/chunk?cx&cy`, `GET /api/menu?x&y&z` (any `z`; returns a
  `target` line like `Asphalt · 1 m` and `["inspect"]`; the client renders, never adds).
- **WebSocket `/ws`.** Server → client: `snapshot` (with `world: {chunk_size, level_h, bounds}`),
  `tick`, `ack` (with `h`), `chunk` (full chunk payload with `levels[]`), `error`.
  Client → server: `input` (dx, dy, sequence), `clock` (paused, speed). On connect the hub sends
  the snapshot plus every chunk within radius 2 the connection has not seen; crossing a chunk
  boundary pushes only the new ones (per-connection revision map). Movement is 20 Hz, independent
  of the 1 Hz clock; pause rejects movement with `accepted=false, reason="paused"`.
- **Standing rule (shared).** Step at most 0.5 m (`Δh ≤ 1`), 2 m (4 `h`) of open headroom, no wall
  on the shared edge; diagonal moves require both L detours open. Floor slabs occupy their own `h`
  volume. Speed: ×0.6 up, ×0.85 down, divided by the material's `walk_cost`.
- **Types.** `combined_schema()` merges OpenAPI with the WS message models and writes
  `server/schema.json`; `web/src/net/schema.d.ts` is generated from it and committed.

## Commands

```text
# Launcher: build once, and again after changing launcher/ (the exe is gitignored)
npm run launcher:build        # Native AOT publish, copies EtherBound.exe to the root
EtherBound.exe                # server (hot reload) + web; keys O R W L H Q; logs in logs\
EtherBound.exe --no-reload    # no restart on saved server sources
EtherBound.exe --cleanup      # kill this checkout's leftovers; refuses while a launcher runs
dotnet run --project launcher/src    # while working on the launcher itself

# Schema and types
cd server && uv run etherbound-schema      # writes server/schema.json
cd web && npm run gen:types                # server/schema.json -> src/net/schema.d.ts

# Checks
cd server && uv run pytest && uv run ruff check && uv run ruff format --check && uv run pyright
cd web && npm run gen:types && git diff --exit-code src/net/schema.d.ts && npm run build
npm run check:launcher        # dotnet build -c Release (warnings are errors) + dotnet test
```

`server/schema.json` must be regenerated before `gen:types`; it is not committed. Without a
console (an agent, output redirected) `EtherBound.exe` prints plain lines and takes no keys; end
its process to stop it, and its job takes the services with it. The logs are
`logs\launcher.log`, `server.log` and `web.log`, with the previous run kept as `*.prev.log`.

## Measured pitfalls

- **uvicorn's `--reload` hangs under a launcher that pipes its output.** Its reloader restarts
  the worker with a console Ctrl+C, and conhost only dispatches a pending Ctrl+C when a process
  makes a console call; with every stream on a pipe none does. `EtherBound.exe` never passes
  `--reload`: it watches `server/src` and `server/alembic` (`*.py`, `*.toml`) and restarts the
  server, once per burst of saves.
- **The launcher's jobs must not allow breakaway.** The venv `python.exe` and uv's trampoline
  put their child in jobs of their own that allow silent breakaway, and a silent breakaway climbs
  every parent job that permits one: uvicorn would outlive the launcher.
- **No shells between the launcher and a service.** cmd.exe (and the `npm`, `npx` and
  `node_modules\.bin\*.cmd` shims) turns a Ctrl+C into an unanswerable "Terminate batch job?"
  prompt; that is how the old batch launcher left zombies holding `logs\server.log`. Services are
  started with `CreateProcessW`, suspended until they sit in their job.
- **Never close a service's stdin** (Vite exits on stdin EOF) and never set `FORCE_COLOR`
  (picocolors takes `0` as "force"); the launcher sets `NO_COLOR=1`.
- **Building the launcher.** `dotnet publish -o .` excludes the project's own sources (CS5001);
  the VS 2026 Build Tools need `vswhere.exe` on `PATH` for the AOT link. `npm run launcher:build`
  handles both. The exe is locked while it runs: quit it before rebuilding.
- **Server logs go quiet after the migrations.** `server/alembic/env.py` calls `fileConfig`,
  which disables uvicorn's loggers: no "startup complete", no request errors, no shutdown lines.
  Open in `docs/PENDING.md`.
- **The database file stays locked while the server runs.** Stop the server before touching
  `data/etherbound.db`.
- **Roof slabs eat stair headroom.** A floor slab occupies its own `h` volume, so a stairwell
  needs a roof hole or the upper steps lose their 2 m of headroom and become unstandable.
- **Edge walls belong to the tile that owns the edge.** A wall west of tile (1,0) is `wall_w[1]`
  of the same chunk; chunk-border walls are stored by the neighbouring chunk's first column.
- **Spawn must come from the generator's road.** A first-walkable-tile scan starts in the map
  corner, where radius-2 chunk streaming only finds 9 chunks instead of 25.
- **A* needs two guards.** A goal whose tile has no standing surface must return `None`
  immediately, and unreachable sweeps need an expansion cap, or a probe can run for minutes.
- **`schema.json` is not committed.** It is generated (`uv run etherbound-schema`); `gen:types`
  fails if the file is missing. Only `schema.d.ts` is committed.
- **Pytest warnings are third-party** (FastAPI/Starlette/pytest-asyncio deprecations), not project
  issues. `pytest` is 25 tests, all passing.

## Not yet present

The other seven primitives as data models, verbs beyond `move`, the event bus,
witnesses/knowledge, tile physics, water simulation, NPCs, LLM, Jev, LimeZu art and the city
generator. Placeholder colours and the cutaway go away with the art pass.
