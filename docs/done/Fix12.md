# Fix12: Dev-022 review — headroom, ownership and leftover indirection

[Refactor] [Review]

Review of the split that landed in `v2.1.0` (`33b86c7`) together with Dev-019 and Dev-020. The
moves are faithful: every server function compared by AST against `v2.0.0` differs only where
`self.x` became a parameter, and the six BitCanvas scripts concatenate back to the old `app.js`
with two blocks reordered. What is left is structure: two files sit at the 600-line line that
Dev-021 is about to cross, `MapScene` still forwards to and reaches into its collaborators, and a
few small leftovers. Like Dev-022, this is **no behaviour change**: tests pass unchanged and the
visual baselines are not updated.

**Precondition:** the working tree is clean at `v2.1.0` or later. Dev-021 has not started
implementation; it lands in the files this doc creates.

## 0. Findings (measured 24/09/2026 at `33b86c7`)

| # | Where | Finding |
|---|---|---|
| F1 | `server/src/etherbound/engine/world.py` | 599 lines against the 600 limit (Dev-022 estimated ~550). `chunk_payload` (~40 lines) only reads `self.grid` and builds payload dataclasses |
| F2 | `web/src/game/chunkRenderer.ts` | 565 lines. The wall block (`isFrontWall`, `drawnWallTop`, `wallJoint`, `jointLookup`, `drawWall`, `drawWallUnits`, `drawWallRun`, ~125 lines) is exactly what Dev-021 [Sec. 4] extends with midlines, diagonals and heights |
| F3 | `web/src/game/MapScene.ts` | Eight private one-line wrappers only forward: `layersFor`, `updateChunkBounds`, `syncExtras`, `updateExtras`, `clearExtras`, `animatePhysics`, `updatePhysicsAnimations`, `clearPhysicsAnimations`. `layersFor` also discards its return value |
| F4 | `web/src/game/MapScene.ts` | `MapScene` reads and mutates `chunkRenderer.chunkLayers` in four places (chunk listener, `updateCulling`, `dirtyChunkInfo`, `flushDirtyQueue`), so the map is shared state, not the renderer's |
| F5 | `web/src/game/MapScene.ts` `updateCulling` | Declares `const view = this.cameras.main.worldView` and never reads it |
| F6 | `web/src/game/chunkRenderer.ts` | `ChunkRendererView.cutoff` is never read; `MapScene` still builds its getter. `TerrainAtlas` is typed as `ReturnType<typeof import("./terrainAtlas").buildTerrainAtlas>` inline |
| F7 | `server/src/etherbound/engine/menu.py` | `build_menu` takes nine positional parameters, five of them engine state, and repeats the body of `WorldEngine._world_row` inline. Dev-016 [Sec. 2] rewrites this function |

`clearChunkLayers` in `MapScene` is not a pure wrapper: it also clears the dirty queue. It stays.

## 1. Decisions

