# CONTEXT.md

Current technical context of EtherBound. It records what exists, how it runs and which pitfalls
have been measured. Design lives in `docs/utils/VISION.md`; `Dev-001` (archived in `docs/done/`)
specifies the Phase 0 skeleton, `Dev-002` (archived in `docs/done/`) the world model described
here, `Dev-003` (archived in `docs/done/`) the native launcher, `Dev-005` (archived in
`docs/done/`) the event bus and action pipeline, and `Dev-007` (archived in `docs/done/`) the op
vocabulary, generated menus and activities. `Dev-009` (archived in `docs/done/`) makes BitCanvas
the standalone terrain/furniture generator and adds guarded game-sheet sync. `Dev-012` (archived in
`docs/done/`) adds objects: the Matter primitive as data (kinds, volumes, carrying and the first
handling ops). `Dev-013` (archived in `docs/done/`) adds deterministic action-time tile physics,
cumulative material damage, persisted wall integrity, impacts, falls and authoritative paths.
`Dev-015` (archived in `docs/done/`) adds the generator registry, the `lab` debug generator and
per-save generator options (migration `0007_generator`). `Dev-018` (archived in `docs/done/`) seeds
six Extras near the spawn and runs their deterministic routine brain (`server.minds`), adding
`name` and `mind` to the actor (migration `0008_extra`).
`Dev-022` (archived in `docs/done/`) splits the largest server, renderer, UI and BitCanvas files
without behavior changes, and adds a warning-only source-size check.

## Modules

| Module | Path | Responsibility |
|---|---|---|
| server.app | `server/src/etherbound/app.py`, `config.py`, `routes/api.py` | FastAPI app, lifespan (migrations, world, clock, schema export) and the REST routes. |
| server.clock | `server/src/etherbound/clock.py` | 1 Hz logic clock: speeds x1/x3/x10, pause, autopause locks. |
| server.engine | `server/src/etherbound/engine/` (`payloads.py`, `world_setup.py`, `menu.py`, `ops/edges.py`, `ops/damage.py`, `ops/travel.py`) | The only state writer: `WorldEngine` orchestration, action/target models, generated menus, every handler, deterministic SI physics and trajectory resolution. |
| server.events | `server/src/etherbound/events/` | Typed committed events, FIFO async subscriber bus and transactional sequence persistence. |
| server.minds | `server/src/etherbound/minds/` | Decision sources that propose through the action API; today `ExtrasBrain`, the deterministic routine of an Extra. |
| server.world | `server/src/etherbound/world/` | Material registry, validated object kinds, chunk/grid geometry, A*, the generator registry and seeded generation (`test` and `lab`). |
| server.net | `server/src/etherbound/net/` | WS hub, timed inputs, per-connection chunk tracking and the combined OpenAPI + WS schema export. |
| server.db | `server/src/etherbound/db/`, `server/alembic/` | SQLAlchemy models, engine/session factory and Alembic upgrades. |
| server.rng | `server/src/etherbound/rng.py` | `RNGStreams.stream(system)` — one seeded stream per system. |
| web.world | `web/src/world/` | Chunk store, server-parity standing/wall rules, cutaway, ray, occlusion, picking and material tables. |
| web.game | `web/src/game/` (`MapScene.ts`, `terrainAtlas.ts`, `chunkRenderer.ts`, `wallPainter.ts`, `extras.ts`, `physics.ts`) | `MapScene` owns scene lifecycle, input, camera and streaming; the atlas, `ChunkRenderer` and `WallPainter` draw ordered batches, while `ExtrasLayer` and `PhysicsAnimator` animate server state. It never simulates collision. |
| web.net | `web/src/net/` | WS client, movement prediction/reconciliation, generated `schema.d.ts` and action-result trajectory listeners. |
| web.ui | `web/src/ui/` (`ContextMenus.tsx`, `NewGamePopover.tsx`, `DebugViews.tsx`, `HudPanels.tsx`) | React overlay in a framed viewport with `GAME`/`DEBUG`/`LLM` view tabs (`viewTabs.ts`, `Alt+1..3`): clock, speeds, pills, meters, `CARRY`, `FEED`, `ACT`, input, context/radial menus (`radialMenu.ts`), `NEW` map select; the `DEBUG` view holds the `MAP` generator form (`genForm.ts`), `LLM` is a placeholder. No debug drawer. |
| launcher | `launcher/` | `EtherBound.exe`, the C# (.NET 10, Native AOT) dev launcher: server + web jobs, health checks, hot reload, leftover and port handling. |
| bitcanvas | `BitCanvas/` (`pixelart.js`, `gamesync.js`, `core.js`, `terrain.js`, `sides.js`, `furnitureData.js`, `furniture.js`, `app.js`) | Standalone seeded texture and furniture generator (classic deferred scripts, HTML/JS, no build) with guarded "Send to game" sync. |
| tooling | root config: `package.json`, `global.json`, `.gitignore`, `.env.example` | Build and check scripts, pinned .NET SDK. |

