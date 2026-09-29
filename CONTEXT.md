# CONTEXT.md

Current technical context of EtherBound: what exists, how it runs and where its pitfalls are.
Design lives in `docs/utils/VISION.md`.

## Modules

Ported from a Python/FastAPI server and a Phaser/React web client to a deterministic C# simulation
and a Godot 4 client (`docs/done/Dev-025.md`, cut-over 26/09/2026); the earlier stack is archived at
`legacy-python-web-stack.zip`, and every rule below carried over unchanged, proven by goldens
exported from the old server before cut-over. The measured reason: 1,000 Extras averaged
5,645 ms/tick in Python versus 64.864 ms/tick in the release C# sim (`EtherBound.Bench`), ~87×
faster, at clock speed 10.

| Module | Path | Responsibility |
|---|---|---|
| sim.rng | `sim/EtherBound.Sim/Rng/` | `PyRandom`, a bit-exact port of CPython's MT19937; `RngStream`/`RNGStreams` — one seeded stream per system. |
| sim.world | `sim/EtherBound.Sim/World/` | Material registry, validated object kinds, chunk/grid geometry, A*, the generator registry and seeded generation (`test` and `lab`). |
| sim.events | `sim/EtherBound.Sim/Events/`, `sim/EtherBound.Sim/Core/` | Typed committed events, FIFO subscriber bus, transactional sequence persistence, and core primitives (`Ids`, `GameAction`, target types). |
| sim.db | `sim/EtherBound.Sim/Db/` | `Microsoft.Data.Sqlite` schema, the stepwise migration runner, load/save. |
| sim.engine | `sim/EtherBound.Sim/Engine/`, `sim/EtherBound.Sim/Clock/` | The only state writer: `WorldEngine` orchestration, action/target types, generated menus, every handler, deterministic SI physics, trajectory resolution, `Pick` (ray casting for the client) and `SimClock`. |
| sim.minds | `sim/EtherBound.Sim/Minds/` | Decision sources that propose through the action API; today `ExtrasBrain`, the deterministic routine of an Extra. |
| sim.host | `sim/EtherBound.Host/` | `SimulationHost`: the sim's single writer thread, a bounded command channel (move, clock, actions, menu/pick queries, new game) and detached, revisioned `WorldFrame`s the client reads without a lock. `ActorMotion` and `StepPlayout`: the client's presentational interpolation between frames. |
| game.app | `game/app/`, `game/project.godot`, `game/EtherBound.Game.csproj`, `game/export_presets.cfg` | `WorldClient`: starts `SimulationHost` and sprite decoding before scene setup, shares actor render resources, and applies host frames; dispatches CPU chunk geometry builds while keeping Godot resource/node work on the main thread. Includes WASD input and `--shots`. |
| game.render | `game/spike/` | `PixelView` (low-res `SubViewport`, orthographic camera, art-pixel snapping), `ChunkMesher.BuildGeometry` (parallel CPU vertex lists) / `ToMeshes` (main-thread `ArrayMesh`), `Terrain.gdshader`, furniture voxel meshes, `Cutaway`, `FurnitureLibrary`, `WorldDump`. |
| game.ui | `game/ui/` | `GameHud` (`GAME`/`DEBUG`/`LLM` tabs, `CLOCK`/`FEED`/`CARRY`/`ACT` panels, input line, `NEW` popover), `ActionMenuOverlay` (right-click list, `V` radial menu), `GeneratorPanel` (`NEW`/`MAP` forms from `GeneratorSpec`), `TrajectoryAnimator`, `CompassOverlay` (iso north/east/south/west and `CAM` marker, to name cutaway faces). |
| bitcanvas | `BitCanvas/` (`pixelart.js`, `gamesync.js`, `core.js`, `terrain.js`, `sides.js`, `furnitureData.js`, `furniture.js`, `app.js`) | Standalone seeded texture and furniture generator (classic deferred scripts, HTML/JS, no build); furniture exports to `game/assets/furniture/` via `scripts/export-furniture.mjs`, and "Send to game" targets `game/assets/sprites/`. |
| tooling | root config: `package.json`, `global.json`, `.gitignore`; `scripts/` | Build and check scripts, pinned .NET SDK, `export-furniture.mjs`, and `publish-game.mjs` for ReadyToRun Windows exports. |