| Topic | Decision | Why |
|---|---|---|
| Behaviour | Pure moves and call-site rewrites, as Dev-022 [Sec. 1]. No logic edits, no drive-by fixes | The unchanged suites and baselines stay the proof |
| `chunk_payload` (F1) | Body moves to `payloads.py` as `chunk_payload(grid, cx, cy) -> ChunkPayload | None`. `WorldEngine.chunk_payload` and `chunks_near` keep their signatures and delegate | `ws.py`, `routes/api.py` and `test_handling.py` call the engine method; the public API does not move |
| `world_row` (F7) | `world_setup.world_row(session)` holds the body of `_world_row`. `WorldEngine` calls it at its seven sites and loses the method; `menu.py` imports it | One copy of the "not initialized" rule, and a few lines off `world.py` |
| `build_menu` (F7) | Signature becomes `build_menu(sessions, grid, registry, catalog, load_cache, *, actor_id, x, y, z)`. `WorldEngine.menu` passes the last four by keyword | Engine state first, request keyword-only: a swapped `x`/`y` or `actor_id` cannot pass silently. A context object is heavier than the problem |
| Walls (F2) | New `wallPainter.ts`: class `WallPainter` with the seven wall members and the constants `MAX_WALL_H`, `CUT_DEPTH`, `CUT_WIDTH`. `ChunkRenderer` builds one and calls `isFrontWall` and `drawWall` | Dev-021 then edits a ~130-line wall file instead of pushing the renderer past 600 |
| Wall collaborators | `WallPainter` receives `chunks`, `atlas`, `view` and a `paint` object `{ drawMask, materialColor }` bound to the renderer's methods | The wall code needs only those two drawing primitives; it never touches layers directly |
| `scaleColor` | Becomes an exported pure function in `terrainSprites.ts`, next to `shadeColor`; both renderer and painter import it | It uses no state; today it is a private method both files would need |
| Layers ownership (F4) | `chunkLayers` becomes `private`. `ChunkRenderer` gains `hasLayers(key)`, `boundsOf(key)`, `cull(isVisible)` and `dirtyInfo(hasLevels)`, holding the bodies of the four `MapScene` sites | The renderer owns the map it fills; `MapScene` keeps the policy (what is visible, what is near Niko) |
| Wrappers (F3) | Deleted. Call sites call `chunkRenderer`, `extrasLayer` or `physicsAnimator` directly | Indirection with no behaviour |
| Leftovers (F5, F6) | Delete the unused `view` local, the `cutoff` view field and its getter. `terrainAtlas.ts` exports `type TerrainAtlas = ReturnType<typeof buildTerrainAtlas>` | Dead code; one named type instead of an inline import |

## 2. Out of scope

- `handling.py` (667) and `pixelart.js` (833): Dev-022 [Sec. 1, Scope] still holds.
- The three near-identical `ActionResult` blocks in `WorldEngine.submit`: not on Dev-021's path.
- Moving culling or the dirty queue out of `MapScene`: they are scene policy [Sec. 1, Layers].
- The one-commit-for-three-docs history of `v2.1.0`: already on `main`, not rewritten.

## 3. What changes

### 3.1 server.engine

- `payloads.py`: add `chunk_payload(grid: WorldGrid, cx: int, cy: int) -> ChunkPayload | None`,
  the body of today's method with `self.grid` → `grid`.
- `world.py`: `chunk_payload` returns `payloads.chunk_payload(self.grid, cx, cy)`; `chunks_near`
  is unchanged (it calls `self.chunk_payload`). `_world_row` removed; its seven callers use
  `world_setup.world_row(session)`. `menu` passes `actor_id=`, `x=`, `y=`, `z=`.
- `world_setup.py`: add `world_row(session) -> WorldMeta`.
- `menu.py`: new signature [Sec. 1]; the inline `WorldMeta` lookup becomes `world_row(session)`.

### 3.2 web.game

- **New `wallPainter.ts`:** `WallPainter` [Sec. 1]. Members keep their names and bodies;
  `this.drawMask` / `this.materialColor` become `this.paint.drawMask` / `this.paint.materialColor`,
  `this.scaleColor` becomes `scaleColor`.
- `chunkRenderer.ts`: loses the wall block, `scaleColor` and the three constants; `drawChunk`
  calls `this.walls.isFrontWall(...)` and `this.walls.drawWall(...)`. `chunkLayers` is private;
  add `hasLayers`, `boundsOf`, `cull`, `dirtyInfo`. `ChunkRendererView` drops `cutoff`.
  `TerrainAtlas` is imported from `terrainAtlas.ts`.
- `terrainAtlas.ts`: export `TerrainAtlas`. `terrainSprites.ts`: export `scaleColor`.
- `MapScene.ts`: remove the eight wrappers [F3], the `cutoff` getter and the unused `view`
  local; the four `chunkLayers` sites use the new `ChunkRenderer` methods.

### 3.3 Docs

- `Dev-021.md` [Sec. 4] **web.game**: add `wallPainter.ts` to the file list; the midline,
  diagonal and cap bullets point to `WallPainter`.
- `Dev-016.md` [Sec. 2] and `Dev-017.md` [Sec. 0]: `build_menu` pointers stay valid; no edit
  unless wording names its parameters.
