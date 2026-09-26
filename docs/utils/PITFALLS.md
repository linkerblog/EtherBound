# EtherBound: Pitfalls

[Guide] [Pitfalls]

Measured pitfalls, grouped by area. Read only the sections for the areas your change touches;
the index in `CONTEXT.md` maps paths to sections. Add a new pitfall to the section of its area.

## 1. Godot process and the database file

- **`GODOT_BIN` is not on `PATH` by default.** Point it at the Godot 4.7.2 (.NET/mono) editor
  executable before `check:game`'s headless import or any windowed run; unset, `check:game` still
  builds but skips the import.
- **`--headless` never renders.** It disables the display and rendering server outright, so a scene
  that awaits `RenderingServer.FramePostDraw` (the `--shots` capture loop) hangs forever waiting for
  a signal that will never fire. Screenshots and any other GPU-visible check need a real windowed
  run (`"$GODOT_BIN" --path game -- ...`, no `--headless`); `--headless --import` is for the
  script/scene import check only.
- **The database file stays locked while a Godot session runs.** `SimulationHost` opens
  `data/etherbound.db` for the life of the process; close the running client before touching the
  file directly.
- Kill any zombie Godot process (`Stop-Process` by id) left over from a hung or crashed run before
  starting another one against the same database.

## 2. World, generation and grid

- **Roof slabs eat stair headroom.** A floor slab occupies its own `h` volume, so a stairwell
  needs a roof hole or the upper steps lose their 2 m of headroom and become unstandable.
- **Levels are indexed per chunk.** Tile standing/solidity lookups use the chunk's own sparse level
  map rather than scanning every level in the world. The optional `WorldGrid.chunk()` loader caches
  hits and misses; the current test world is eagerly loaded before movement.
- **Test-world changes require a generator version bump.** `EnsureWorld` regenerates an older
  generator's chunks and preserves actor rows; it does not wipe the save or change the schema. Bump
  the generator's `version` in its spec, and every save regenerates with its stored options.
- **A generator's options are data, not code.** Every `WorldFrame` carries each generator's fields
  (`HostGenerator.Fields`); adding a field to the C# options record makes it appear in the `MAP`
  form, and the client never names an option. To add a feature, follow `World/Lab.cs`'s existing
  `feature` choices and bump the `lab` version.
- **An unknown generator row falls back to `test`.** `EnsureWorld` logs a warning, resets
  `generator`/`gen_options` and regenerates, so an old or hand-edited save always opens.
- **Edge walls belong to the tile that owns the edge.** A wall west of tile (1,0) is `wall_w[1]`
  of the same chunk; chunk-border walls are stored by the neighbouring chunk's first column.
- **Walls stay thin planes on their edge for every system.** Physics, picking, occlusion and the
  cutaway treat a wall as its tile edge. `WALL_T` (1/4 m) and the top strip, end faces and corner
  post are render-only, drawn outward behind the visible face, so nothing moves on the plane.
  Wall-face masks hang from their draw height (the run top), like ground faces.
- **Spawn must come from the generator's road.** A first-walkable-tile scan starts in the map
  corner, where radius-2 chunk streaming only finds 9 chunks instead of 25.
- **A* needs two guards.** A goal whose tile has no standing surface must return `null`
  immediately, and unreachable sweeps need an expansion cap, or a probe can run for minutes.
- **Extras are seeded once, never refreshed.** `EnsureWorld` seeds them only when the save has no
  `kind = "extra"` rows; an existing Extra is settled like Niko, never moved home or renamed. So a
  population change (names, count, radius) does not touch an existing save, and a second open logs
  nothing.

## 3. Movement and the host channel

- **There is no prediction, and none is needed.** WASD submits a `Move` command straight through
  `SimulationHost`'s bounded channel to the same thread that owns the world; the client only ever
  draws the frame the sim already committed, and the capsule/camera interpolation in `_Process` is
  presentational only, never a source of truth.
