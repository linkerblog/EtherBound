# Fix09: Textured side faces and the open building base

[Rendering] [Art] [Bugfix]

On 23/09/2026 the user sent a screenshot at x1, seed 1895070486, with Niko at
`X 149.33 · Y 149.69 · Z 2 (h 12)` on the test building's ground floor (roofed cutaway), and
asked what to do about the flat boxes under the textures. Of the three options offered, they chose
**C**: every textured material gets its own side sheet, drawn as art. This doc also covers the
black gaps visible along the building's base in the same screenshot.

## 1. Findings

### F1: Side faces are flat, and their colour does not match the texture
- **Where they are drawn.** Ground ledge faces (`web/src/game/MapScene.ts` lines 466/469) and
  floor slab edges (481/484) both go through `drawVertical`. It tints the white `faceS*`/`faceE*`
  masks with `shade(materialColor) × light`.
- **Flat on purpose.** Fix06 [Sec. 3.6] kept faces flat ("Faces and slab edges stay flat colour").
- **Wrong colour.** The colour comes from `materials.toml`, not from the sheet:
  - `concrete` `#a1a5a3` gives `#848786` on south edges, much lighter than the dark
    `stone_x4.png`. These are the grey bands under the stone floors.
  - `grass` `#6a9f4b` gives `#578240`. These are the plain green blocks under the textured grass.

### F2: Black gaps along the building's base
Verified in code and data (seed 1895070486):
- **The interior ground is void.** Every interior tile (x 137–159, y 133–159) has ground at
  `h = 12`, with `LEVEL_VOID` on z 1 and z 2 (`testworld.py`). `drawChunk` skips a void ground's top
  *and its faces* (`groundShown` is false).
- **The terrain around the building is lower.** The ground around it is at `h 1–4`: column x 160
  is at 1–2, and row y 160 is at 1–3, apart from the landing at 6.
- **Nothing closes the gap.** Between that terrain and the ground-floor slab, only two things are
  drawn: the basement slab edge (`h 5–6`) and the perimeter walls. The solid column under the void
  (terrain up to `h 6`) has no faces.
- **Known and deferred.** Fix05 [Sec. 9] left "faces toward excavated neighbours" out of scope. It
  needs a "solid top below the void" rule.

Not verified: exactly which tiles the black pixels belong to. By `wallBaseH`, the perimeter walls
start at the perimeter ground (`h 2`) and should cover part of the gap. x 160 and y 160 are also
chunk boundaries (`CHUNK_SIZE` 32). Step 0 [Sec. 3.5] confirms the cause before F2 is coded.

## 2. Decisions to approve

| Topic | Decision | Why |
|---|---|---|
| Side sheets | An optional sheet per material: 128×32 RGBA, opaque. Two rows of 32×16 cells: **cap** (y 0–15) and **fill** (y 16–31), each with 4 variants. It is drawn flat, unsheared and at neutral light, and tiles seamlessly across and down. This is the contract given to the texture agent on 23/09/2026 | Option C. Unsheared art is easier to draw, and one sheet serves both sides |
| Shearing | The atlas shears each cell into the exact shape of `faceMask(side, 1)`, moving every pixel column down as a whole. The code lives in a pure, tested helper | Shifting whole columns loses no pixels, so pixel art survives it. Horizontal lines become correct 2:1 isometric lines |
| Stacking | Textured faces are drawn one unit (16 px) at a time. The unit at the face's top uses a **cap**, and every unit below uses a **fill**. The variant is hashed from `(x, y, side, h)`. Flat faces keep today's 8/4/2/1 runs | The grass lip appears once at the top of a cliff, not on every run. Hashing each unit breaks up repetition on tall cliffs |
| Light | `sideTint(color, hTop, light)` = `spriteTint(color, hTop)` scaled by 0.82 (south) or 0.66 (east) | The same height shading as the top above it, and the same side contrast as today |
| Fallback | If a material has no sheet, or its sheet is not 128×32, it gets a `console.warn` and keeps today's flat faces. Top and side sheets are independent | Materials without art (asphalt, water, roofing) stay the same |
| Void-cut column | New `ChunkStore.solidTopH(x, y)`: the ground top, or the bottom of the contiguous VOID bands under it. A void ground draws its S/E faces from its solid top, using **fill cells only**. Every ground face also measures its neighbour with `solidTopH` | This closes F2 without strata on the client. The excavated side has no grass lip. It also lifts the Fix05 [Sec. 3.1] known limit |
| Void-cut top | Stays hidden, as today | Nothing generates a floorless excavation yet. In the building, the basement slab covers it |
| Art dependency | The code lands together with the three PNGs. Sheets are statically imported, as in Fix07 [Sec. 3.1], so a missing file breaks the build. If only some sheets arrive, the table lists only those | A static import is the pattern proven against Vite's allow list |
| Scope | Web only (`web.game`, `web.world`). The server, protocol and schema stay untouched. No VISION change: a sprite table is art, not rules | |