- `CONTEXT.md`: the `web.game` module row adds `wallPainter.ts`.
- `docs/PENDING.md`: Fix12 is listed under "Planned work" before Dev-021; remove it once this
  lands.

## 4. Modules and versions

| Module | Change | Bump |
|---|---|---|
| server.engine | `payloads.chunk_payload`, `world_setup.world_row`, `build_menu` signature | +0.0.1 |
| web.game | `wallPainter.ts`, `ChunkRenderer` ownership, `MapScene` cleanup, `scaleColor` | +0.0.1 |

No `server.net`, `server.app` or schema change: `chunk_payload` keeps its engine signature. The
overall version is a Z bump (COMMITS [Sec. 2]: refactor).

## 5. What must not break

- Every server and web test passes with **no** test-source change.
- `schema.d.ts` regenerates byte-identical.
- Spawn and shell-layout baselines pass without `--update-snapshots`.
- `WorldEngine`'s public methods, the WebSocket and REST contracts, and `createGame`'s exports
  keep their signatures.
- Dev-020's wall thickness renders pixel-identical (it is covered by the spawn shots).

## 6. Acceptance

1. `npm run check` passes; `git diff --stat` shows no file under `server/tests` or `web/tests`.
2. `npm run check:visual` passes on the existing baselines (stop the launcher first: ports 8000
   and 5173). Visual shots: `spawn.spec.ts` x1/x2/x4, `shell-layout.spec.ts`.
3. `wc -l`: `world.py` ≤ 560, `chunkRenderer.ts` ≤ 460, `wallPainter.ts` ≤ 160.
4. `grep -n "chunkLayers" web/src/game/MapScene.ts` finds nothing.
5. `grep -n "_world_row" server/src` finds nothing; `grep -n "view\.cutoff\b\|get cutoff"
   web/src/game` finds nothing.
6. `npm run check:sizes` still lists only `handling.py` and `pixelart.js`.

## 7. Todo

### Decisions
- [x] Approve [Sec. 1]

### Server
- [x] `payloads.chunk_payload`, `world_setup.world_row`, `world.py` call sites [Sec. 3.1]
- [x] `build_menu` keyword-only request [Sec. 3.1]

### Web
- [x] `scaleColor` in `terrainSprites.ts`, `TerrainAtlas` export [Sec. 3.2]
- [x] `wallPainter.ts` and `ChunkRenderer` wiring [Sec. 3.2]
- [x] `ChunkRenderer` ownership methods; `MapScene` sites [Sec. 3.2]
- [x] Remove the eight wrappers, `cutoff` getter, unused `view` [Sec. 3.2]

### Docs
- [x] `Dev-021.md`, `CONTEXT.md`, `PENDING.md` [Sec. 3.3]

### Acceptance
- [x] [Sec. 6] 1, 3–6
- [x] [Sec. 6] 2 (visual, needs the launcher stopped)

### Closing
- [x] `VERSION.md` [Sec. 4]
- [x] Notion: Dev Blog entry (Kind `Fix`), Work Report for the date
- [x] Move this doc to `docs/done/`
- [ ] Commit per `COMMITS.md` (not requested)

---

## TL;DR

- Dev-022's moves are faithful; this fixes what the review found around them, with no behaviour
  change, no test edits and no new baselines.
- Headroom before Dev-021: `chunk_payload` moves to `payloads.py` and `world_row` to
  `world_setup.py` (`world.py` ≤ 560); the wall block becomes `WallPainter` in `wallPainter.ts`
  (`chunkRenderer.ts` ≤ 460).
- Ownership: `chunkLayers` goes private behind four `ChunkRenderer` methods; `MapScene` loses its
  eight forwarding wrappers.
- Small: `build_menu` takes the request keyword-only, `scaleColor` becomes a shared pure
  function, dead `cutoff` getter and `view` local removed, `TerrainAtlas` is a named export.
- `server.engine` and `web.game` +0.0.1, overall Z bump. Lands before Dev-021.
