# Fix06: Fix05 review (misplaced south faces, buildings hiding Niko, floor textures)

[Bugfix] [Rendering] [Design] [Art]

Review of Fix05 as it stands in the working tree (uncommitted, overall `v0.6.1`,
`docs/done/Fix05.md`). On 22/09/2026 the user sent a screenshot at x1, seed 7, with Niko at
`X 131.32 · Y 146.77 · h 10` on the ramp to the test building's west door, and reported:
- the building's roof hides Niko;
- levels are cut strangely in some places.

The two have different causes: F1 is a bug in Fix05, and F2 is a design gap Dev-008 left out of
scope. The user also asked to use the new textures (F3).

## 1. Findings

### F1: South faces are drawn one tile east of their tile
`drawVertical` (`web/src/game/MapScene.ts` line 508) gets the edge's first corner and works out
the owner tile from it:
- The east face is called with `(x + 1, y)` and computes `tileX = x1 − 1 = x`, which is correct.
- The south face is called with `(x, y + 1)` and computes `tileX = x1 + 1 = x + 1` and
  `tileY = y1 − 1 = y`. It should be `(x, y)`.

So every south face, both terrain and floor slab, is drawn under tile `(x + 1, y)`. Along a south
ledge that runs in x the offset is invisible, because the next tile draws the same face, but:
- **The first tile of the ledge loses its face.** The background shows through as a black
  parallelogram. These are the black notches at every jog of the terraces in the screenshot.
- **The last tile's face hangs under the lower neighbour** as a light strip beyond the ledge end.
- **The building's slab edges step sideways**, which is visible along its south side.

Two more things make this hard to see:
- The side is guessed from the light factor (`light === 0.82 ? "s" : "e"`), not passed in.
- `tile-masks.test.ts` checks the masks, not where `MapScene` puts them.

### F2: A building in front of Niko hides him, and nothing reacts
In the screenshot the building (x 136–160) lies in +x of Niko, which is towards the camera. Its
roof at `h = 24` and its west walls cover Niko's column on screen. That occlusion is geometrically
correct, and the sorting is not at fault.

The rule is what fails: Niko is not *roofed*, so the cutoff is ∞. The front-wall stubs only cut
walls within Niko's own band (`base < viewerH + 4`), so the upper storeys and the roof stay. Dev-008
[Sec. 9] left both the x-ray silhouette and fading buildings out of scope. Every door on a
building's N or W side has this problem, because the building stands between that door and the
camera.

### F3: New textures are not used
The user added `src/sprites/floor/planks_x4.png` and `src/sprites/floor/stone_x4.png`, and replaced
`src/sprites/grass/grass_x4.png`, which `MapScene` already imports (`grass_x4_2.png` is gone). The
renderer has only one textured material, grass, and it is hard-coded (`drawTop`,
`material?.key === "grass"`, `tintGrass` with the grass colour baked in). Checked: all three sheets
are 256×32, 4 frames, binary alpha, and match the diamond rule of Fix05 [Sec. 3.2] pixel for pixel,
so they drop into the `terrain` atlas as they are.

### Noted, not a renderer bug
The long orange strip across the roof is the first floor (wood), seen through a real opening. The
generator removes the roof along the whole row `y = 150` except `x = 144`
(`server/src/etherbound/world/gen/testworld.py`, "Keep the stairwell open"). This is world data and
out of scope here [Sec. 7].

## 2. Decisions to approve

| Topic | Decision | Why |
|---|---|---|
| Face placement | `drawVertical` takes the owner tile and the side explicitly, and the placement becomes a pure, tested helper | This removes the inference that caused F1, and the tests can catch the next placement error |
| Structure cutaway | When a structure's floors or roof cover Niko on screen, hide that structure's storeys above his, exactly as if he were roofed. Other structures are unchanged | The same visual language as entering a building (Dev-008 roofed cutaway), applied only to what is in the way |
| Storey kept | The structure's cutoff is `max(viewerH, F) + 4`, where `F` is the highest floor of the structure with `floor_h ≤ viewerH + 4`, or else its lowest floor | Standing low beside a building would otherwise hide all its floors. Its void ground is not drawn, so a black hole would show |
| Silhouette | When anything that is drawn still covers Niko (terrain, a slab edge, a wall), draw a second copy of him on top of everything: an outline plus a translucent body | This catches every leftover case, hills included, and costs nothing when he is not covered |
| Material sprites | A client table maps a material `key` to a 4-frame sheet: `grass` → `grass/grass_x4.png`, `wood_floor` → `floor/planks_x4.png`, `concrete` → `floor/stone_x4.png`. Any material in the table is drawn textured, both as ground and as a floor top. The others stay flat | Concrete is what the test building's basement, ground floor and first floor use, so it is where the stone shows most. Stone could go on `rock` instead; say so if you prefer that |
| Vision | `VISION.md` [Sec. 5] gains the two rules above (wording in [Sec. 3.7]) | The current wording says "otherwise nothing is hidden". CLAUDE.md requires the vision to change first |
| Scope | Web only (`web.game`, `web.world`) | The server does not know about the camera |