- **A full channel drops the command, silently, unless the caller checks.** `TryMove`,
  `TryRequestRadialMenu`, `TrySubmitAction` and friends all return `bool`; a `RunShots`-style script
  that ignores the result can wait forever on a response that was never enqueued. Push a HUD warning
  (`SIM BUSY`) on `false`, as `WorldClient._UnhandledInput` does for the right-click and `V` paths.
- **A held Godot input key is read every `_Process` frame, not on key-down.** `SendMovement`
  accumulates real time and steps `TryMove` at a fixed `1/MoveHz` interval, so frame-rate hitches
  neither skip nor double a step; a blocked axis simply returns `false` from the sim's own move
  handler, exactly as a network client's rejected input once did.
- **A cursor ray must agree with the sim's own geometry, not the render mesh.** `WorldClient`
  builds a `WorldRay` from `PixelView.RayFromScreen` and always resolves it through
  `WorldEngine.Pick` (sim thread), never against Godot physics or mesh collision shapes, so picking
  a wall, an actor or an object top matches what the standing rule already knows.
- **`Godot.Vector3` and `WorldRay` do not share an axis order.** Godot's ray is `(x, y-up, z)`; the
  sim's is `(x, y, height)`. `WorldClient` maps `origin.X, origin.Z, origin.Y` (and the same for the
  direction) — swap two of those and picking silently offsets or inverts.
- **`mind` is engine data, read defensively.** `GetState` and `inspect` tolerate a missing or
  malformed `mind` (no goal, `Standing`); only `SetGoal` writes it. Niko has no `name` row and is
  shown as `Niko`; the client draws every actor from the frame, keyed by `Ids.Player`.
- **The brain bears a full submit per step and only proposes.** Every Extra move is a
  `Submit(move, deltaSeconds)` with its own transaction and `actor.moved` row; at x10 six Extras can
  mean hundreds of rows a minute, which the event log retention item must eventually bound. The
  brain keeps its path cache and stall retry in memory only, re-derived from state, so a restart at
  any tick replays to the same log.

## 4. Objects and physics

- **A solid object's volume starts ABOVE its resting `h`.** A kind with `height = 2` resting at `h`
  fills the cells `h+1` and `h+2`, and its standing/placing top is `h+2`. The sim's grid and its
  render-only projection (`WorldDump` in `game/spike/`) must agree exactly, or picking drifts.
- **A non-solid object has no top to rest on.** `height = 0` kinds add no volume and cannot support
  anything, so an apple at your feet is taken, never "something is on it".
- **Object rows hide inside containers.** A closed container's contents are in the database but not
  in the chunk payload nor the menu; a changed tile object still bumps the chunk revision so the
  client re-reads the container's own `open`.

## 5. Renderer, shaders and furniture meshes

