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
- **A save is WAL while open (Fix19).** `Database` sets `journal_mode=WAL` and `synchronous=NORMAL`
  for file saves, so the newest commits sit in `etherbound.db-wal` until the session closes. Copying
  only the `.db` of an open save gets an old snapshot; copy the `-wal` too, or close the client first.
  `Database` does not pool connections, so a clean `Dispose` folds the WAL back and removes it. A
  leftover `-wal` means a session did not close cleanly; SQLite replays it on the next open.
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
- **Interpolate over the update interval, never chase at a fixed speed.** Niko's position changes
  only `MoveHz` (20) times a second, 0.2 m per step. A fixed-speed chase faster than walking
  (the old `MoveToward(12 m/s)`) covers a step in one frame and then waits, so the camera and the
  world stutter even at 75 fps (Fix18). `ActorMotion` (`sim/EtherBound.Host/`) draws a linear
  segment from the drawn position to each new one, lasting `1 / MoveHz` for Niko and one clock
  tick (`1 / Speed` s) for everyone else, and snaps past `ActorMotion.MaxStep` (spawn, new game).
- **Draw Niko's steps on their schedule, not on their arrival.** Frames reach `_Process` only on
  frame boundaries and a step's commit time varies, so a segment started on arrival beats between
  0.76× and 1.35× (Fix19). `StepPlayout` gives each step its ideal time (when the accumulator
  crossed the interval), matches frames to steps through `WorldFrame.MovesApplied`, and plays about
  two frames behind real time. It never rewinds: a stall holds on the newest committed position and
  the delay re-settles at ±2 %. Check changes with `--trace-walk` and `scripts/trace-walk.mjs`: on
  `lab` every walking frame is within 5 % of 4 m/s. On the `test` spawn, walking D runs into an
  edge and the sim's own slide zigzags (`docs/PENDING.md`), which the trace shows faithfully.
- **The first step leaves on the key-down frame.** `SendMovement` primes the accumulator with
  `Min(1 / MoveHz, time since the last step)`, so movement starts at once but tapping never beats
  holding.
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

- **A chunk's mesh depends on its four neighbours and on every roof flag.** `ChunkMesher` reads one
  cell past the chunk on each side (edge faces `x+1`/`y+1`, wall corners `x-1`/`y-1`), and
  `RoofBands` spans all loaded chunks. `WorldClient.RebuildChunks` rebuilds new or changed chunks
  plus their 4-neighbours, and everything when the roof bands change. Fix18 measured a full rebuild
  at 47–99 ms and a 10-chunk border rebuild at 23–37 ms. The terrain renderer splits this into
  `BuildGeometry` (CPU-only lists over one read-only world snapshot) and `ToMeshes` (`ArrayMesh` on
  the main thread); only geometry runs in parallel, while meshes/nodes are created in `frame.Chunks`
  order. The roof-band set is ready in the mesher constructor, never lazily shared between workers.
  Serial/parallel vertex data matched exactly on test (25 full, 15 rebuilt) and lab (16 full, 16
  rebuilt). A temporary one-column test-frame shift rebuilt 15 chunks in 50.5 ms (26.8 geometry,
  15.4 mesh/node creation); this is not comparable to Fix18's 23–37 ms for 10 chunks and still
  hitches. See `docs/PENDING.md`.

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
- **Only the natural-ground face codes may change.** The around-Niko cliff window cuts the shader
  codes `0`/`1`/`2` (unbuilt ground top and exposed sides) and nothing else. Built slabs use `6`/`7`
  and walls keep their `3`/`4` on the top strip (`ChunkMesher.Box`), so floors and stairs are never
  shredded by the window; a new code that renders as ground must be added to both `ChunkMesher` and
  the `cut_top`/`cut_side` test in `Terrain.gdshader`.