## 3. What changes

### 3.1 F1: face placement (`web.game`)
- New pure helper `faceTile(side, tileX, tileY)` in `iso.ts`. It returns the tile whose frame
  origin the face mask is placed on, which is always `(tileX, tileY)`. This spells out the contract
  that faces belong to the tile whose top they hang from.
- `drawVertical(layers, usedRows, side, tileX, tileY, h0, h1, color, light, row, alpha)`:
  - The callers pass `(x, y)` and `"s"`/`"e"`: the ground faces at `MapScene.ts` lines 408/411 and
    the slab faces at 423/426.
  - The `x2`/`y2` arguments and the `light === 0.82` test are removed.
- Walls (`drawWallRun`) are already correct and stay as they are.

### 3.2 Ray march (`web.world`)
- New `web/src/world/ray.ts` exports
  `marchRay(store, s, t, fromH, toH, cutoffAt): RayHit | null`, where
  `RayHit = { x, y, h, kind: "floor" | "ground", z }` and `cutoffAt(x, y): number`. It holds the
  body of today's `pickTile` loop: 0.5 steps from `fromH` down to `toH`, with the same hit rules
  (VOID ground skipped, ground cutaway under floors). It stops at the first hit.
- `pickTile` becomes `marchRay(..., viewerH + 40, viewerH − 40, ...)` mapped to `{x, y, z}`. Its
  behaviour does not change.

### 3.3 Occlusion (`web.world`, new `web/src/world/occlusion.ts`)
- **Probe points:** Niko's screen column at heights `viewerH + 1`, `viewerH + 2.5` and
  `viewerH + 3.5` (the body is 58 px, about 3.6 units).
- **Rule:** a probe is covered when `marchRay(store, s, t, viewerH + 40, probeH + 0.5, cutoffAt)`
  hits. Everything on the ray above the probe's height is closer to the camera, so any hit is in
  front of Niko.
- `occludingStructures(store, nx, ny, viewerH, cutoff): Structure[]`:
  - It probes from the **centre of Niko's tile** against the **uncut** world (the plain
    `cutoffH`, ignoring structure cuts). It runs only when the tile or `viewerH` changes. Using the
    uncut world means that hiding a structure cannot flip the result back, so it does not flicker.
  - For every probe hit of kind `"floor"`, flood-fill the structure from the hit tile:
    4-connected tiles that have any level cell with a floor or a wall, capped at 4,096 tiles.
  - `Structure = { tiles: Set<string>, cutoff: number, bounds }`, with `cutoff` as in [Sec. 2].
    Hits already inside a found structure are not filled again.
- `isCovered(store, x, y, viewerH, cutoffAt): boolean`:
  - It uses the same probes from Niko's **rendered** position against the **drawn** world
    (structure cuts applied).
  - It runs every frame and only drives the silhouette, so it never causes a redraw.

### 3.4 Per-tile cutoff (`web.game`)
- `MapScene` keeps `structures: Structure[]` and a method `cutoffAt(x, y)`. The method returns
  `min(this.cutoff, cutoff of the structure containing (x, y))`, or `this.cutoff` when the tile is
  in no structure.
- In `drawChunk`, every `this.cutoff` becomes `cutoffAt(x, y)`: the ground cutaway, the floors, the
  wall bases and the wall cap in `drawWall`. `pickTile` receives `cutoffAt`.
- `setViewer`: when the tile or `h` changes, recompute `occludingStructures`. When the set changes,
  mark dirty the chunks that intersect the bounds of the old and new structures. This is a new
  `structureChunkKeys` in `dirty.ts`.
- Structures are cleared on snapshot, and recomputed when a chunk inside their bounds changes.