## Data model

SQLite at `data/etherbound.db` (gitignored), opened by `sim/EtherBound.Sim/Db/` through
`Microsoft.Data.Sqlite`; the engine keeps authoritative state in memory and commits a session's
changes in one transaction. File saves run in WAL with `synchronous=NORMAL` and no connection
pooling (Fix19): a step commits in about 0.7 ms, a power cut can drop the last moments but never
corrupts, and the `-wal` file is folded back on close. Migrations `0001_initial`, `0002_world`, `0003_event`,
`0004_dig_activity`, `0005_object`, `0006_physics`, `0007_generator` and `0008_extra` are unchanged
from the retired Python server (`legacy-python-web-stack.zip`); the sim's own chain starts at
`0009_walls` (Dev-036). `Database.EnsureSchema` is a stepwise runner: a missing save is created from
the embedded `Schema0008.sql` at `0008_extra`, then every pending step is applied in order from the
save's own `alembic_version`, one transaction each, and the head is written; a version the runner
does not know is refused instead of guessed. The sim also records its own `sim_schema` row.

| Table | Columns |
|---|---|
| `world_meta` | `id` (pk, always 1), `seed`, `game_minute`, `speed` (1/3/10), `paused`, `gen_version`, `generator` (default `test`), `gen_options` (JSON, default `{}`) |
| `actor` | `id` (pk), `kind` (`player` or `extra`), nullable `name`, `x`, `y`, `z` (derived `h // 6`), `h` (half-metres), `mass_kg` (default 80), nullable JSON `activity` (`op`, `action`, `started_minute`, `ends_minute`), nullable JSON `mind` (`anchor {x, y, h}`, `goal {kind: wander, x, y, h}` or null) |
| `material` | append-only `id` ↔ `key` mapping plus rendering/physics properties |
| `chunk` | pk `(cx, cy)`; blobs `ground_h` (int16×1024), `surface_mat` (uint16×1024), nullable `dug` (uint8×1024, NULL = all zeros), `strata` JSON, `revision`, `gen_version` |
| `chunk_level` | pk `(cx, cy, z)`; blobs `floor_h`, `floor_mat`, `wall_n`, `wall_w`, `edge_flags`, `flags`, nullable `slot_mask` (uint8, NULL = all zeros) and `slot_mat` (uint16) for the six wall slots |
| `object` | `id` (pk), `kind`, `loc` (`tile`/`in`/`held`/`worn`), nullable `x`/`y`/`h`/`cx`/`cy`, nullable `container_id` (self-FK), nullable `actor_id`, nullable `slot`, `quantity` (> 0), JSON `state`, nullable `integrity`, nullable `owner`; a CHECK pins the exact columns of each `loc` |
| `wall_slot` | sparse pk `(cx, cy, z, cell_index, slot)` for partly damaged walls, edge and interior alike; stores remaining joules |
| `event` | `seq` (global ordered pk), `game_minute`, `type`, nullable `actor_id`, JSON `data`; indexed by minute, type and actor |

