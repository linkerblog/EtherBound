# Fix05: Isometric renderer review (void floors, grass overdraw, x1 frame drops)

[Bugfix] [Rendering] [Performance]

Review of Dev-008 as it stands in the working tree (uncommitted, overall `v0.6.0`,
`docs/done/Dev-008.md`). The user reported on 22/09/2026:
- textures overlap each other;
- when Niko goes up to a building floor, grass is drawn instead of the floor's material;
- frames drop at zoom x1 but not at x4.

All three causes were found by reading the code. Nothing was profiled in a browser, so the costs
in [Sec. 1] are estimates, and the acceptance in [Sec. 7] is what measures them.

## 1. Findings

### F1: Floors flagged `LEVEL_VOID` are never drawn, and the excavated grass is drawn instead
`LEVEL_VOID` means *the ground volume of this level band is excavated*. It does not mean *this
cell has no floor*. The server reads it that way (`world/grid.py` `_void_at`, `standing_surfaces`),
and so does the client's prediction (`ChunkStore.isVoid`, `standingH`). The renderer and the picker
read it the other way:
- `MapScene.drawChunk` (`web/src/game/MapScene.ts` line 377) skips any floor with `LEVEL_VOID`.
- `pickTile` (`web/src/world/pick.ts` line 23) does the same.
- Neither checks whether the ground top itself is void.

In the test world the building's basement (`z = 1`, `h = 6`) and ground floor (`z = 2`, `h = 12`,
concrete) are both VOID-flagged over the interior, and the interior ground is grass at `h = 12`
(`world/gen/testworld.py`). The server stands Niko on the concrete, while the client draws the
excavated grass and no concrete. Inside the building Niko walks on grass, and the basement has
no floor. Dev-008 [Sec. 3.4] wrote "skipping VOID cells", so the error comes from the plan.

### F2: Grass images are drawn over the whole base layer
Everything below Niko's feet goes to one `base` `Graphics` per chunk (Dev-008 [Sec. 3.2]). Grass
tops are separate `Image`s at the same depth. They are recreated on every redraw, so they always
sit later in the display list than the `Graphics`, and Phaser draws equal depths in list order. As
a result every grass tile below Niko is painted over *all* of its chunk's base content: cliff faces
in front of it, walls, floors and non-grass tops.

This is the overlap the user sees. It gets worse the higher Niko stands, because more of the world
falls into `base`: on a hill, the grass behind covers the cliffs in front, and on the roof the
ground grass covers the building's walls and floors. The row layers are not affected, because
tiles in one row never overlap on screen (each tile's faces and walls stay inside its 64 px
column), and a grass top only overlaps a floor at its own height, which is the F1 case.

### F3: Redraw storm, which scales with the visible area
`redrawVisible()` redraws every loaded chunk in view. Its triggers are:

| Trigger | Where | Frequency while walking |
|---|---|---|
| Niko changes tile | `setViewer` (every frame from `update`) | about 4–6 per second at 4 m/s |
| The ack and `update` disagree on the tile or `h` | `onAck` calls `setViewer` with the server position, and the next frame calls it with the prediction, which runs ahead | 1–3 extra redraws on every tile crossing |
| The camera moves 96 world px | `refreshView` | about 1.5 per second |
| `h` or cutoff changes | `setViewer` | on every step up or down |

Each redraw destroys and recreates one `Image` per grass tile in the expanded view (+256 px). At x1
on 1920×1080 that is about 3,800 images, against about 250 at x4. `DisplayList` removal is
`indexOf` plus `splice`, so destroying them costs O(n²). On top of that every `Graphics` of about 25
chunks re-records its commands, and `chunkBounds` is recomputed per chunk. That is a hitch of
roughly 50–100 ms, 5–8 times per second, at x1. At x4 the view holds about 1/16 of the tiles, so the
same triggers cost little. This matches the report.

Also: every grass tile is filled twice (a vector diamond under the image).

## 2. Decisions to approve