- **The cliff cut is a line-of-sight hole with a rim and a silhouette outline.** `Terrain.gdshader`
  clears natural ground only where `length(vec2(dot(offset, cliff_cut_right), dot(offset,
  cliff_cut_up)))` is within `Cutaway.CliffCutRadius` (`6` m, one constant shared with the outline)
  and the fragment is above `viewer_h + 1` (0.5 m over Niko's feet), so the floor and the lower
  stairs keep their tiles; Niko's window also requires `dot(offset, cliff_cut_back) > 0` (toward the
  camera), so it never opens behind him. There are two windows, Niko's and the cursor's
  (`cliff_cutaway_enabled`/`cursor_cutaway_enabled`, each with its own center). Niko's opens only
  when natural terrain itself hides his body (`Cutaway.TerrainHidden`), never when a roof or a
  building does — a terrain window opened by a structure cut walls and stairs that covered nothing,
  while the building's own cut is by storey band. The cursor's opens only while the right button is
  held and it rests on terrain above the same half-metre line over Niko's feet:
  `Cutaway.CursorTerrain` marches the mouse ray over the dump's solid tops in unscaled mesh space
  and gives up when a structure or the sky comes first, so a hole follows the mouse on cliffs and
  banks. The two windows **fuse**: the rim only traces the outer boundary of the union (`max` of the
  two rims while inside both, `min` otherwise), so no seam shows where they overlap. The cursor's
  disc is fully round — no `front` cut — because a view-plane line through its center read as a
  crack. The `6` m circle can still clear nearby terrain that does not cover him; shrinking it to
  `2.2` m and to Niko's body column was tried and reverted (too tight, read as a small rectangle). A
  fragment within `0.08` m of a boundary — a screen arc, the 0.5 m line or (Niko's only) the far cut
  on the view plane through his center — stays and draws with the outline cream, so the opening is
  traced as a closed contour; without the `front` term Niko's far (north and west) edges have no
  visible trace. Cleared fragments `discard`, never `ALPHA = 0` (which kept depth and moved the
  material to the transparent pipeline), but the whole block is skipped in the shadow pass so the
  removed mass keeps casting (see the shadow bullet). The band threshold mixes metres (`len`,
  `front`) with half-metres (`above`), so convert `above` before comparing. The hole is bounded on
  both screen axes: dropping the vertical bound turns it into a straight slit through the world, and
  widening it shows the background because a heightmap draws only exposed faces. A wide wedge, a
  band cut over an 8 m radius and ghosting the cut (`blend_mix, depth_draw_always`, alpha `0.4`)
  were tried and reverted (Dev-033).
- **The cut is view-only: it must not change the lighting.** The window `discard`s for the camera
  pass (never `ALPHA = 0`, which kept depth and pushed the material into the transparent pipeline)
  but skips the cut in the shadow pass (`IN_SHADOW_PASS`, a global built-in), so the removed mass
  keeps casting its shadow exactly as if it were intact. Discarding in the shadow pass too made the
  hole light up and the ground lose the mass's shadow, which read as the cutaway "changing the
  shadows" (Dev-033).
- **The cut mass's outline is geometry, built by the mesher, not a post-process.** `ChunkMesher`
  emits an `Outline` surface: an `OutlineW` (`0.06` m) strip along each drawn face's top crest and
  along each end that does not continue into the next tile (`SouthContinues`/`EastContinues`), plus
  a bare crest (no end caps) on the north and west drops whose risers face away and are never drawn.
  Those far crests skip an edge with a wall (`AnyWall`) or with a floor resting on the ground
  (`RestsOnGround`): an ungated crest on all four drop directions was tried and reverted because it
  drew over walls and floors and left artifacts in the terrain. `_outlineMat` (`WorldClient`) is
  `unshaded, cull_disabled, depth_draw_opaque` and discards every fragment outside the same hole
  (same `len`, `above` and `front` test as the terrain shader), so it is invisible unless the cut is
  on. Keep its uniforms in sync with `UpdateCliffShader`, and keep each crest offset `0.01` away
  from the terrain mass (toward the lower neighbour) or it z-fights the face it sits on.
  `cliff_cut_back`, `cliff_cut_center`, `viewer_h` and `cliff_cut_right/up` are in unscaled mesh
  coordinates, and `Cutaway.RayToCamera` (the mesh-space camera back) feeds both the shader uniform
  and the `TerrainHidden`/`Occluded` probe.
- **The building cut is by storey band, and a flat plane cannot replace it.** `Terrain.gdshader`'s
  `band_cut` hides a slab when its own floor band (`UV2.x`) is above `viewer_band`, and a wall, prop
  or furniture fragment when its height band is. A clip plane at 3 m would keep the next floor's
  slab (its underside sits below the plane) and reveal it from above. The front-wall stub reads
  `UV2.x` as the wall's top and `UV` as its owner tile, so both must keep their current meaning for
  walls. The gate is `Cutaway.ClipH` (roofed, or a building between Niko and the camera): it reads
  only floors and walls, so a natural cliff hides its tiles through the cliff window without
  opening a building, and an outdoor building that blocks Niko does open (Dev-033).
- **The iso-space shader only samples a texture when `layer >= 0`.** Furniture and the plain object
  box pass `topLayer = -1`, so `Terrain.gdshader` takes the `col = base * tint` branch and just
  tints the vertex `COLOR.rgb`; a voxel's alpha does not need a face-code encoded into it the way a
  terrain top does; `height_tint` still applies unless the face code is 3 or 4 (a wall).
- **`cull_disabled` flips `NORMAL` on faces seen from behind.** A quad wound the other way loses
  the sun. The terrain shader passes the mesh normal through a varying (view space, via
  `MODEL_NORMAL_MATRIX`) and writes it in `fragment()`, so light never depends on winding.
- **Runtime sprite sheets and furniture JSON are filesystem sidecars.** `WorldClient` uses
  `Image.LoadFromFile` for `assets/sprites/`, and `FurnitureLibrary` uses `File.ReadAllText` for
  `assets/furniture/`; both paths are relative to `ProjectSettings.GlobalizePath("res://")`, so a
  Windows export needs these two directories beside `EtherBound.exe`. The export preset excludes
  those sidecars but keeps other resources (including the HUD font) in the PCK. `publish-game.mjs`
  refuses an export missing either directory. `Image.LoadFromFile` and `Image.Convert` run in the
  background; `Texture2DArray`, materials, `ArrayMesh` and nodes stay on the main thread. Godot's
  headless `--import` check does not catch a missing or malformed sidecar, only script/scene errors;
  a GPU run (`--shots`) is still the only thing that proves a new asset actually renders.
- **Actor materials are shared resources.** All actor `MeshInstance3D`s share one `CapsuleMesh` and
  use one of two fixed-color `StandardMaterial3D`s (Niko, Extras), both with the shared silhouette
  `ShaderMaterial` as `NextPass`. Do not create or mutate a material per actor; per-instance changes
  would reintroduce repeated shader compilation and violate this sharing.

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

