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

## Modules

| Module | Path | Responsibility |
|---|---|---|
| server.app | `server/src/etherbound/app.py`, `config.py`, `routes/api.py` | FastAPI app, lifespan (migrations, world, clock, schema export) and the REST routes. |
| server.clock | `server/src/etherbound/clock.py` | 1 Hz logic clock: speeds x1/x3/x10, pause, autopause locks. |
| server.engine | `server/src/etherbound/engine/` | The only state writer: action/target models, generated menus, every handler, deterministic SI physics and trajectory resolution. |
| server.events | `server/src/etherbound/events/` | Typed committed events, FIFO async subscriber bus and transactional sequence persistence. |
| server.world | `server/src/etherbound/world/` | Material registry, validated object kinds, chunk/grid geometry, A* and seeded generation. |
| server.net | `server/src/etherbound/net/` | WS hub, timed inputs, per-connection chunk tracking and the combined OpenAPI + WS schema export. |
| server.db | `server/src/etherbound/db/`, `server/alembic/` | SQLAlchemy models, engine/session factory and Alembic upgrades. |
| server.rng | `server/src/etherbound/rng.py` | `RNGStreams.stream(system)` — one seeded stream per system. |
| web.world | `web/src/world/` | Chunk store, server-parity standing/wall rules, cutaway, ray, occlusion, picking and material tables. |
| web.game | `web/src/game/` | Phaser isometric renderer and ordered tile-object batches; animates server physics paths, never simulates collision. |
| web.net | `web/src/net/` | WS client, movement prediction/reconciliation, generated `schema.d.ts` and action-result trajectory listeners. |
| web.ui | `web/src/ui/` | React overlay: clock, speeds, pills, meters, `CARRY`, `FEED`, `ACT`, input, context menu, `NEW` and `DEBUG`. |
| launcher | `launcher/` | `EtherBound.exe`, the C# (.NET 10, Native AOT) dev launcher: server + web jobs, health checks, hot reload, leftover and port handling. |
| bitcanvas | `BitCanvas/` | Standalone seeded texture and furniture generator (HTML/JS, no build) with guarded "Send to game" sync. |
| tooling | root config: `package.json`, `global.json`, `.gitignore`, `.env.example` | Build and check scripts, pinned .NET SDK. |

## Data model

SQLite at `data/etherbound.db` (gitignored). Migrations `0001_initial`, `0002_world`, `0003_event`,
`0004_dig_activity`, `0005_object` and `0006_physics`:

| Table | Columns |
|---|---|
| `world_meta` | `id` (pk, always 1), `seed`, `game_minute`, `speed` (1/3/10), `paused`, `gen_version` |
| `actor` | `id` (pk), `kind` (`player`), `x`, `y`, `z` (derived `h // 6`), `h` (half-metres), `mass_kg` (default 80), nullable JSON `activity` (`op`, `action`, `started_minute`, `ends_minute`) |
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
  (instant, 30 m, tile or object text, no event), `wait` (15 min), `dig` (ground surface only,
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
- **Menus.** `WorldEngine.menu(actor_id, x, y, z)` is a read (no lock): candidates are the tile's
  standing surface for `z`, `self` and the objects lying on it at that surface's band, plus the
  contents of its open or lidless containers; on the actor's own tile, also every held and worn
  object and the contents of worn open containers; it also offers adjacent actors and existing
  north/west wall edges as physics targets. Each handled op whose targets allow a candidate
  and whose `applies` holds becomes an entry with its `action`, `available`, `reason` and `subject`,
  in catalog order then candidate order.
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
  `GET /api/materials`, `GET /api/objects` (the kind catalog), `GET /api/world/chunk?cx&cy`,
  `GET /api/menu?x&y&z` (any `z`, computed for Niko;
  returns a `target` line like `Asphalt · 1 m` and `ops: MenuEntry[]`; the client renders, never
  adds),
  `GET /api/events?after_seq&limit&type&actor_id` (ordered event records; limit 1–500).
- **WebSocket `/ws`.** Server → client: `snapshot` (with `world: {chunk_size, level_h, bounds}`),
  `tick`, `ack` (with `h`), `result` (for an `action`: accepted, reason, text, activity, `carried`,
  `load_kg`, trajectory),
  `activity` (one of Niko's activities finished: op, outcome, reason), `chunk` (full chunk payload
  with `levels[]` and `objects[]`), `error`. Snapshot and tick actors carry `activity`, `carried`
  and `load_kg`.
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
npm run check                   # versions + server + web + bitcanvas + launcher
npm run check:fast              # versions (staged), ruff, bitcanvas
npm run check:visual            # Playwright seed-7 spawn baselines (free ports, msedge)
```

`server/schema.json` must be regenerated before `gen:types`; it is not committed. `check:web`
runs the schema export, the `schema.d.ts` diff, the web tests and the production build.
`check:visual` is not part of `check`: it needs free ports and a GPU-less renderer run. Without a
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
  issues. The current server suite is 122 tests, all passing.
- **Current automated validation:** 122 server tests, Ruff, Pyright, 64 web tests, generated schema
  types and production web build pass. The `check:visual` Playwright seed-7 spawn baselines pass
  twice in a row. The launcher suite was not rerun for Dev-013. Manual physics
  animation and the other GUI acceptances remain in `docs/PENDING.md`.
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
- **Select structure cutaways from uncut world geometry, but apply their cutoff per tile at draw and
  pick time.** Re-probing the already cut map can make a building alternate between cut and visible.
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
- **BitCanvas (`bitcanvas` v0.0.5; tooling v0.0.11; project v0.9.1) sends only game-ready terrain sheets.** The File System Access API is Chromium-only
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
witnesses/knowledge, tile physics, water simulation, NPCs, LLM, Jev, item degradation and content
beyond the nine v1 object kinds, and the city generator. Vector placeholders remain for materials
not mapped in `web/src/game/terrainSprites.ts`, and placeholder prisms remain for object kinds with
no BitCanvas sprite sheet.