- **VOID is a ground-volume flag, not a missing-floor flag.** `ChunkMesher` and `Cutaway` render
  and pick stored floors even when their band is VOID; suppress only a ground top whose own band
  is void. A slab resting on the solid ground still counts as ground for the cutaway ray, or
  clipping it opens a hole through an excavated column (`ChunkMesher.Build`'s `rests` check).
- **Touching voxels z-fight unless the shared face is culled.** Two adjacent furniture voxels each
  drawing a face on the exact same plane flicker under the ortho camera depending on draw order.
  `FurnitureMesh` looks up each of the piece's own 5 neighbour cells (`FurniturePiece.Occupied`) and
  skips a face when the neighbour is present; the bottom face is always skipped, matching the plain
  object box.
- **A wall is a thin plane on its tile edge for every system.** Physics, picking, occlusion and the
  cutaway all treat it that way. `WallT` (1/4 m) and the top strip, end faces and corner post are
  render-only, drawn outward behind the visible face, so nothing moves on the plane.
- **The occlusion ray follows Niko's body, not a shortened feet ray**, and does the front-wall-stub
  job by itself. `Cutaway.ClipH` walks a ray from Niko toward the camera over the sim grid
  (`Occluded`) and separately checks what roofs or covers Niko's own tile (`Roofed`); there is no
  separate "front wall" rule to keep in sync — a wall between the camera and Niko is just another
  occluder on that same ray.
- **The iso-space shader only samples a texture when `layer >= 0`.** Furniture and the plain object
  box pass `topLayer = -1`, so `Terrain.gdshader` takes the `col = base * tint` branch and just
  tints the vertex `COLOR.rgb`; a voxel's alpha does not need a face-code encoded into it the way a
  terrain top does; `height_tint` still applies unless the face code is 3 or 4 (a wall).
- **`cull_disabled` flips `NORMAL` on faces seen from behind.** A quad wound the other way loses
  the sun. The terrain shader passes the mesh normal through a varying (view space, via
  `MODEL_NORMAL_MATRIX`) and writes it in `fragment()`, so light never depends on winding.
- **Assets load from the filesystem, not `res://`.** `WorldClient`/`ChunkMesher` read
  `game/assets/sprites/` and `game/assets/furniture/` with plain `Path.Combine` and
  `Image.LoadFromFile`/`File.ReadAllText`, bypassing Godot's resource importer entirely; this works
  at runtime but means the headless `--import` check does not catch a missing or malformed asset
  file, only script/scene errors. A GPU run (`--shots`) is still the only thing that proves a new
  asset actually renders.

## 6. BitCanvas

- **BitCanvas sends only game-ready terrain sheets.** The File System Access API is Chromium-only
  and requires a user-picked directory named `sprites` containing a `grass` or `floor` directory.
  A send overwrites files: `git restore game/assets/sprites` restores tracked sheets, but newly
  created material PNGs are untracked and need separate cleanup if they were only test outputs. New
  material sheets still require an entry in `WorldClient.Sheets` (`game/app/WorldClient.cs`);
  BitCanvas never edits game source.
- **Furniture data is exported, not hand-copied.** `scripts/export-furniture.mjs` evaluates the real
  `BitCanvas/furnitureData.js` (a `vm` sandbox, no DOM) rather than duplicating its `FURNITURE`
  table, so a shape change there only needs `npm run export:furniture`; `check:furniture` catches a
  forgotten re-export. A `vm`-executed script's top-level `const`/`let` bindings are not own
  properties of the sandbox object — read them back with another `vm.runInContext("NAME", sandbox)`
  call, not `sandbox.NAME`.

## 7. Tests and tooling

- **An "export and diff" check only catches drift it is actually run against.** `check:furniture`
  (BitCanvas → `game/assets/furniture/`) follows the same shape as the old `check:goldens`
  (Python → `sim/EtherBound.Sim.Tests/Goldens`): it re-exports and fails on an uncommitted diff, so
  a source change without a re-export passes locally and only fails at the next check.
  `check:goldens` itself retired with the Python server; the committed golden JSON is now a frozen
  fixture the C# tests (`WorldGenerationGoldens.cs`, `RngParity.cs`, …) compare against directly.
- **`check:game`'s headless import is a script/scene check, not a render check.** It catches a
  missing type or a broken `.tscn` reference, never a blank screenshot or a wrong colour; those need
  a windowed GPU run (`--shots`), which is manual or agent-launched outside `npm run check`.
- **A request/response wait in a capture script needs its own timeout budget, checked.** The
  `--shots` script waited only 2 real seconds (120 frames) for a menu's `HostMenuResponse` before
  saving the screenshot regardless, which could silently save a blank frame on a slow tick; it now
  waits 5 seconds and logs a warning (`GD.PrintErr`) if the wait actually times out, so a future
  regression fails loud instead of shipping a quietly-wrong baseline.
- **`dotnet test`/`dotnet build` are the only test runners now.** There is no Python or web test
  suite left to run; `npm run check` only touches `.NET`, Node syntax checks and the doc/version
  scripts.

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