| Topic | Decision | Why |
|---|---|---|
| VOID | A floor is drawn and picked whenever `floor_h ≠ NO_FLOOR`, VOID or not. The ground top and its faces are skipped when the ground's own band is VOID (`ChunkStore.isVoid(x, y, groundH)`, made public) | Same meaning as the server and the prediction |
| Terrain drawing | Replace the terrain `Graphics` and grass `Image`s with **one `Blitter` per layer** (the chunk base and each row) over a single runtime atlas `terrain`. The atlas holds the 4 grass frames plus white masks (diamond, face units, wall units, edge line) that are tinted to today's colours | Bobs inside a `Blitter` draw in insertion order, so grass tops interleave with faces and walls in true painter's order (fixes F2). One texture and one object per layer batch in a single pass, bobs are cheap to rebuild (no display-list churn), and nothing is filled twice |
| Split plane | Kept, as Dev-008 [Sec. 4] requires. Units are whole `h` steps, so a face splits at `viewerH` by assigning each unit bob to `base` or to its row | No change in behaviour |
| Viewer | Only `update()` calls `setViewer`, with the rendered prediction. `onAck` and `onState` stop calling it | Ends the ack/prediction flip-flop. The next frame already renders the reconciled position |
| Redraw triggers | Narrowed per cause [Sec. 3.4], and run through a time-budgeted queue | Most tile crossings and camera moves no longer redraw anything |
| Scope | Web only (`web.game`, `web.world`). The server, protocol and schema stay untouched | The server is correct |

## 3. What changes

### 3.1 VOID (`web.world`, `web.game`)
- `ChunkStore.isVoid(x, y, h)` becomes public; its body stays the same.
- `MapScene.drawChunk`: drop `(flags & LEVEL_VOID) === 0` from the floor test. Draw the ground top
  and its south and east faces only when `groundShown && !store.isVoid(x, y, h)`.
- `pick.ts`: drop the VOID test on floors. The ground is not a hit when
  `store.isVoid(x, y, groundH)`, so the march continues downwards and finds the basement floor.
- `cutaway.ts` stays as it is. It already ignores VOID, and its results for the test building are
  correct once the floors exist.
- Known limit: a face whose neighbour's ground is void still stops at that neighbour's `groundH`.
  The test building hides this behind its perimeter walls ([Sec. 9]).

### 3.2 Atlas and masks: `web/src/game/tileMasks.ts` (new, pure)
All shapes derive from one diamond rule, which matches `src/sprites/grass/grass_x4.png`
pixel for pixel (checked: binary alpha, same mask in all 4 frames):

```text
pixel (c, r) of the 64×32 diamond is opaque  ⇔  |c − 31.5| ≤ 2·min(r, 31 − r) + 0.5
```

- `diamondMask()`: the rule above.
- `faceMask(side: "s" | "e", units: 1 | 2 | 4 | 8)`: the south face takes columns 0–31 and the east
  face columns 32–63. For column `c`, let `B(c)` be the diamond's lowest opaque row, with the pattern
  continued through the tip column (`B(0) = 15` and `B(63) = 15`). The face covers rows
  `B(c) + 1 … B(c) + 16·units`. The tip columns must be included, or every cliff shows a 1 px
  background seam at each tile's side tip.
- `wallMask(edge: "n" | "w", units: 1 | 2 | 4)`: an N wall takes columns 32–63 and a W wall columns
  0–31. For column `c`, let `T(c)` be the diamond's highest opaque row, with the pattern continued
  through the tips. The wall covers rows `T(c) − 16·units … T(c) − 1`. Derive it from `T`; do not
  reuse `faceMask` unless the tests prove the two are identical.
- `edgeLineMask(edge)`: the 1 px row `T(c)` over the wall's columns, used as the wall's top line.
- Each mask comes with its offset from the tile's 64×32 frame origin, so
  `bob = toScreen(x, y, h) − (32, 0) + offset`, where `(x, y)` is the tile's top corner.

`MapScene.create()` builds the canvas texture `terrain`:
- It copies the 4 grass frames, then writes the masks as white pixels.
- It adds one named frame per shape (`grass0–3`, `top`, `faceS1…faceE8`, `wallN1…wallW4`,
  `lineN`, `lineW`) with `texture.add`, then calls `refresh()`.

A height of `n` units is drawn as its binary decomposition: at most 4 bobs, and a 6-unit wall is
2 bobs.

### 3.3 Layers (`MapScene.ts`)
- `ChunkLayers` becomes
  `{ base: Blitter; rows: Map<number, Blitter>; bbox; hMin; hMax }`, with the same depths as
  today (`baseDepth(cx, cy)`, `rowDepth(row)`). `grass: Image[]` is removed.
- `drawChunk` keeps its walk order and split rule exactly. Only the primitives change:
  - `drawTop`: grass gets a `grassN` bob tinted `tintGrass(h)`. Any other material gets a `top` bob
    tinted `shade(color, h)`. There is no vector diamond underneath.
  - `drawVertical(h0 → h1)`: decompose the range into unit runs, and put a run in `base` when its top
    is `≤ viewerH`, otherwise in its row. Tint is `scaleColor(color, light)`. Glass is a bob with tint
    `0xd8e6f0` and `alpha 0.45`.
  - The wall top line is a `lineN`/`lineW` bob tinted `scaleColor(color, 1.12)`.