Spatial units: 1 m tiles in 32×32 chunks; `h` in half-metres; `z` is the absolute 3 m band
`floor(h / 6)`; walls live on tile edges (each tile owns north/west) with doorway/window edge
flags, and each cell also carries a `slot_mask` byte for the six wall slots `N`, `W` and the
interior `H`, `V`, `D1`, `D2` (1, 2, 4, 8, 16, 32) that phases 2 and 3 of Dev-036 fill;
below the surface everything is implicit strata until a `void` flag excavates it. Strata
depth is measured from the original ground (`ground_h + dug`), so digging exposes deeper layers
instead of dragging them down. The test
world (`gen_version = 5`) is 8×8 chunks with hills, a road (spawn at 121.5, 128.5, h=2), a terrace
with a ramp, a building with a graded approach, west doorway, accessible basement, first floor at
h=18, roof at h=24 and a pond with a park pit. It also lays out objects in a fixed order: a shovel
(124, 126, h=2), a backpack (125, 126, h=2), a closed chest (124, 127, h=2) holding apple ×3 and
bottle ×2, a table (137, 133, h=12) with a bottle on its top, two chairs, a shelf with apple ×2, a
barrel (all on the building's ground floor), two chests stacked at (126, 127) (the upper resting
on the lower's top at h=3), and a sledgehammer at (126, 126). `NewGame` wipes actors, objects,
chunks and levels and regenerates from the seed; `EnsureWorld` fills an existing save without a
wipe, keeping held and worn objects with their contents and deleting the uncarried ones before
re-laying the v5 layout. On load, a saved actor within
0.5 m of a standing surface in its tile is snapped to that surface's exact `h` (no event);
further off, it is relocated to spawn (`actor.spawned`, `relocated`).

**Extras.** Each world also carries six Extras. `World/Population.cs` places them from the
`population` RNG stream (names from `Data/names.toml`, and walkable standing ground tiles within
15 m of the spawn, reachable by `Nav.FindPath`, at least 2 m apart); population is a pure grid pass
that touches no database. The engine writes each Extra as an `actor` row `extra-NNN` with
`kind = "extra"`, its `name`, its position and `mind = {anchor, goal: null}` where the anchor is
its start tile; it logs `actor.spawned` per Extra. `NewGame` creates Niko and the Extras;
`EnsureWorld` settles every existing actor (same snap or `relocated` rule) and, only when the save
has no Extras, seeds them, so a pre-`0008_extra` save gains them on first open and a second open
emits nothing. Population is deliberately not part of `GeneratedWorld`: population is actors, not
terrain, and never changes the test world's bytes.

**Generators.** `World/Generators.cs` holds the registry: `test` (version 5, `World/TestWorld.cs`,
unchanged) and `lab` (version 1, `World/Lab.cs`). A `GeneratorSpec` carries `key`, `name`,
`version`, an options record, `Generate(seed, options, registry) -> GeneratedWorld` and a pure,
cheap `Spawn(seed, options)`. `GeneratedWorld.cs` holds the chunks, levels, objects, spawn, version
and generator key; the test world generator keeps its byte output. `NewGame` validates the options,
stores `generator` and the full options dump, generates through the spec and logs `world.generated`
with both. `EnsureWorld` resolves the save's generator (an unknown key falls back to `test` with a
warning) and regenerates when `gen_version` is below the spec version; the spec's own `Spawn` runs
instead of the generator running again on load. The `lab` map is a 4×4-chunk (128 m) flat fixture
with asphalt paths and nine 36×36 bays — steps, materials, water, structure, feature, objects, dig,
walls and open — around central bay `feature`, where a feature (today only `relief`, noise hills)
is stamped through a `GenCanvas` (`World/GenCanvas.cs`); the default spawn is the path north of
the feature bay. Every published `WorldFrame` carries `Generators` (`HostGenerator`,
`HostOptionField`: path, label, kind, default, min, max, step, choices, group) and, for `lab`, its
`HostGeneratorBay`s; the Godot `MAP`/`NEW` forms (`GeneratorPanel`) build themselves from this data.

## Contracts

- **Action API.** `WorldEngine.Submit(actorId, action, deltaSeconds)` is the single mutation entry
  point: pause check → a zero-length `move` returns accepted and does nothing → handler
  `Validate` (a rejection changes nothing and emits nothing) → a running activity is cleared with
  `activity.finished` (`interrupted`) → an instant op resolves (events and an optional `text`), a
  durative one stores an activity and emits `activity.started` → transactional commit and event
  enqueue → FIFO dispatch on the sim thread. Actions are a union discriminated by `op`;
  targets are `self`, `tile {x, y, h}`, `object {id}`, `actor {id}` or
  `edge {x, y, z, direction}`; `put` carries a second `into` target.
