# CONTEXT.md

Current technical context of EtherBound: what exists, how it runs and where its pitfalls are.
Design lives in `docs/utils/VISION.md`.

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
| sim | `EtherBound.sln`, `sim/EtherBound.Sim/` (`Core/`, `Rng/`, `World/`, `Events/`, `Db/`, `Engine/`, `Clock/`), `sim/EtherBound.Sim.Tests/`, `sim/EtherBound.Bench/` | Dev-025 stage 2: the sim ported — `PyRandom` (MT19937), the world grid, A*, `test`/`lab`, population, SQLite persistence (Python's `0008_extra` schema), `WorldEngine.Submit`, all 18 op handlers, menus, the phased `EventBus`, `ExtrasBrain`. The `EtherBound.Host` project is a skeleton; `WorldEngine` is still invoked synchronously. 165 xUnit tests plus the golden suites pass. Navigation, snapshot and tick-write optimizations bring `EtherBound.Bench` to 64.864 ms average / 108.090 ms p95 for 1,000 Extras at x10 against the 100 ms average target. |
| game.app, game.render | `game/` (`project.godot`, `EtherBound.Game.csproj`, `spike/`) | Dev-025 stage 0 look spike: Godot 4.7.2 .NET, `net10.0`. Loads a JSON world dump and draws it with the ortho camera, iso-space shader, sun, SSAO and cutaway. Not wired to the sim yet. |
| tooling | root config: `package.json`, `global.json`, `.gitignore`, `.env.example`; `server/scripts/` | Build and check scripts, pinned .NET SDK, `export_world.py` (world dumps for `game/`). |

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
  in catalog order then candidate order. Each entry carries `tile_dx`/`tile_dy`, the offset from
  the origin tile of the tile whose candidate built it (self, held and worn are `0, 0`), and the
  payload lists `places`, one `{dx, dy, h, label}` per scanned tile. The client renders that
  payload either as the right-click list or as the radial menu on `V`, which groups the entries by
  op and places each verb's targets in their tile's screen direction around Niko; it never adds,
  removes or reorders an entry within a group.
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
  `GET /api/menu?x&y&z&radius` (any `z`, computed for Niko; `radius` (0 or 1, default 0) also scans
  the 3×3 square around the tile, each neighbour `in_close_reach` allows (a diagonal through
  either open side), for the radial menu;
  returns a `target` line like `Asphalt · 1 m`, `ops: MenuEntry[]` and `places: MenuPlace[]`;
  the client renders, never adds),
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
cd server && uv run python scripts/export_world.py   # dumps for the Godot spike
cd server && uv run python scripts/export_goldens.py # sim/EtherBound.Sim.Tests/Goldens
"$GODOT_BIN" --path game -- --world test-7 [--shots|--bench|--shimmer DIR]
npm run launcher:build        # Native AOT publish, copies EtherBound.exe to the root
EtherBound.exe                # server (hot reload) + web; keys O R W L H Q; logs in logs\
EtherBound.exe --no-reload    # no restart on saved server sources
EtherBound.exe --cleanup      # kill this checkout's leftovers; refuses while a launcher runs
dotnet run --project launcher/src    # while working on the launcher itself

# Schema and types
cd server && uv run etherbound-schema      # writes server/schema.json
cd web && npm run gen:types                # server/schema.json -> src/net/schema.d.ts

# Checks (COMMITS.md; hooks run check:fast on commit, check-versions on the message, check on push)
npm run check                   # versions, docs, sizes, server, web, bitcanvas, launcher, goldens, sim, game
npm run check:sim               # dotnet test sim/EtherBound.Sim.Tests
npm run check:game              # dotnet build game; headless Godot import when GODOT_BIN is set
npm run check:goldens           # re-export the goldens; fails if Python drifted
npm run check:fast              # versions (staged), docs, sizes, ruff, bitcanvas
npm run check:docs              # fails on a broken `X.md` [Sec. N] reference
npm run check:sizes             # warning-only scan: sources over 600 lines, doc budgets
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
it skips generated `web/src/net/schema.d.ts` and warns without failing. It also warns when a boot
doc exceeds its character budget (`CLAUDE.md` 2k, `AGENTS.md` 5k, `CONTEXT.md` 24k, `VISION.md`
17k) or a plan in `docs/` exceeds 220 lines. `check:docs` scans the living docs (root, `docs/`,
`docs/utils/`) and fails on a file-qualified section reference whose file or numbered heading does
not exist; references into `docs/done/` and ones quoted as inline code are skipped. Only
`server/src/etherbound/engine/ops/handling.py` and `BitCanvas/pixelart.js` currently exceed 600 lines.

## Pitfalls index

Measured pitfalls live in `docs/utils/PITFALLS.md`, one section per area. Read the sections for
the areas you touch.

| Sec. | Area | Read when touching |
|---|---|---|
| 1 | Launcher and Windows processes | `launcher/`, server start/stop, the database file |
| 2 | World, generation and grid | `server/src/etherbound/world/`, generator versions, Extras population |
| 3 | Movement, net and client sync | `server/.../net/`, `server/.../minds/`, `web/src/net/`, movement code |
| 4 | Objects and physics | `engine/ops/`, object kinds, `grid.py`/`ChunkStore` volumes |
| 5 | Renderer, atlas and assets | `web/src/game/`, `web/src/world/`, `web/src/ui/`, `src/sprites/` |
| 6 | BitCanvas | `BitCanvas/` |
| 7 | Tests and tooling | `scripts/`, Playwright, schema generation |
| 8 | Godot client | `game/`, shaders, camera, light |

## Not yet present

The other six primitives as data models, handlers for the 40 catalog ops that have none (`jump`,
`sit`, `lie`, `sleep`, `hide`, `search`, `watch`, `give`, `lock`, `unlock`, `use`, `eat`,
`drink`, `treat`, `fill`, `build`, `repair`, `ignite`, `extinguish`, `cook`, `craft`, `grab`,
`shoot`, the social, communication and trade ops, and `work`), modifiers, rolls,
witnesses/knowledge, water simulation, Agent brains and their needs, traits and utility (the
deterministic Extras exist), LLM, Jev, item degradation, content beyond the eleven object kinds of
`world/objects.toml`, and the city generator. Vector placeholders remain for materials not mapped
in `web/src/game/terrainSprites.ts`, and placeholder prisms remain for object kinds with no
BitCanvas sprite sheet.