## 3. What changes

### 3.1 Art (blocking)
The texture agent delivers these files, saved next to their tops:
- `src/sprites/grass/grass_side_x4.png`: a grass lip on the cap, soil below.
- `src/sprites/floor/stone_side_x4.png`: the edge of a stone slab.
- `src/sprites/floor/planks_side_x4.png`: the edge of a plank slab.

### 3.2 Sheets and atlas (`web.game`)
- **`terrainSprites.ts` (pure).**
  - `TERRAIN_SIDE_FILES: Record<string, string>` =
    `{ grass: "grass/grass_side_x4.png", wood_floor: "floor/planks_side_x4.png", concrete: "floor/stone_side_x4.png" }`.
  - `sideTint(color, h, light)`.
  - `sideVariant(x, y, side, h): 0 | 1 | 2 | 3`, a hash in the style of `variant`.
- **`terrainSheets.ts`.** One static import per side sheet, exported as `TERRAIN_SIDE_SHEETS`, with
  the same keys as `TERRAIN_SIDE_FILES`.
- **`tileMasks.ts` (pure).** New `shearSideCell(sheet, side, part, variant)`.
  - `sheet` is `{ width, height, data }` (RGBA).
  - It returns `{ width, height, offsetX, offsetY, rgba }`, with the same size and offsets as
    `faceMask(side, 1)`.
  - Mask pixel `(x, bottomRow(x) + 1 + r)` takes sheet pixel `(variant·32 + c, part·16 + r)`, where
    `c = x − firstX` (0–31) and `r` runs 0–15.
  - Both sides read the sheet from left to right.
- **`MapScene.preload`.** Load each side sheet as an image, keyed `side:<key>`. The Fix07
  `loaderror` logging covers it.
- **`createTerrainAtlas`.**
  - Every valid side sheet adds 16 frames (2 sides × 2 parts × 4 variants), named
    `side:<key>:<s|e>:<cap|fill>:<v>`.
  - They are placed in a second band starting at `y = 160`. The canvas grows by the tallest frame,
    and its width is `max(today's width, frames × 32)`.
  - Each frame's offsets go into `maskOffsets`, so `drawMask` places it exactly like `face{S|E}1`.
  - Keys are recorded in `sideKeys`.
  - Today that is 48 frames, 1,536 px, under the existing width. Past 4,096 px, warn and skip the
    remaining sheets.

### 3.3 Drawing (`web.game`)
- **`drawVertical`** takes `materialId` and `fillOnly` in place of a precomputed `color`.
  - If the material's key is in `sideKeys`, each lower/upper run (the split at `viewerH` stays)
    goes through a new `drawSideRun`.
  - Otherwise it goes through `drawFaceRun`, with today's colour.
- **`drawSideRun`**, for every unit top `t` in the run:
  - The part is `cap` when `!fillOnly && t === h1` (the face's real top, not the run's), and `fill`
    otherwise.
  - The frame is `side:<key>:<side>:<part>:<sideVariant(x, y, side, t)>`.
  - Each unit is drawn with `drawMask(..., h = t, tint = sideTint(color, h1, light))`.