### 3.5 Silhouette (`web.game`)
- `nikoGhost`: a `Graphics` with the same shape as `niko`: the body filled `0x2583ff` at α 0.35,
  and a 2 px white stroke at α 0.9. It has no shadow.
- Depth is `Number.MAX_SAFE_INTEGER`, the position matches `niko` every frame, and it is visible
  only while `isCovered` is true.

### 3.6 Material sprites (`web.game`)
- New `web/src/game/terrainSprites.ts` exports
  `TERRAIN_SPRITES: Record<string, string>`, which maps a material `key` to an imported sheet URL,
  with the three entries from [Sec. 2]. The imports follow the existing one
  (`../../../src/sprites/...`).
- `preload`: load every sheet as a spritesheet (64×32 frames), keyed `sheet:<key>`.
- `createTerrainAtlas`:
  - Every sheet gets 4 slots, named `<key>0` … `<key>3`, and the masks follow them.
  - A sheet that is not 256×32 is skipped with a `console.warn`, and that material falls back to
    the flat `top`.
  - Today's width stays well under 4,096 px (12 sprite slots plus about 15 masks).
- `drawTop(key, …)` and the floor tops in `drawChunk` both use one helper, `drawSurface`:
  - If the material's key has a sheet, draw frame `<key><variant(x, y)>` tinted
    `spriteTint(color, h)`.
  - Otherwise draw `top` tinted `shade(color, h)`.
- `spriteTint(color, h)` generalizes `tintGrass`: for each channel it computes
  `min(255, round(shade(color, h)ₖ · 255 / colorₖ))`, and uses 255 when `colorₖ = 0`. `tintGrass`
  and the hard-coded `0x6a9f4b` are removed. `grassFrame` becomes `variant(x, y)`, with the same
  hash.
- Faces and slab edges stay flat colour, as today.

### 3.7 Docs
- `docs/utils/VISION.md` [Sec. 5], Buildings: replace "otherwise nothing is hidden." with
  "Otherwise, a building that stands between him and the camera hides its storeys above his, the
  same way. Whatever still covers him, he is drawn as a silhouette on top."
- `CONTEXT.md`, `web.world` row: add `ray.ts` and `occlusion.ts`. `web.game` row: add the
  structure cutaway, the silhouette, and the material sprite table (grass, planks, stone). Change
  "Vector placeholders remain for materials without dedicated sprites" so that it names the table. Measured pitfalls: faces belong to the tile whose top they
  hang from; the occlusion test must read the uncut world.
- `docs/PENDING.md`: this doc's entry, replaced when done.

### 3.8 Tests (`web/tests/`)
- `isometric-world.test.ts`:
  - `faceTile` returns the owner tile for both sides.
  - `pickTile` results are unchanged. The existing cases pass through `marchRay`.
- `occlusion.test.ts` (new), on a small synthetic building 4×4 with a roof at `h = 12`:
  1. Niko at `h = 0`, two tiles west of it, with the roof in front: one structure, with its tiles.
  2. Niko east of the same building, where it is behind him: no structure.
  3. The structure's cutoff follows [Sec. 2]: the highest floor at or below `viewerH + 4`, else its
     lowest floor.
  4. With the structure cut, `occludingStructures` still returns it (uncut world), and `isCovered`
     is false once the cut hides the roof.
  5. A hill in front covers Niko: `isCovered` is true and there is no structure.
- `terrain-sprites.test.ts` (new):
  - Every `TERRAIN_SPRITES` key exists in `server/src/etherbound/world/materials.toml` (read as
    text, matching `key = "…"`).
  - `spriteTint(color, 0)` returns `0xffffff` for the three colours, and it never divides by a zero
    channel.
- `dirty.test.ts`: `structureChunkKeys` selects exactly the chunks that meet the old and new bounds.

## 4. Traps

- **Probe against the uncut world** for structure selection, or the cut flickers on and off every
  tile.
- **Probe from the tile centre** for structures, or they change mid-tile and cause redraws.
- **Pass `cutoffAt` everywhere `cutoff` was used.** A missed spot (the wall cap in `drawWall`, the
  ground cutaway, picking) leaves pieces of the hidden storeys, or lets the menu pick hidden floors.
- **Do not grow a structure through the ground.** The flood fill only walks tiles with level cells,
  so two buildings count as one only if their level cells touch.
- **Keep `cutoffH` (roofed) unchanged.** The structure cut only ever lowers the cutoff.
- **Floor tops go through `drawSurface` too.** Texturing only the ground would leave the building's
  floors flat, since its interior ground is void.