- **Ops.** `Data/ops.toml` is the vocabulary (key, label, group, target kinds, tags); a handler
  (`Applies`, `Builds`, `Subject`, `Validate`, `Duration`, `Resolve`, `Complete`) gives an op
  behaviour, and registration refuses a key missing from the catalog. One target can `Builds` several
  actions and `subject` names what an entry acts on. Handled: `move` (never in menus), `inspect`
  (instant, 30 m, tile, object or actor text, no event), `wait` (15 min), `dig` (ground surface only,
  `ceil(30 × dig_cost / tool)` min per 0.5 m with the best held `tool.dig` or 0.25 bare-handed,
  `dig_cost ≤ 2`, refused when an object rests at the ground `h`), `climb`, `build` (a wall on a
  tile edge or a floor on a tile, `ceil(30 × build_cost × slot length / tool)` min with the best
  held `tool.build` or 0.25 bare-handed; the material is the surface the build stands on and a
  material with no `build_cost` is not a building material, so the spot is refused), the instant handling
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
  5 m/s; horizontal loss per metre is `0.05 × mass × 9.81` J.   `Object.integrity` is remaining J
  (NULL means intact at material resistance × max(1, height) half-metre cells); wall edges span six
  cells and partial damage persists in
  `wall_slot`, keyed by slot so an edge and an interior slot share one namespace. Zero integrity turns objects into data-defined rubble (spilling container
  contents) or opens wall edges. Falls resolve in the same action; bodies over a 3 m fall emit
  potential energy as `impact`. Physics never mutates health. The client only animates the sim's
  trajectory (`TrajectoryAnimator`); it never resolves physics itself.
- **Activities.** One per actor, stored on the actor, advanced only by clock ticks. In
  `AdvanceTime`, actors whose `ends_minute` has come are completed in id order before
  `clock.ticked`: the action is re-validated, and either `complete` applies its events then
  `activity.finished` (`completed`), or `activity.finished` (`failed`, reason). A paused clock
  freezes them; progress is lost on interruption.
- **Minds.** `WorldEngine.SetGoal(actorId, goal, reason)` is the only writer of `mind.goal`: it
  refuses the player, an unknown actor and a goal tile without a standing surface, sets the goal
  (or clears it) and commits `actor.goal_set` (`reason` chosen/arrived/stuck/unreachable) in the same
  transaction as the actor row. `ExtrasBrain`, subscribed to `clock.ticked` before the host publishes
  its frame, walks each idle Extra toward its goal with `Submit(move, deltaSeconds)` at 4 m/s per
  real second (`timeScale / speed` per tick, at most six move submits) and otherwise picks a
  `wander` goal within 12 m of its anchor (60 % of idle ticks) or waits; it re-derives its path from
  state, so a restart replays it. A stalled step recomputes once, then `SetGoal(null, "stuck")`. The
  brain only proposes; the engine validates and writes every step.
- **Menus.** `WorldEngine.Menu(actorId, x, y, z)` is a read (no lock): candidates are the tile's
  standing surface for `z`, `self` and the objects lying on it at that surface's band, plus the
  contents of its open or lidless containers; on the actor's own tile, also every held and worn
  object and the contents of worn open containers; it also offers actors standing on the clicked tile
  at that surface's band and existing north/west wall edges as physics targets, plus — on the actor's
  own tile at the band being read — that tile's two bare edges, so a wall can be built where there is
  none yet. Each handled op whose targets allow a candidate
  and whose `applies` holds becomes an entry with its `action`, `available`, `reason` and `subject`,
  in catalog order then candidate order. Each entry carries `tile_dx`/`tile_dy`, the offset from
  the origin tile of the tile whose candidate built it (self, held and worn are `0, 0`), and the
  payload lists `places`, one `{dx, dy, h, label}` per scanned tile. The client renders that
  payload either as the right-click list or as the radial menu on `V`, which groups the entries by
  op and places each verb's targets in their tile's screen direction around Niko; it never adds,
  removes or reorders an entry within a group.
