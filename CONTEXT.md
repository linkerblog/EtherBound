# CONTEXT.md

Current technical context of EtherBound. It records what exists, how it runs and which pitfalls
have been measured. Design lives in `docs/utils/VISION.md`; `Dev-001` (archived in `docs/done/`)
specifies the Phase 0 skeleton, `Dev-002` (archived in `docs/done/`) the world model described
here, `Dev-003` (archived in `docs/done/`) the native launcher, `Dev-005` (archived in
`docs/done/`) the event bus and action pipeline, and `Dev-007` (archived in `docs/done/`) the op
vocabulary, generated menus and activities.

## Modules

| Module | Path | Responsibility |
|---|---|---|
| server.app | `server/src/etherbound/app.py`, `config.py`, `routes/api.py` | FastAPI app, lifespan (migrations, world, clock, schema export), REST routes |
| server.clock | `server/src/etherbound/clock.py` | 1 Hz logic clock: speeds x1/x3/x10, pause, autopause locks |
| server.engine | `server/src/etherbound/engine/` | World engine: the only writer of state. `world.py` (grid ownership, generation commit, stale-world regeneration, actor state, event persistence/dispatch), `actions.py` (targets, actions, `ActivityState`, `MenuEntry`), `ops.toml` (the 58-op vocabulary), `ops/` (catalog loader, handler registry, `move`, `inspect`, `wait`, `dig`, `climb`), `movement.py` (`move_in_world`, sub-step standing rule, slope/material cost, 0.3 m wall clearance) |
| server.events | `server/src/etherbound/events/` | Typed committed event models and FIFO async subscriber bus; engine assigns global sequence and stores logged events transactionally |
| server.world | `server/src/etherbound/world/` | World data: `materials.py` + `materials.toml` (append-only registry), `chunk.py` (blobs, per-tile `dug`), `grid.py` (per-chunk level index, solidity/headroom, wall bases, climbable links, lazy chunk-loader API), `nav.py` (A* over standing spots), `gen/` (seeded noise + versioned test world) |
| server.net | `server/src/etherbound/net/` | WS hub with per-connection chunk tracking; movement chunk changes use the position returned by `submit`, not a world-state DB read per input; Pydantic timed `input` (`dt` 0 < dt ≤ 0.1 s, default 0.05); combined OpenAPI + WS schema export |
| server.db | `server/src/etherbound/db/`, `server/alembic/` | SQLAlchemy models, engine/session factory, Alembic upgrade on start |
| server.rng | `server/src/etherbound/rng.py` | `RNGStreams.stream(system)` — one seeded stream per system (`worldgen` drives generation) |
| web.world | `web/src/world/` | `ChunkStore`, `rules.ts` (server-parity standing/headroom/wall rules, slope and material costs), `cutaway.ts` (roof connectivity and height cutoff), `pick.ts` (height-aware tile picking), `materials.ts` |
| web.game | `web/src/game/` | Phaser scene: fixed 64×32 isometric projection (16 px per `h`), chunk base layers and lazily sorted diagonal rows split at Niko's feet, terraced ground/floor faces and walls, grass atlas plus vector placeholders, roofed cutaway and front-wall stubs, chunk culling, screen-relative WASD, isometric bounds and height-aware right-click picking; integer zoom x1–x4 (wheel, `+`/`-`/`0`, saved per browser); timed 50 ms movement steps plus final partial step; clears and redraws arriving-chunk neighbours; resyncs idle prediction on ticks |
| web.net | `web/src/net/` | WS client, prediction/reconciliation by replaying unacknowledged timed steps (no wall clock, 0.3 m wall clearance), generated `schema.d.ts`, protocol types; snapshot listeners run before state listeners, and `requestNewGame(seed)` calls the existing REST endpoint; `sendAction` and pause flush the current partial movement step, `onResult`, `onActivity` |
| web.ui | `web/src/ui/` | React overlay framed around the Phaser viewport: clock, speeds, pills, meters, `FEED` (kinds `seen`, `act`, `warn`, `fail`, `echo`), `ACT` telemetry row, input, generated context menu (server `target` line, arrow keys, Enter, Esc) that submits each entry's `action`; `NEW` control opens a confirmation popover with a seed field; `DEBUG` opens a right-side drawer with a placeholder tab |
| launcher | `launcher/` | `EtherBound.exe`, the dev launcher (C#, .NET 10, Native AOT): starts server + web without shells, each in its own job inside a kill-on-close launcher job, health checks, hot reload by restart, leftover and port handling, UTF-8 logs, framed version/services banner |
| tooling | root config: `package.json`, `global.json`, `.gitignore`, `.env.example` | Build and check scripts, pinned .NET SDK |

## Data model

SQLite at `data/etherbound.db` (gitignored). Migrations `0001_initial`, `0002_world`, `0003_event` and `0004_dig_activity`:

| Table | Columns |
|---|---|
| `world_meta` | `id` (pk, always 1), `seed`, `game_minute`, `speed` (1/3/10), `paused`, `gen_version` |
| `actor` | `id` (pk), `kind` (`player`), `x`, `y`, `z` (derived `h // 6`), `h` (half-metres), nullable JSON `activity` (`op`, `action`, `started_minute`, `ends_minute`) |
| `material` | append-only `id` ↔ `key` mapping plus rendering/physics properties |
| `chunk` | pk `(cx, cy)`; blobs `ground_h` (int16×1024), `surface_mat` (uint16×1024), nullable `dug` (uint8×1024, NULL = all zeros), `strata` JSON, `revision`, `gen_version` |
| `chunk_level` | pk `(cx, cy, z)`; blobs `floor_h`, `floor_mat`, `wall_n`, `wall_w`, `edge_flags`, `flags` |
| `event` | `seq` (global ordered pk), `game_minute`, `type`, nullable `actor_id`, JSON `data`; indexed by minute, type and actor |

Spatial units: 1 m tiles in 32×32 chunks; `h` in half-metres; `z` is the absolute 3 m band
`floor(h / 6)`; walls live on tile edges (each tile owns north/west) with doorway/window edge
flags; below the surface everything is implicit strata until a `void` flag excavates it. Strata
depth is measured from the original ground (`ground_h + dug`), so digging exposes deeper layers
instead of dragging them down. The test
world (`gen_version = 3`) is 8×8 chunks with hills, a road (spawn at 121.5, 128.5, h=2), a terrace
with a ramp, a building with a graded approach, west doorway, accessible basement, first floor at
h=18, roof at h=24 and a pond with a park pit. `new_game` wipes actors, chunks and levels
and regenerates from the seed; `ensure_world` fills an existing Phase 0 save without a wipe. On load, a saved actor within
0.5 m of a standing surface in its tile is snapped to that surface's exact `h` (no event);
further off, it is relocated to spawn (`actor.spawned`, `relocated`).

## Contracts

- **Action API.** `WorldEngine.submit(actor_id, action, delta_seconds)` is the single mutation entry
  point: pause check → a zero-length `move` returns accepted and does nothing → handler
  `validate` (a rejection changes nothing and emits nothing) → a running activity is cleared with
  `activity.finished` (`interrupted`) → an instant op resolves (events and an optional `text`), a
  durative one stores an activity and emits `activity.started` → transactional commit and event
  enqueue → FIFO dispatch outside the engine lock. Actions are a union discriminated by `op`;
  targets are `self` or `tile {x, y, h}`.
- **Ops.** `engine/ops.toml` is the vocabulary (key, label, group, target kinds, tags); a handler
  (`applies`, `build`, `validate`, `duration`, `resolve`, `complete`) gives an op behaviour, and
  `register` refuses a key missing from the catalog. Handled: `move` (never in menus), `inspect`
  (instant, 30 m, text only, no event), `wait` (15 min), `dig` (ground surface only,
  `ceil(30 × dig_cost)` min per 0.5 m, `dig_cost ≤ 2`, own tile or orthogonal neighbour within
  ±1 m), `climb` (orthogonal neighbour 1–1.5 m up or down, 1 min).
- **Activities.** One per actor, stored on the actor, advanced only by clock ticks. In
  `advance_time`, actors whose `ends_minute` has come are completed in id order before
  `clock.ticked`: the action is re-validated, and either `complete` applies its events then
  `activity.finished` (`completed`), or `activity.finished` (`failed`, reason). A paused clock
  freezes them; progress is lost on interruption.
- **Menus.** `WorldEngine.menu(actor_id, x, y, z)` is a read (no lock): candidates are the tile's
  standing surface for `z` and, on the actor's own tile, `self`; each handled op whose targets
  allow a candidate and whose `applies` holds becomes an entry with its exact `action`,
  `available` and `reason`, in catalog order.
- **Event bus.** Only `WorldEngine` stamps/enqueues events. Logged events share the state
  transaction; `clock.ticked` dispatches but is not stored. `new_game` resets the log and sequence
  to 1. A fresh world logs `world.generated`, `actor.spawned`, `clock.changed`; opening an existing
  save emits nothing. `drain()` called while another task is draining returns immediately; the
  active task dispatches the caller's events in `seq` order, possibly after the caller has returned.
  Nothing guarantees subscribers have run when `submit`, `set_clock` or `new_game` returns.
  Handlers run in subscription order; reentrant events append to the active FIFO drain. Handler
  failures are logged and isolated; a cascade is capped at 10,000 events. `seq` is unique among
  stored events; a transient event's `seq` may be reused after a restart, so never key state on it.
- **REST.** `GET /api/health`, `POST /api/game/new {seed}`, `GET /api/game/state`,
  `GET /api/materials`, `GET /api/world/chunk?cx&cy`, `GET /api/menu?x&y&z` (any `z`, computed for Niko;
  returns a `target` line like `Asphalt · 1 m` and `ops: MenuEntry[]`; the client renders, never
  adds),
  `GET /api/events?after_seq&limit&type&actor_id` (ordered event records; limit 1–500).
- **WebSocket `/ws`.** Server → client: `snapshot` (with `world: {chunk_size, level_h, bounds}`),
  `tick`, `ack` (with `h`), `result` (for an `action`: accepted, reason, text, activity),
  `activity` (one of Niko's activities finished: op, outcome, reason), `chunk` (full chunk payload
  with `levels[]`), `error`. Snapshot and tick actors carry `activity` (op, started, ends).
  Client → server: `input` (dx, dy, sequence, dt), `action` (sequence, action), `clock` (paused,
  speed). A `chunk.changed` event (not logged) re-sends the chunk only to connections that
  already hold an older revision. On connect the hub sends
  the snapshot plus every chunk within radius 2 the connection has not seen; movement carries `dt`;
  crossing a chunk
  boundary pushes only the new ones (per-connection revision map). Movement is 20 Hz, independent
  of the 1 Hz clock; pause rejects movement with `accepted=false, reason="paused"`.
- **Standing rule (shared).** Step at most 0.5 m (`Δh ≤ 1`), keep the three half-metre cells
  `h+1..h+3` clear (a slab at `h+4` touches but does not intersect a 2 m body), no wall on the shared
  edge, and at least 0.3 m body-centre clearance from blocked edges. Diagonals validate both
  height-consistent L routes. Navigation adds same-column `LEVEL_CLIMBABLE` links; normal player
  movement still uses the shared action API. Speed: ×0.6 up, ×0.85 down, divided by `walk_cost`.
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
- **The database file stays locked while the server runs.** Stop the server before touching
  `data/etherbound.db`.
- **Roof slabs eat stair headroom.** A floor slab occupies its own `h` volume, so a stairwell
  needs a roof hole or the upper steps lose their 2 m of headroom and become unstandable.
- **A blocked movement axis must stay blocked for that input step.** Otherwise repeated substeps
  snap the actor between the wall and the 0.3 m radius limit; server movement and client prediction
  now clamp once and slide only along an unblocked axis.
- **Levels are indexed per chunk.** Tile standing/solidity lookups use the chunk's own sparse level
  map rather than scanning every level in the world. The optional `WorldGrid.chunk()` loader caches
  hits and misses; the current test world is eagerly loaded before movement.
- **Test-world changes require a generator version bump.** `ensure_world` regenerates an older
  generator's chunks and preserves actor rows; it does not wipe the save or change the schema.
- **The WebSocket hub should use action results for chunk tracking.** `ActionResult.x/y` already
  identify the resulting player chunk, avoiding a DB-backed `get_state()` on every movement input.
- **Edge walls belong to the tile that owns the edge.** A wall west of tile (1,0) is `wall_w[1]`
  of the same chunk; chunk-border walls are stored by the neighbouring chunk's first column.
- **Spawn must come from the generator's road.** A first-walkable-tile scan starts in the map
  corner, where radius-2 chunk streaming only finds 9 chunks instead of 25.
- **A* needs two guards.** A goal whose tile has no standing surface must return `None`
  immediately, and unreachable sweeps need an expansion cap, or a probe can run for minutes.
- **`schema.json` is not committed.** It is generated (`uv run etherbound-schema`); `gen:types`
  fails if the file is missing. Only `schema.d.ts` is committed.
- **The browser never moved Niko before v0.5.0.** From v0.1.0 the client sent `input` as
  `{direction, vector}` with no `dx`/`dy`, so the server rejected every input with a validation
  `error` and only the prediction moved. A GUI check of walking must compare against
  `GET /api/game/state`, not the screen.
- **Held keys repeat.** Movement distance per `input` depends on `dt` and the surface's terrain
  and slope cost. The client sends timed steps in a loop every 50 ms while held, including multiple
  steps on a slow frame, plus the final partial step when the direction changes, the key is
  released, or an action is sent. `dt` defaults to 0.05 and is bounded
  to 0.1 s. A zero vector remains a no-op and must never interrupt an activity. Prediction is the
  authoritative position plus replay of unacknowledged steps plus the current partial step; material
  `walk_cost` and slope multipliers match the server.
- **Moves are logged by the event subscriber.** Logged events appear in `server.log` as
  `event 812 actor.moved niko {"from_tile":...,"to_tile":...,"mode":"walk"}`. The `etherbound`
  logger owns an INFO handler because Alembic leaves root at `WARN`. Transient events
  such as `clock.ticked` and `chunk.changed` are skipped; `/api/events` remains the structured log.
- **Pytest warnings are third-party** (FastAPI/Starlette/pytest-asyncio deprecations), not project
  issues. The current server suite is 79 tests, all passing.
- **Current automated validation:** 79 server tests, 25 web tests, and 55 launcher tests pass;
  generated API types are unchanged. The production web build passes with the existing large-bundle
  advisory. Manual isometric visual and performance acceptance remains in `docs/PENDING.md`.

## Not yet present

The other seven primitives as data models, handlers for the 53 ops beyond `move`, `inspect`,
`wait`, `dig` and `climb`, modifiers, rolls, witnesses/knowledge, tile physics,
water simulation, NPCs, LLM, Jev, LimeZu art pipeline and the city generator. Vector placeholders
remain for materials without dedicated sprites.