- **The sprite table is art, not rules.** It maps a key to a picture. Nothing in movement or menus
  may read it.

## 5. Modules and versions

| Module | Version |
|---|---|
| web.game | v0.1.4 → v0.1.5 |
| web.world | v0.0.6 → v0.0.7 |

The overall project version is suggested to move to `v0.6.2`.

## 6. What must not break

- Everything in Fix05 [Sec. 6] and Dev-008 [Sec. 6].
- The Fix05 performance behaviour: a flat walk away from buildings triggers no chunk redraws.
  Occlusion runs only on tile and `h` changes, and the silhouette test never redraws.
- The roofed cutaway inside buildings, the stubs, and picking.

## 7. Checks and acceptance

Checks: `cd web && npm test && npm run build`, generated types unchanged, and the server checks
pass unchanged.

Manual, with `EtherBound.exe`, new game with seed 7 and seed 0, at x1 and x4:
1. **Ledges:** terraces and cliffs show no black notches at jogs, and no face strip hangs past the
   end of a ledge.
2. **Building edges:** the slab edges on the building's south side line up with their floors.
3. **West approach** (the ramp at y 146, as in the screenshot): the upper storeys and the roof of
   the building disappear, the ground floor stays, and Niko is fully visible.
4. **Walking away** west until the building no longer covers him: the building comes back whole.
   There is no flicker while he stands still or walks inside one tile.
5. **East and south sides:** walking past, the building is never cut.
6. **Behind a hill** (for example the terrace at y 89–94, approached from the north): the
   silhouette shows, and nothing is cut.
7. **Inside the building:** the roofed cutaway behaves as in Dev-008 [Sec. 7] 6–8.
8. **Performance:** Fix05 acceptance 5–7 still hold.
9. **Textures:** grass uses the new `grass_x4.png`. The building's basement, ground floor and first
    floor show stone (concrete), and the floor under the roof shows planks (wood). Neighbouring tiles
    vary between the 4 frames, higher floors are darker as with grass, and there are no seams
    between tiles at x1–x4.

Automated checks passed. The interactive GUI and frame-time acceptance 1–9 was not run here and
remains open in `docs/PENDING.md`.

Out of scope:
- The roof slot at `y = 150`, which is a `server.world` data change. If it is not intended, it
  needs its own doc.
- Fading instead of cutting, and hysteresis on the structure cut.

## 8. Todo

### Decisions
- [x] Approve [Sec. 2], including the VISION wording [Sec. 3.7] and the sprite mapping (approved by the request to execute Fix06)

### Code
- [x] F1: explicit face side and owner tile, `faceTile` [Sec. 3.1]
- [x] `ray.ts`; `pickTile` on top of it [Sec. 3.2]
- [x] `occlusion.ts` [Sec. 3.3]
- [x] Per-tile `cutoffAt`, structure redraws [Sec. 3.4]
- [x] Silhouette [Sec. 3.5]
- [x] Material sprite table, `drawSurface`, `spriteTint` [Sec. 3.6]

### Checks
- [x] Tests [Sec. 3.8]; web build, generated types, server checks
- [ ] Manual acceptance 1–9 [Sec. 7]

### Closing
- [x] `VISION.md`, `CONTEXT.md`, `PENDING.md` [Sec. 3.7]; `docs/utils/VERSION.md` [Sec. 5]
- [x] Notion: Work Report for the date and a Dev Blog page
- [x] Move this doc to `docs/done/`

---

## TL;DR

- **Black notches and misaligned edges:** Fix05 draws every south face one tile east
  (`tileX = x1 + 1`). Pass the owner tile and the side explicitly, and test the placement.
- **The roof hides Niko:** a correct occlusion that nothing handled. When a structure's floors or
  roof cover Niko on screen, it hides its storeys above his, as if he were roofed. If anything
  still covers him, he is drawn as a silhouette on top.
- **New textures:** a client table maps material keys to 4-frame sheets: grass, planks for
  `wood_floor`, and stone for `concrete`. Ground and floor tops use the table, and the others stay
  flat.
- The cut is chosen from the uncut world at the tile centre, so it does not flicker, and it runs
  only on tile or height changes.
- The roof slot at `y = 150` is generator data and out of scope.

Web only: `web.game` v0.1.5, `web.world` v0.0.7. VISION [Sec. 5] is updated first.