- **Event bus.** Only `WorldEngine` stamps/enqueues events. Logged events share the state
  transaction; `clock.ticked` dispatches but is not stored. `NewGame` resets the log and sequence
  to 1. A fresh world logs `world.generated`, one `actor.spawned` for Niko and each Extra, then
  `clock.changed`; opening an existing save emits nothing, except the Extra spawns when it had none.
  A drain call made while another is already draining returns immediately; the active one dispatches
  the caller's events in `seq` order, possibly after the caller has returned. Nothing guarantees
  subscribers have run when `Submit`, `SetClock` or `NewGame` returns. Handlers run in subscription
  order; reentrant events append to the active FIFO drain. Handler failures are logged and isolated;
  a cascade is capped at 10,000 events. `seq` is unique among stored events; a transient event's
  `seq` may be reused after a restart, so never key state on it.
- **The host contract (`sim/EtherBound.Host/`).** There is no REST or WebSocket: `SimulationHost`
  owns the sim on one dedicated thread; Godot enqueues commands on a bounded channel (`Move`,
  `Clock`, `NewGame`, `SubmitAction`, `MenuQuery`/`MenuRay`/`RadialMenu`, `PickRay`) and reads an
  immutable, atomically-published `WorldFrame` — sequence, clock, every actor, every chunk near the
  player (radius 2, revisioned so an unchanged chunk is not resent), the material/object-kind
  catalog and the generator list — with no lock. Responses to request-carrying commands (actions,
  menus, picks, new game, errors) arrive on a separate unbounded channel, matched to the client's
  own request id (`HostResponse.RequestId`); `HostActivityNotice` for a finished player activity has
  no request id and is pushed as it happens. `WorldEngine.Pick(ray)` (sim-space ray cast: tiles,
  wall edges, actor bodies, object volumes) backs both picking and the menu-at-ray query, so the
  Godot camera's cursor ray always agrees with the sim's own geometry. Movement is stepped at
  `SimulationHost.MoveHz` (20, unchanged from the old 20 Hz WS input), independent of the 1 Hz
  clock; there is no client-side prediction, since there is no network latency to hide. The client
  draws Extras through `ActorMotion`, interpolating each new position over one clock tick, and Niko
  through `StepPlayout`, which places each WASD step on its ideal 50 ms schedule using the frame's
  `MovesApplied` (every `Move` the host has handled). Other player moves fall back to `ActorMotion`.
- **Standing rule (shared).** Step at most 0.5 m (`Δh ≤ 1`), keep the three half-metre cells
  `h+1..h+3` clear (a slab at `h+4` touches but does not intersect a 2 m body), no wall on the shared
  edge, and at least 0.3 m body-centre clearance from blocked edges. A solid object's cells count as
  blocked and a covered surface loses its headroom; a surface object's top `h+height` is an
  additional standing surface. Diagonals validate both
  height-consistent L routes. Navigation adds same-column `LEVEL_CLIMBABLE` links; normal player
  movement still uses the shared action API. Speed: ×0.6 up, ×0.85 down, divided by `walk_cost` and
  multiplied by the carried `load_multiplier`.
- **Types.** The sim's C# types are the only definition; `game/` links the sim assembly directly, so
  there is no schema export or generated client type step.

## Commands