- A redraw calls `blitter.clear()` and rebuilds the bobs. Rows that stay empty are destroyed, as
  today.
- `bbox`, `hMin` and `hMax` are computed once per chunk revision, from the chunk's data and not the
  cutoff: `hMin` is the ground min − 4 and the lowest slab − 1, and `hMax` is the max of the ground,
  floors + 6 and wall tops. They are cached on the layers.
- The same data drives the stub and cutoff rules, so `isFrontWall`, `cutoffH` and the ground cutaway
  do not change.

### 3.4 Redraw triggers
Pure selection helpers in `web/src/game/dirty.ts` (new), fed with chunk keys, `hMin`/`hMax`, and
whether a chunk has levels:

| Trigger | Chunks marked dirty |
|---|---|
| A chunk arrives or changes | That chunk and its 4 neighbours (as today) |
| Materials load | All |
| `viewerH` changes from `a` to `b` | Chunks with `hMin < max(a, b)` and `hMax > min(a, b)`. No other chunk changes its split |
| Cutoff changes | Chunks with levels only. The ground cutaway only applies under floors |
| Niko changes tile | Chunks with levels that intersect the square ±8 tiles around the old or the new tile. The stub rule [Dev-008 Sec. 3.4] cannot reach further |
| Camera moves or resizes | Nothing. Only culling runs |
| Snapshot | Destroy every layer (as today) |

The **queue**: dirty chunks inside the 3×3 chunk block around Niko redraw in the same frame. The
rest redraw nearest first, until 4 ms of the frame are spent (`performance.now()`, render-side
only). Offscreen dirty chunks stay queued until they come into view (as today).

Culling runs every frame over the cached `bbox`es, and calls `setVisible` only when a chunk's
visibility flips. `refreshView`, `lastView`, `inGrassView` and the 96 px rule are removed.

### 3.5 Viewer
- `onAck`: keep `prediction.reconcile`, remove `setViewer`.
- `onState`: keep `prediction.reset`, remove both `setViewer` calls.
- `update()` stays the only caller of `setViewer`. Its early return is unchanged.

### 3.6 Tests (`web/tests/`)
- `isometric-world.test.ts`:
  - A VOID-flagged floor is picked.
  - A void ground is skipped, and the pick reaches the basement floor below it.
  - `isVoid` is true for the test building's interior ground at `h = 12`.
- `tile-masks.test.ts` (new):
  1. A 4×4 grid of diamonds covers its inner area exactly once, with no gaps and no overlaps.
  2. Stacking `faceMask(s, 1)` four times equals `faceMask(s, 4)`.
  3. A 2×2 plateau `k` units (1–3) above flat ground: its tops, its south and east faces, and the
     lower diamonds cover the plateau's silhouette with no holes, and the faces never cover the
     plateau's tops.
  4. A wall unit sits directly on the diamond's top boundary, with no gap and no overlap.
- `dirty.test.ts` (new): each trigger in [Sec. 3.4] selects exactly the expected chunks, including
  "a tile change far from any level selects nothing".

### 3.7 Docs
- `CONTEXT.md`:
  - `web.game` row: say that terrain is drawn with per-layer `Blitter`s over the runtime `terrain`
    atlas, that redraws are queued, and that culling is per frame.
  - Measured pitfalls: `LEVEL_VOID` excavates the ground band and never removes a floor; a separate
    `Image` at a `Graphics` layer's depth draws over the whole layer; destroying many display-list
    objects is O(n²).
- `docs/PENDING.md`: this doc's entry, replaced when done.
- No change to `VISION.md`.

## 4. Traps

- **Do not remove the split plane or change the walk order.** This fix changes the primitives, not
  the sorting.
- **Tip columns.** A face or wall mask built only from the diamond's own opaque columns leaves a
  1 px seam at every side tip. The mask tests catch it.
- **Bob position is the frame's top-left**, not its centre.
- **`Blitter` has a single texture.** Everything drawn in a layer must come from `terrain`. Niko
  stays a `Graphics`.
- **`hMin`/`hMax` must ignore the cutoff.** If they follow what is drawn, a chunk with hidden upper
  floors never gets redrawn when those floors come back.
- **Removing `setViewer` from `onState`** must not skip the first draw: `update()` runs on the next
  frame with the reset prediction.