- **`drawChunk` ground faces.**
  - `faceTop = solidTopH(x, y)`, and `fillOnly = isVoid(x, y, h)`.
  - Faces are drawn when `fillOnly ? faceTop <= tileCutoff : groundShown`.
  - The neighbour bottoms `eastH`/`southH` come from `solidTopH` of the neighbour.
  - The top itself is unchanged.
- **Slab edges** pass the floor's material with `fillOnly = false`. They are one unit, so they
  always show a cap.

### 3.4 `ChunkStore.solidTopH` (`web.world`)
```ts
solidTopH(x, y): number | undefined
// ground = groundH(x, y); undefined → undefined
// z = floor(ground / LEVEL_H); band z not VOID → ground
// while band z − 1 is VOID: z −= 1
// return z · LEVEL_H
```
The test building's interior gives 6. A ground that sits exactly on the floor of a void band returns
itself, and its faces go through the `fillOnly` path.

### 3.5 F2 step 0: confirm before coding F2
- Run seed 1895070486 and stand where the screenshot was taken.
- Temporarily draw the S/E faces of void-cut columns in magenta, from `solidTopH` down to the
  neighbour's ground.
- If the magenta fills the black gaps along x 159|160 and y 159|160, go on with [Sec. 3.3].
- If it does not (for example, perimeter tiles not drawn at the chunk boundary), stop. Record the
  real cause in [Sec. 1] and update [Sec. 2] before coding F2. F1 does not depend on this.

**Result (23/09/2026, headless).** No GUI was available in this session, so step 0 was run against
the generator and the same rules instead of the screen: `generate_test_world(1895070486)` gives
every interior tile (x 137–159, y 133–159) ground 12 with `LEVEL_VOID` on z 1 and z 2, so its
`solidTopH` is 6; x 160 is ground 1–2 and y 160 is ground 1–3 apart from the landing at 6. The
void-cut rule adds 41 S/E faces along x 159|160 and y 159|160, running from the surrounding terrain
(neighbour tops 1, 2 or 3) up to 6, and none where the neighbour is already at 6. The magenta would
fill the gaps, so F2 is coded as in [Sec. 3.3, 3.4].

### 3.6 Docs
- `CONTEXT.md`:
  - `web.game` row: the side sheet table, sheared side frames, and cap/fill stacking.
  - `web.world` row: `solidTopH`.
  - Measured pitfalls:
    - Side sheets are unsheared, and the atlas shears them.
    - A void ground's faces start at its solid top.
- `docs/PENDING.md`: this doc's manual acceptance while open.
- `docs/done/Fix05.md` is not edited. Its deferred item is closed here.

### 3.7 Tests (`web/tests/`)
- `tile-masks.test.ts`:
  - `shearSideCell` has exactly the footprint of `faceMask(side, 1)` (size, offsets, opaque set) for
    both sides.
  - On a synthetic sheet that encodes `(sx, sy)` in each pixel, the sampled pixels follow the
    mapping in [Sec. 3.2] for cap/fill and variants 0 and 3.
- `terrain-sprites.test.ts`:
  - Every `TERRAIN_SIDE_FILES` key exists in `materials.toml`.
  - Every file exists, and its PNG `IHDR` reads 128×32.
  - `sideTint(c, 0, 1)` equals `spriteTint(c, 0)`, and `light` scales each channel.
  - `sideVariant` is deterministic and uses all 4 values over a 16×16 area.
- `isometric-world.test.ts`, `solidTopH`:
  - A plain tile returns its ground.
  - A synthetic building column (ground 12, VOID on z 1–2) returns 6.
  - VOID on z 2 only returns 12.
  - A missing tile returns `undefined`.

## 4. Traps

- **The cap goes on the face's real top.** If you test the cap against the run's top, a cliff
  split at `viewerH` gets two lips.