```text
# One-time per clone: enable the versioned git hooks
npm run setup

npm run bitcanvas                      # open the standalone texture/furniture generator
npm run export:furniture               # BitCanvas/furnitureData.js -> game/assets/furniture/*.json
dotnet build EtherBound.sln            # sim, host, tests, bench, game
dotnet run --project sim/EtherBound.Bench -c Release   # 1,000/5,000/10,000-Extra tick benchmark
"$GODOT_BIN" --path game -- [--seed N] [--generator test|lab] [--database PATH] [--shots DIR] [--trace-walk CSV]
node scripts/trace-walk.mjs CSV        # walking smoothness from a --trace-walk run (Fix19)
node scripts/publish-game.mjs [--export DIR] # ReadyToRun assemblies -> existing Windows export

# Checks (COMMITS.md; hooks run check:fast on commit, check-versions on the message, check on push)
npm run check                   # versions, docs, sizes, bitcanvas, furniture, sim, game
npm run check:sim               # dotnet test sim/EtherBound.Sim.Tests
npm run check:game              # dotnet build game; headless Godot import when GODOT_BIN is set
npm run check:furniture         # re-export furniture; fails if BitCanvas drifted
npm run check:fast              # versions (staged), docs, sizes, bitcanvas
npm run check:docs              # fails on a broken `X.md` [Sec. N] reference
npm run check:sizes             # warning-only scan: sources over 600 lines, doc budgets
```

`GODOT_BIN` points at the Godot 4.7.2 (.NET/mono) editor executable; `--seed`/`--generator` default
to `7`/`test`, and `--database` defaults to the user data dir (so a plain run keeps using the same
save). `check:game`'s headless import only runs when `GODOT_BIN` is set; otherwise it just builds.

`check:sizes` scans source files under `BitCanvas`, `sim` and `game`, and warns without failing.
It also warns when a boot doc exceeds its character budget (`CLAUDE.md` 2k, `AGENTS.md` 5k,
`CONTEXT.md` 24k, `VISION.md` 17k) or a plan in `docs/` exceeds 220 lines. `check:docs` scans the
living docs (root, `docs/`, `docs/utils/`) and fails on a file-qualified section reference whose
file or numbered heading does not exist; references into `docs/done/` and ones quoted as inline
code are skipped. Only `BitCanvas/pixelart.js` currently exceeds 600 lines.

## Pitfalls index

Measured pitfalls live in `docs/utils/PITFALLS.md`, one section per area. Read the sections for
the areas you touch.

| Sec. | Area | Read when touching |
|---|---|---|
| 1 | Godot process and the database file | `GODOT_BIN`, running the client, the database file |
| 2 | World, generation and grid | `sim/EtherBound.Sim/World/`, generator versions, Extras population |
| 3 | Movement and the host channel | `sim/EtherBound.Host/`, `sim/EtherBound.Sim/Minds/`, movement/picking code |
| 4 | Objects and physics | `sim/EtherBound.Sim/Engine/`, object kinds, grid volumes |
| 5 | Renderer, shaders and furniture meshes | `game/spike/`, `game/assets/`, shaders |
| 6 | BitCanvas | `BitCanvas/`, `scripts/export-furniture.mjs` |
| 7 | Tests and tooling | `scripts/`, `dotnet test`, export-and-diff checks |
| 8 | Godot client | `game/`, shaders, camera, light |

## Not yet present

The other six primitives as data models, handlers for the 39 catalog ops that have none (`jump`,
`sit`, `lie`, `sleep`, `hide`, `search`, `watch`, `give`, `lock`, `unlock`, `use`, `eat`,
`drink`, `treat`, `fill`, `repair`, `ignite`, `extinguish`, `cook`, `craft`, `grab`,
`shoot`, the social, communication and trade ops, and `work`), modifiers, rolls,
witnesses/knowledge, water simulation, Agent brains and their needs, traits and utility (the
deterministic Extras exist), LLM, Jev (its C# runtime is an open question, `docs/PENDING.md`), item
degradation, content beyond the eleven object kinds of `Data/objects.toml`, and the city generator.
The plain object box remains for kinds with no BitCanvas furniture piece (tools, food, `rubble`).
