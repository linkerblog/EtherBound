# EtherBound: Pitfalls

[Guide] [Pitfalls]

Measured pitfalls, grouped by area. Read only the sections for the areas your change touches;
the index in `CONTEXT.md` maps paths to sections. Add a new pitfall to the section of its area.

## 1. Launcher and Windows processes

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
- Kill any zombie server process.

## 2. World, generation and grid

- **Roof slabs eat stair headroom.** A floor slab occupies its own `h` volume, so a stairwell
  needs a roof hole or the upper steps lose their 2 m of headroom and become unstandable.
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
- **Extras are seeded once, never refreshed.** `ensure_world` seeds them only when the save has no
  `kind = "extra"` rows; an existing Extra is settled like Niko, never moved home or renamed. So a
  population change (names, count, radius) does not touch an existing save, and a second open logs
  nothing.

## 3. Movement, net and client sync

- **A blocked movement axis must stay blocked for that input step.** Otherwise repeated substeps
  snap the actor between the wall and the 0.3 m radius limit; server movement and client prediction
  now clamp once and slide only along an unblocked axis.
- **The WebSocket hub should use action results for chunk tracking.** `ActionResult.x/y` already
  identify the resulting player chunk, avoiding a DB-backed `get_state()` on every movement input.
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
- **The brain bears a full submit per step and only proposes.** Every Extra move is a
  `submit(move, delta_seconds)` with its own transaction and `actor.moved` row; at x10 six Extras can
  mean hundreds of rows a minute, which the event log retention item must eventually bound. The brain
  keeps its path cache and stall retry in memory only, re-derived from state, so a restart at any
  tick replays to the same log.
- **`mind` is engine data, read defensively.** `get_state` and `inspect` tolerate a missing or
  malformed `mind` (no goal, `Standing`); only `set_goal` writes it. Niko has no `name` row and is
  shown as `Niko` by `actor_name`; the client draws only non-player actors, picked by `PLAYER_ID`.
- **The server sends each chunk once per connection.** The hub records it in `_known_chunks` and
  never re-sends it, so the client must cache and replay every chunk it receives to a late
  `onChunk` listener, or a scene that starts after `connect()` loses the world for good.
- **The client must reconnect.** Vite is ready seconds before the API server and hot reload restarts
  it, so a socket can fail to open or drop mid-session. `WebSocketClient` retries a closed socket
  with a backoff; without it the tab stays `OFFLINE` with a black world, because only a new
  connection gets the snapshot and the chunks.

## 4. Objects and physics

- **A solid object's volume starts ABOVE its resting `h`.** A kind with `height = 2` resting at `h`
  fills the cells `h+1` and `h+2`, and its standing/placing top is `h+2`. The server
  (`grid.py`) and client (`ChunkStore`) must agree exactly, or prediction and picking drift.
- **A non-solid object has no top to rest on.** `height = 0` kinds add no volume and cannot support
  anything, so an apple at your feet is taken, never "something is on it".
- **Object rows hide inside containers.** A closed container's contents are in the database but not
  in the chunk payload nor the menu; a changed tile object still bumps the chunk revision so the
  client re-reads the container's own `open`.

## 5. Renderer, atlas and assets

- **Hide the canvas with `visibility`, never `display: none`.** The Phaser game uses
  `Scale.RESIZE`, so a `display: none` parent collapses it to 0×0; the `DEBUG`/`LLM` views cover it
  and `.hidden-view` keeps it mounted, sized and running.
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
  position against uncut geometry. Slabs are drawn as 0.5 m boxes, so test their exposed south/east
  faces as well as their tops, and probe the whole sprite. Keep the cut for one tile beyond its last
  covering point and apply its cutoff per tile at draw and pick time; use an independent coverage
  oracle because probing the cut map can make a building flicker.
- **A front-wall stub needs a clear floor-plan line from Niko's tile centre.** The screen window
  alone cuts walls behind other shown walls; doorway edges are open and window walls still block.
- **Assets outside `web/` must be static imports.** Vite refuses a `new URL(…, import.meta.url)`
  request for a file outside its serving allow list, silently (Phaser just fails the load). A file
  in the module graph is let through, so the sheets in the root `src/sprites/` are imported
  statically in `web/src/game/terrainSheets.ts`; `terrainSprites.ts` keeps only the pure
  key → file-name table for node tests.
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
- **The terrain atlas side and wall bands each hold at most 8 materials** (512 px per material,
  4096 px per band). The side band and the wall band are separate rows in the same canvas, and a
  ninth sheet in either needs a wider or wrapped band; the loader warns and skips the overflow.
- **The object sprite atlas lives in its own band and shares the 4096 px guard.** Kinds drawn as a
  placeholder prism (no sheet) still take their slot's draw order inside the tile batch.

## 6. BitCanvas

- **BitCanvas sends only game-ready terrain sheets.** The File System Access API is Chromium-only
  and requires a user-picked directory named `sprites` containing a `grass` or `floor` directory.
  A send overwrites files: `git restore src/sprites` restores tracked sheets, but newly created
  material PNGs are untracked and need separate cleanup if they were only test outputs. New material
  sheets still require entries in `terrainSprites.ts` and static imports in `terrainSheets.ts`;
  BitCanvas never edits game source. The manual Vite reload check was not run.

## 7. Tests and tooling

- **`schema.json` is not committed.** It is generated (`uv run etherbound-schema`); `gen:types`
  fails if the file is missing. Only `schema.d.ts` is committed.
- **A paused clock paints late in the software renderer.** With no tick after the snapshot the first
  canvas paint can lag a few seconds under swiftshader, so `check:visual` waits before the x1 shot;
  this is a screenshot settle, not a game-render change.
- **Pytest warnings are third-party** (FastAPI/Starlette/pytest-asyncio deprecations), not project
  issues.

## 8. Godot client

- **`cull_disabled` flips `NORMAL` on faces seen from behind.** A quad wound the other way loses
  the sun. The terrain shader passes the mesh normal through a varying (view space, via
  `MODEL_NORMAL_MATRIX`) and writes it in `fragment()`, so light never depends on winding.
- **In a custom `light()`, `LIGHT_COLOR` needs `/ PI`** (Godot 4.7). Without it the sun is π times
  too strong; the stage-0 calibration measures tops at exactly the sheet colour with it.
- **Coplanar faces z-fight under the ortho camera.** A slab's or the ground's edge face in the plane
  of a wall's face shows as dark streaks; the mesher drops faces covered by a wall on that edge.
- **Height is scaled on the render root, not in the meshes.** Meshes are built in metres
  (`x`, `h / 2`, `y`); the root's `Y` scale √(2/3) turns 0.5 m into 16 px. Snap the camera in that
  scaled space, and keep the SubViewport size even so tile corners land on pixel corners.