## 5. Modules and versions

| Module | Version |
|---|---|
| web.game | v0.1.3 → v0.1.4 |
| web.world | v0.0.5 → v0.0.6 |

The overall project version is suggested to move to `v0.6.1` (bugfix), after Dev-008 is committed
as `v0.6.0`.

## 6. What must not break

- Everything in Dev-008 [Sec. 6]: server-authoritative movement and replay, Fix03/Fix04 timing,
  the `{x, y, z}` menu contract, integer zoom x1–x4, chunk revisions and neighbour redraws,
  clearing on snapshot, the idle resync, and no text drawn in Phaser.
- Today's colours: the tops' `shade`, the ×0.82 / ×0.66 faces, the grass tint, and the glass.
- The roofed cutaway, the front-wall stubs, the ground cutaway, and picking through hidden floors.

## 7. Checks and acceptance

Checks: `cd web && npm test && npm run gen:types && git diff --exit-code src/net/schema.d.ts &&
npm run build`, plus the server checks, which must pass unchanged.

Manual, with `EtherBound.exe`, new game with seed 0, at 1920×1080:
1. **Ground floor:** entering the west doorway (136, 146), the floor is concrete, not grass.
   `inspect` on it names concrete.
2. **Basement:** down the south flight (x 145–150, y 152), the concrete basement floor is drawn,
   and right-clicking it gives `z = 1`.
3. **No overdraw:** on the terrace (y 89–94) and on the roof, grass behind never covers cliff faces,
   walls, floors or the road in front. Grass tiles still show the atlas.
4. **No seams:** cliffs, slab edges and walls show no 1 px background lines at x1–x4.
5. **x1 flat walk** across grass, away from the building, for 10 s: ≥ 58 fps, and DevTools shows
   no `drawChunk` during the walk except when crossing into new chunks.
6. **x1 hills and ramps:** no frame over 33 ms when `h` changes.
7. **x1 chunk streaming** across several chunk borders: no frame over 33 ms.
8. **Building at x1:** walking along the walls, the stubs still follow Niko, and upper floors hide
   and show as in Dev-008 [Sec. 7] 5–8.
9. **x4:** the fps is not worse than today.

Automated checks passed. GUI visual and performance acceptance 1–9 remains pending in
`docs/PENDING.md` because the renderer was not launched in a browser during this change.

## 8. Todo

### Decisions
- [x] Approve [Sec. 2] (approved by the request to execute Fix05)

### Code
- [x] VOID in the renderer and the picker, public `isVoid` [Sec. 3.1]
- [x] `tileMasks.ts` and the `terrain` atlas [Sec. 3.2]
- [x] Blitter layers, cached `bbox`/`hMin`/`hMax` [Sec. 3.3]
- [x] `dirty.ts`, redraw queue, per-frame culling; remove `refreshView` and grass images [Sec. 3.4]
- [x] `setViewer` only from `update()` [Sec. 3.5]

### Checks
- [x] Tests [Sec. 3.6]; web build, generated types, server checks
- [ ] Manual acceptance 1–9 [Sec. 7]

### Closing
- [x] `CONTEXT.md`, `PENDING.md` [Sec. 3.7]; `docs/utils/VERSION.md` [Sec. 5]
- [x] Notion: Work Report for the date and a Dev Blog page
- [x] Move this doc to `docs/done/`

## 9. Out of scope

- Faces toward excavated neighbours [Sec. 3.1]. They need a "solid top below the void" rule that
  follows the strata.
- Art for materials other than grass. The white masks are also the slots where future sprites will
  go.
- Other actors' sorting, the height pop on ramps, and the other items in Dev-008 [Sec. 9].

---

## TL;DR

Three defects in the isometric renderer:
1. **Grass instead of floors:** it read `LEVEL_VOID` as "no floor", but it means "the ground here is
   excavated". Draw and pick VOID floors, and hide the void ground instead.
2. **Overlapping textures:** the grass `Image`s draw over the whole base `Graphics` layer. Terrain
   becomes one `Blitter` per layer over a runtime atlas (grass frames plus white masks tinted to
   today's colours), so tops, faces and walls keep true painter's order.
3. **x1 frame drops:** every tile crossing, camera move and ack redrew every visible chunk,
   recreating about 3,800 grass images at x1. Redraws are now narrowed per trigger and queued,
   camera moves only cull, and only `update()` moves the viewer.

The split plane and the walk order stay. Web only: `web.game` v0.1.4, `web.world` v0.0.6.