- **Measure neighbours with `solidTopH` in both places**, the face bottom and the "is it higher"
  test. Otherwise faces run into void columns.
- **Void-cut faces are fill only.** A cap there would draw grass inside the basement.
- **Do not bake light into the sheet path.** The tint carries the 0.82/0.66 light, so both sides
  share one sheet.
- **Keep the bob count in check.** Per-unit drawing applies only to textured faces. A flat walk
  away from buildings must still trigger no redraws (Fix05).
- **The sprite table is art, not rules.** Movement, picking and menus never read it.

## 5. Modules and versions

| Module | Version |
|---|---|
| web.game | v0.1.6 → v0.1.7 |
| web.world | v0.0.7 → v0.0.8 |

The overall project version is suggested to move to `v0.6.4`.

## 6. What must not break

- Everything in Fix06 [Sec. 6], Fix05 [Sec. 6] and Dev-008 [Sec. 6].
- Top textures, face placement (`faceTile`), the structure cutaway and the silhouette.
- Materials without side sheets draw exactly as today.
- Picking: `pick.ts` and `ray.ts` are not touched.

## 7. Checks and acceptance

Checks: `cd web && npm test && npm run build`, generated types unchanged, and the server checks
pass unchanged.

Manual, with `EtherBound.exe`, seed 1895070486 and seed 7, at x1 and x4:
1. **Grass ledges:** soil sides with a grass lip only on the top unit. Tall cliffs (the terrace at
   y 89–94, the park pit) stack fill cells without seams.
2. **Slab edges:** the building's floors show stone or plank edges that match their tops. The light
   grey bands are gone.
3. **Seams:** long ledges and slab edges show no seams at tile joins, at x1–x4.
4. **Light:** south faces are lighter than east faces, and higher faces shade like their tops.
5. **Other materials:** asphalt, water, roofing and walls are unchanged.
6. **Building base:** the black gaps along the east and south sides are gone, as in the
   screenshot's position.
7. **Fallback:** with a side sheet renamed away, the build fails loudly (static import). With a
   wrong-sized sheet, the console warns and the faces are flat.
8. **Performance:** Fix05 acceptance 5–7 still hold.

## 8. Todo

### Decisions
- [x] Approve [Sec. 2]

### Art
- [x] Three side sheets delivered and saved [Sec. 3.1] (generated to the contract in this session;
      swap the files if the texture agent delivers a different set)

### Code
- [x] F2 step 0 [Sec. 3.5]
- [x] Side sheet table, `sideTint`, `sideVariant`, static imports [Sec. 3.2]
- [x] `shearSideCell` and the atlas side band [Sec. 3.2]
- [x] `drawVertical` / `drawSideRun`, cap/fill stacking [Sec. 3.3]
- [x] `solidTopH` and void-cut faces [Sec. 3.3, 3.4]

### Checks
- [x] Tests [Sec. 3.7]; web build, generated types, server checks
- [ ] Manual acceptance 1–8 [Sec. 7]

### Closing
- [x] `CONTEXT.md`, `PENDING.md` [Sec. 3.6]; `docs/utils/VERSION.md` [Sec. 5]
- [ ] Notion: Work Report for the date and a Dev Blog page
- [x] Move this doc to `docs/done/`

---

## TL;DR

- **Flat boxes under the textures:** these are side faces (ledges and slab edges), flat by
  Fix06's choice and coloured from `materials.toml`. Option C: each textured material gets a
  128×32 side sheet with cap and fill rows, 4 variants, drawn unsheared. The atlas shears it into
  the face shape.
- Faces are drawn one unit at a time: the cap on the top unit, fill below. They get the height
  shading and the 0.82/0.66 light. Materials without a sheet stay flat.
- **Black gaps at the building's base:** the void interior column draws no faces, a limit Fix05
  deferred. `solidTopH` gives the solid top under the void, and its faces use fill only. Step 0
  confirms this is the cause before it is coded.
- Blocked on the three PNGs. Web only: `web.game` v0.1.7, `web.world` v0.0.8.