## Data model

SQLite at `data/etherbound.db` (gitignored). Migrations `0001_initial`, `0002_world`, `0003_event`,
`0004_dig_activity`, `0005_object`, `0006_physics`, `0007_generator` and `0008_extra`:

| Table | Columns |
|---|---|
| `world_meta` | `id` (pk, always 1), `seed`, `game_minute`, `speed` (1/3/10), `paused`, `gen_version`, `generator` (default `test`), `gen_options` (JSON, default `{}`) |
| `actor` | `id` (pk), `kind` (`player` or `extra`), nullable `name`, `x`, `y`, `z` (derived `h // 6`), `h` (half-metres), `mass_kg` (default 80), nullable JSON `activity` (`op`, `action`, `started_minute`, `ends_minute`), nullable JSON `mind` (`anchor {x, y, h}`, `goal {kind: wander, x, y, h}` or null) |
| `material` | append-only `id` ↔ `key` mapping plus rendering/physics properties |
| `chunk` | pk `(cx, cy)`; blobs `ground_h` (int16×1024), `surface_mat` (uint16×1024), nullable `dug` (uint8×1024, NULL = all zeros), `strata` JSON, `revision`, `gen_version` |
| `chunk_level` | pk `(cx, cy, z)`; blobs `floor_h`, `floor_mat`, `wall_n`, `wall_w`, `edge_flags`, `flags` |
| `object` | `id` (pk), `kind`, `loc` (`tile`/`in`/`held`/`worn`), nullable `x`/`y`/`h`/`cx`/`cy`, nullable `container_id` (self-FK), nullable `actor_id`, nullable `slot`, `quantity` (> 0), JSON `state`, nullable `integrity`, nullable `owner`; a CHECK pins the exact columns of each `loc` |
| `wall_integrity` | sparse pk `(cx, cy, z, cell_index, edge)` for partly damaged north/west wall edges; stores remaining joules |
| `event` | `seq` (global ordered pk), `game_minute`, `type`, nullable `actor_id`, JSON `data`; indexed by minute, type and actor |

Spatial units: 1 m tiles in 32×32 chunks; `h` in half-metres; `z` is the absolute 3 m band
`floor(h / 6)`; walls live on tile edges (each tile owns north/west) with doorway/window edge
flags; below the surface everything is implicit strata until a `void` flag excavates it. Strata
depth is measured from the original ground (`ground_h + dug`), so digging exposes deeper layers
instead of dragging them down. The test
world (`gen_version = 5`) is 8×8 chunks with hills, a road (spawn at 121.5, 128.5, h=2), a terrace
with a ramp, a building with a graded approach, west doorway, accessible basement, first floor at
h=18, roof at h=24 and a pond with a park pit. It also lays out objects in a fixed order: a shovel
(124, 126, h=2), a backpack (125, 126, h=2), a closed chest (124, 127, h=2) holding apple ×3 and
bottle ×2, a table (137, 133, h=12) with a bottle on its top, two chairs, a shelf with apple ×2, a
barrel (all on the building's ground floor), two chests stacked at (126, 127) (the upper resting
on the lower's top at h=3), and a sledgehammer at (126, 126). `new_game` wipes actors, objects, chunks and levels
and regenerates from the seed; `ensure_world` fills an existing Phase 0 save without a wipe, keeping
held and worn objects with their contents and deleting the uncarried ones before re-laying the v5
layout. On load, a saved actor within
0.5 m of a standing surface in its tile is snapped to that surface's exact `h` (no event);
further off, it is relocated to spawn (`actor.spawned`, `relocated`).

**Extras.** Each world also carries six Extras. `world/population.py` places them from the
`population` RNG stream (names from `world/names.toml`, and walkable standing ground tiles within
15 m of the spawn, reachable by `find_path`, at least 2 m apart); `populate(grid, registry, spawn,
seed) -> tuple[GeneratedActor, ...]` is a pure grid pass that touches no database. The engine writes
each Extra as an `actor` row `extra-NNN` with `kind = "extra"`, its `name`, its position and
`mind = {anchor, goal: null}` where the anchor is its start tile; it logs `actor.spawned` per Extra.
`new_game` creates Niko and the Extras; `ensure_world` settles every existing actor (same snap or
`relocated` rule) and, only when the save has no Extras, seeds them, so a pre-`0008_extra` save
gains them on first open and a second open emits nothing. `population` is deliberately not part of
`GeneratedWorld`: population is actors, not terrain, and never changes the test world's bytes.

**Generators.** `world/gen/registry.py` holds `GENERATORS`: `test` (version 5, the existing test
world, unchanged) and `lab` (version 1, `world/gen/lab.py`). A `GeneratorSpec` carries `key`,
`name`, `version`, a Pydantic options model, `generate(seed, options, registry) -> GeneratedWorld`
and a pure, cheap `spawn(seed, options)`. `GeneratedWorld` (in `world/gen/types.py`) holds the
chunks, levels, objects, spawn, version and generator key; `generate_test_world` keeps its byte
output. `new_game` validates the options, stores `generator` and the full options dump, generates
through the spec and logs `world.generated` with both. `ensure_world` resolves the save's generator
(an unknown key falls back to `test` with a warning) and regenerates when `gen_version` is below the
spec version; `_spawn_point` calls `spec.spawn` instead of the generator running again on load. The
`lab` map is a 4×4-chunk (128 m) flat fixture with asphalt paths and nine 36×36 bays — steps,
materials, water, structure, feature, objects, dig, walls and open — around central bay `feature`,
where a feature (today only `relief`, noise hills) is stamped through a `GenCanvas`
(`world/gen/canvas.py`); the default spawn is the path north of the feature bay. `GET /api/gen`
describes each generator as data: `fields` (path, label, kind, default, min, max, step, choices,
group) and, for `lab`, its `bays`.

## Contracts

- **Action API.** `WorldEngine.submit(actor_id, action, delta_seconds)` is the single mutation entry
  point: pause check → a zero-length `move` returns accepted and does nothing → handler
  `validate` (a rejection changes nothing and emits nothing) → a running activity is cleared with
  `activity.finished` (`interrupted`) → an instant op resolves (events and an optional `text`), a
  durative one stores an activity and emits `activity.started` → transactional commit and event
  enqueue → FIFO dispatch outside the engine lock. Actions are a union discriminated by `op`;
  targets are `self`, `tile {x, y, h}`, `object {id}`, `actor {id}` or
  `edge {x, y, z, direction}`; `put` carries a second `into` target.
- **Ops.** `engine/ops.toml` is the vocabulary (key, label, group, target kinds, tags); a handler
  (`applies`, `builds`, `subject`, `validate`, `duration`, `resolve`, `complete`) gives an op
  behaviour, and `register` refuses a key missing from the catalog. One target can `builds` several
  actions and `subject` names what an entry acts on. Handled: `move` (never in menus), `inspect`
  (instant, 30 m, tile, object or actor text, no event), `wait` (15 min), `dig` (ground surface only,
  `ceil(30 × dig_cost / tool)` min per 0.5 m with the best held `tool.dig` or 0.25 bare-handed,
  `dig_cost ≤ 2`, refused when an object rests at the ground `h`), `climb`, the instant handling
  ops, and `push`, `pull`, `drag`, `throw`, `hit`, `break`. Physics resolves a full tile path at
  submit time using SI energy and friction values; `physics.resolved` records the result and the
  action response carries its authoritative trajectory. `climb` still takes
  1 min to an orthogonal neighbour 1–1.5 m up or down, and reach is the actor's own tile or an
  orthogonal neighbour within `h-1..h+1.5` m.
- **Objects.** Only the engine writes object rows. A tile object rests at `h`; a `solid` kind fills
  the cells `h+1..h+height` and a `surface` kind's top `h+height` is a standing and placing surface.
  Taking needs both hands for `bulk > 25`, respects a 40 kg lift limit, splits a stack and merges it
  back on drop. A closed container hides its contents from the chunk payload and the menu; a
  supported object (something on its top) cannot be taken or opened. Carried mass slows movement
  (`load_multiplier`); `load_kg` recursively counts held and worn objects and their contents.
- **Physics.** Shove uses reduced mass at 2 m/s; throw caps at 8 m/s and 100 J; hands use 2 kg at
  5 m/s; horizontal loss per metre is `0.05 × mass × 9.81` J. `Object.integrity` is remaining J
  (NULL means intact at material resistance × max(1, height) half-metre cells); wall edges span six
  cells and partial damage persists in
  `wall_integrity`. Zero integrity turns objects into data-defined rubble (spilling container
  contents) or opens wall edges. Falls resolve in the same action; bodies over a 3 m fall emit
  potential energy as `impact`. Physics never mutates health. The client animates server paths only.
- **Activities.** One per actor, stored on the actor, advanced only by clock ticks. In
  `advance_time`, actors whose `ends_minute` has come are completed in id order before
  `clock.ticked`: the action is re-validated, and either `complete` applies its events then
  `activity.finished` (`completed`), or `activity.finished` (`failed`, reason). A paused clock
  freezes them; progress is lost on interruption.
- **Minds.** `WorldEngine.set_goal(actor_id, goal, reason)` is the only writer of `mind.goal`: it
  refuses the player, an unknown actor and a goal tile without a standing surface, sets the goal
  (or clears it) and commits `actor.goal_set` (`reason` chosen/arrived/stuck/unreachable) in the same
  transaction as the actor row. `ExtrasBrain`, subscribed to `clock.ticked` before the WS hub, walks
  each idle Extra toward its goal with `submit(move, delta_seconds)` at 4 m/s per real second
  (`time_scale / speed` per tick, at most six move submits) and otherwise picks a `wander` goal
  within 12 m of its anchor (60 % of idle ticks) or waits; it re-derives its path from state, so a
  restart replays it. A stalled step recomputes once, then `set_goal(None, "stuck")`. The brain only
  proposes; the engine validates and writes every step.
- **Menus.** `WorldEngine.menu(actor_id, x, y, z)` is a read (no lock): candidates are the tile's
  standing surface for `z`, `self` and the objects lying on it at that surface's band, plus the
  contents of its open or lidless containers; on the actor's own tile, also every held and worn
  object and the contents of worn open containers; it also offers actors standing on the clicked tile
  at that surface's band and existing north/west wall edges as physics targets. Each handled op whose
  targets allow a candidate
  and whose `applies` holds becomes an entry with its `action`, `available`, `reason` and `subject`,
  in catalog order then candidate order. The client renders that payload either as the right-click
  list or as the radial menu on `V`, which centers the same entries on Niko's own tile; it never
  adds, removes or reorders an entry.
- **Event bus.** Only `WorldEngine` stamps/enqueues events. Logged events share the state
  transaction; `clock.ticked` dispatches but is not stored. `new_game` resets the log and sequence
  to 1. A fresh world logs `world.generated`, one `actor.spawned` for Niko and each Extra, then
  `clock.changed`; opening an existing save emits nothing, except the Extra spawns when it had none.
  `drain()` called while another task is draining returns immediately; the
  active task dispatches the caller's events in `seq` order, possibly after the caller has returned.
  Nothing guarantees subscribers have run when `submit`, `set_clock` or `new_game` returns.
  Handlers run in subscription order; reentrant events append to the active FIFO drain. Handler
  failures are logged and isolated; a cascade is capped at 10,000 events. `seq` is unique among
  stored events; a transient event's `seq` may be reused after a restart, so never key state on it.
- **REST.** `GET /api/health`, `POST /api/game/new {seed, generator?, options?, paused?}` (an unknown
  generator or invalid options returns 422 and leaves the world untouched; `{seed}` alone gives the
  `test` world, and `paused: true` starts the clock paused so a shot does not move the Extras), `GET /api/game/state` (with `generator`, `gen_version`, `gen_options`),
  `GET /api/gen` (every generator's options as form fields plus the lab bays),
  `GET /api/materials`, `GET /api/objects` (the kind catalog), `GET /api/world/chunk?cx&cy`,
  `GET /api/menu?x&y&z` (any `z`, computed for Niko;
  returns a `target` line like `Asphalt · 1 m` and `ops: MenuEntry[]`; the client renders, never
  adds),
  `GET /api/events?after_seq&limit&type&actor_id` (ordered event records; limit 1–500).
- **WebSocket `/ws`.** Server → client: `snapshot` (with `world: {chunk_size, level_h, bounds}`),
  `tick`, `ack` (with `h`), `result` (for an `action`: accepted, reason, text, activity, `carried`,
  `load_kg`, trajectory),
  `activity` (one of Niko's activities finished: op, outcome, reason), `chunk` (full chunk payload
  with `levels[]` and `objects[]`), `error`. Snapshot and tick actors carry `name`, `activity`,
  `carried` and `load_kg`.
  Client → server: `input` (dx, dy, sequence, dt), `action` (sequence, action), `clock` (paused,
  speed). A `chunk.changed` event (not logged) re-sends the chunk only to connections that
  already hold an older revision. On connect the hub sends
  the snapshot plus every chunk within radius 2 the connection has not seen; movement carries `dt`;
  crossing a chunk
  boundary pushes only the new ones (per-connection revision map). Movement is 20 Hz, independent
  of the 1 Hz clock; pause rejects movement with `accepted=false, reason="paused"`.
- **Standing rule (shared).** Step at most 0.5 m (`Δh ≤ 1`), keep the three half-metre cells
  `h+1..h+3` clear (a slab at `h+4` touches but does not intersect a 2 m body), no wall on the shared
  edge, and at least 0.3 m body-centre clearance from blocked edges. A solid object's cells count as
  blocked and a covered surface loses its headroom; a surface object's top `h+height` is an
  additional standing surface. Diagonals validate both
  height-consistent L routes. Navigation adds same-column `LEVEL_CLIMBABLE` links; normal player
  movement still uses the shared action API. Speed: ×0.6 up, ×0.85 down, divided by `walk_cost` and
  multiplied by the carried `load_multiplier`.
- **Types.** `combined_schema()` merges OpenAPI with the WS message models and writes
  `server/schema.json`; `web/src/net/schema.d.ts` is generated from it and committed.

## Commands

```text
# One-time per clone: enable the versioned git hooks
npm run setup

# Launcher: build once, and again after changing launcher/ (the exe is gitignored)
npm run bitcanvas             # open the standalone sprite generator
npm run launcher:build        # Native AOT publish, copies EtherBound.exe to the root
EtherBound.exe                # server (hot reload) + web; keys O R W L H Q; logs in logs\
EtherBound.exe --no-reload    # no restart on saved server sources
EtherBound.exe --cleanup      # kill this checkout's leftovers; refuses while a launcher runs
dotnet run --project launcher/src    # while working on the launcher itself

# Schema and types
cd server && uv run etherbound-schema      # writes server/schema.json
cd web && npm run gen:types                # server/schema.json -> src/net/schema.d.ts

# Checks (COMMITS.md; hooks run check:fast on commit, check-versions on the message, check on push)
npm run check                   # versions + sizes + server + web + bitcanvas + launcher
npm run check:fast              # versions (staged), sizes, ruff, bitcanvas
npm run check:sizes             # warning-only scan for sources over 600 lines
npm run check:visual            # Playwright spawn baselines + shell layout (free ports, msedge)
```

`server/schema.json` must be regenerated before `gen:types`; it is not committed. `check:web`
runs the schema export, the `schema.d.ts` diff, the web tests and the production build.
`check:visual` is not part of `check`: it needs free ports 8000 and 5173 (stop the launcher first)
and a GPU-less renderer run. Its specs run on one worker because each resets the shared server with
its own seed. Without a
console (an agent, output redirected) `EtherBound.exe` prints plain lines and takes no keys; end
its process to stop it, and its job takes the services with it. The logs are
`logs\launcher.log`, `server.log` and `web.log`, with the previous run kept as `*.prev.log`.

`check:sizes` scans source files under `server/src`, `web/src`, `BitCanvas` and `launcher/src`;
it skips generated `web/src/net/schema.d.ts` and warns without failing. Only
`server/src/etherbound/engine/ops/handling.py` and `BitCanvas/pixelart.js` currently exceed 600 lines.

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
  generator's chunks and preserves actor rows; it does not wipe the save or change the schema. Bump
  the generator's `version` in its spec, and every save regenerates with its stored options.
- **A generator's options are data, not code.** `GET /api/gen` walks the Pydantic options model;
  adding a field to the model makes it appear in the DEBUG form, and the client never names an
  option. To add a feature: a module in `world/gen/features/` with
  `stamp(canvas, rect, seed, options) -> list[GeneratedObject]`, its options model as a nested field
  of `LabOptions`, and its key in the `feature` choice; then bump the `lab` version.
- **An unknown generator row falls back to `test`.** `ensure_world` logs a warning, resets
  `generator`/`gen_options` and regenerates, so an old or hand-edited save always opens.
- **The WebSocket hub should use action results for chunk tracking.** `ActionResult.x/y` already
  identify the resulting player chunk, avoiding a DB-backed `get_state()` on every movement input.
- **Edge walls belong to the tile that owns the edge.** A wall west of tile (1,0) is `wall_w[1]`
  of the same chunk; chunk-border walls are stored by the neighbouring chunk's first column.
- **Walls stay thin planes on their edge for every system.** Physics, picking, occlusion and the
  cutaway treat a wall as its tile edge. `WALL_T` (1/4 m) and the top strip, end faces and corner
  post are render-only, drawn outward behind the visible face, so nothing moves on the plane.
  Wall-face masks hang from their draw height (the run top), like ground faces.
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
- **Extras are seeded once, never refreshed.** `ensure_world` seeds them only when the save has no
  `kind = "extra"` rows; an existing Extra is settled like Niko, never moved home or renamed. So a
  population change (names, count, radius) does not touch an existing save, and a second open logs
  nothing.
- **The brain bears a full submit per step and only proposes.** Every Extra move is a
  `submit(move, delta_seconds)` with its own transaction and `actor.moved` row; at x10 six Extras can
  mean hundreds of rows a minute, which the event log retention item must eventually bound. The brain
  keeps its path cache and stall retry in memory only, re-derived from state, so a restart at any
  tick replays to the same log.
- **Hide the canvas with `visibility`, never `display: none`.** The Phaser game uses
  `Scale.RESIZE`, so a `display: none` parent collapses it to 0×0; the `DEBUG`/`LLM` views cover it
  and `.hidden-view` keeps it mounted, sized and running.
- **A paused clock paints late in the software renderer.** With no tick after the snapshot the first
  canvas paint can lag a few seconds under swiftshader, so `check:visual` waits before the x1 shot;
  this is a screenshot settle, not a game-render change.
- **`mind` is engine data, read defensively.** `get_state` and `inspect` tolerate a missing or
  malformed `mind` (no goal, `Standing`); only `set_goal` writes it. Niko has no `name` row and is
  shown as `Niko` by `actor_name`; the client draws only non-player actors, picked by `PLAYER_ID`.
- **Pytest warnings are third-party** (FastAPI/Starlette/pytest-asyncio deprecations), not project
  issues.
- **Current automated validation:** 146 server tests, Ruff, Pyright, 93 web tests, generated schema
  types, production web build and 55 launcher tests pass. The `check:visual` Playwright seed-7
  spawn baselines were re-shot at the framed 1240×652 canvas (Dev-019), pass with the new
  `shell-layout.spec.ts`, and passed twice consecutively after Dev-022. BitCanvas's file:// exports
  also remain byte-identical at seed `A17F3C`. Manual physics animation,
  the Dev-018 Extras acceptance and the other GUI acceptances remain in `docs/PENDING.md`.
- **Fix12 module versions:** `server.engine` v0.0.14 and `web.game` v0.1.15; project v2.1.1.
- **VOID is a ground-volume flag, not a missing-floor flag.** Render and pick stored floors even
  when their band is VOID; suppress only a ground top whose own band is void.
- **Do not mix separate sprites with a same-depth terrain batch.** Phaser preserves display-list
  order at equal depth, so a later grass `Image` can cover every cliff, wall, floor and road surface
  in the chunk's base `Graphics`. Terrain tops, faces and walls share ordered `Blitter` batches.
- **Repeated display-list removal is costly.** Destroying thousands of tile sprites can become
  quadratic in the display-list size; dirty chunk redraws reuse their `Blitter` batches instead.
  Browser frame-time estimates remain unmeasured until the manual Fix05 acceptance is run.
- **A face mask is anchored to its owner tile, not inferred from its first edge vertex.** Pass its
  side and owner coordinates separately so south and east faces cannot drift across a ledge.
- **Occlusion probes follow Niko's body, not a shortened feet ray.** Trace each point with
  `t = x + y - (viewerH + probeH)`, and select structures every frame from Niko's rendered
  position against uncut geometry. Keep the cut for one tile beyond its last covering point and
  apply its cutoff per tile at draw and pick time; probing the cut map can make a building flicker.
- **Assets outside `web/` must be static imports.** Vite refuses a `new URL(…, import.meta.url)`
  request for a file outside its serving allow list, silently (Phaser just fails the load). A file
  in the module graph is let through, so the sheets in the root `src/sprites/` are imported
  statically in `web/src/game/terrainSheets.ts`; `terrainSprites.ts` keeps only the pure
  key → file-name table for node tests.
- **The server sends each chunk once per connection.** The hub records it in `_known_chunks` and
  never re-sends it, so the client must cache and replay every chunk it receives to a late
  `onChunk` listener, or a scene that starts after `connect()` loses the world for good.
- **The client must reconnect.** Vite is ready seconds before the API server and hot reload restarts
  it, so a socket can fail to open or drop mid-session. `WebSocketClient` retries a closed socket
  with a backoff; without it the tab stays `OFFLINE` with a black world, because only a new
  connection gets the snapshot and the chunks.
- **Texture mappings are visual only.** `terrainSprites.ts` maps registered material keys to sheet
  file names and `terrainSheets.ts` imports them; movement, collision and menus must continue to use
  material data, never sprite availability.
- **Side sheets are unsheared; the atlas shears them.** `shearSideCell` moves every 32-pixel sheet
  column down as a whole into `faceMask(side, 1)`, so a sheet is a flat 128×32 cap/fill strip with
  four variants per part. Drawing is one unit per cell, the cap only on the face's real top
  (`h1`), never on a run boundary split at `viewerH`, so a cliff does not grow two grass lips.
- **A void ground's faces start at its solid top.** `ChunkStore.solidTopH` walks down through
  contiguous VOID bands, and `drawChunk` measures both the face bottom and the "is it higher" test
  with it; void-cut faces are fill only, so the excavated side has no grass lip.
- **BitCanvas (`bitcanvas` v0.0.6; tooling v0.0.13; project v2.0.1) sends only game-ready terrain sheets.** The File System Access API is Chromium-only
  and requires a user-picked directory named `sprites` containing a `grass` or `floor` directory.
  A send overwrites files: `git restore src/sprites` restores tracked sheets, but newly created
  material PNGs are untracked and need separate cleanup if they were only test outputs. New material
  sheets still require entries in `terrainSprites.ts` and static imports in `terrainSheets.ts`;
  BitCanvas never edits game source. The manual Vite reload check was not run.
- **The terrain atlas side and wall bands each hold at most 8 materials** (512 px per material,
  4096 px per band). The side band and the wall band are separate rows in the same canvas, and a
  ninth sheet in either needs a wider or wrapped band; the loader warns and skips the overflow.
- **A solid object's volume starts ABOVE its resting `h`.** A kind with `height = 2` resting at `h`
  fills the cells `h+1` and `h+2`, and its standing/placing top is `h+2`. The server
  (`grid.py`) and client (`ChunkStore`) must agree exactly, or prediction and picking drift.
- **A non-solid object has no top to rest on.** `height = 0` kinds add no volume and cannot support
  anything, so an apple at your feet is taken, never "something is on it".
- **Object rows hide inside containers.** A closed container's contents are in the database but not
  in the chunk payload nor the menu; a changed tile object still bumps the chunk revision so the
  client re-reads the container's own `open`.
- **The object sprite atlas lives in its own band and shares the 4096 px guard.** Kinds drawn as a
  placeholder prism (no sheet) still take their slot's draw order inside the tile batch.

## Not yet present

The other six primitives as data models, handlers for the 46 ops beyond `move`, `inspect`, `wait`,
`dig`, `climb`, `take`, `drop`, `put`, `open`, `close`, `wear` and `remove`, modifiers, rolls,
witnesses/knowledge, tile physics, water simulation, Agent brains and their needs, traits and
utility (the deterministic Extras exist), LLM, Jev, item degradation and content
beyond the nine v1 object kinds, and the city generator. Vector placeholders remain for materials
not mapped in `web/src/game/terrainSprites.ts`, and placeholder prisms remain for object kinds with
no BitCanvas sprite sheet.
