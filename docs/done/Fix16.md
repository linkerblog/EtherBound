# Fix16: Occlusion sees slab edges and feet

[Bugfix] [Rendering] [Review]

After Fix14 the user stood behind the lab building's north wall near its east end. The first-floor
slab still covered Niko's head: no cut and no silhouette. A small step left and the cut came on
(GUI shot, 24/09/2026). The rays test only a floor's **top** plane. Every slab is drawn as a
0.5 m box, with south and east faces under its top. From the side, the ray passes through that
face and misses the top. Two probe gaps come from the same rewrite: the highest probe is below
the sprite's top, and there is no probe at the feet. This doc makes the occlusion rays see the slab
as drawn, and the probes cover the whole sprite.

Reference: `docs/done/Fix14.md` (body probes, tracker, sweep test); `web/src/world/occlusion.ts`,
`web/src/world/ray.ts`; `chunkRenderer.ts` (slab faces `floorH − 1 .. floorH`, drawn when the
south or east neighbour's `floor_h` on that level differs).

## 0. Findings (measured 24/09/2026 on the Fix14 working tree)

Measured with throwaway `tsx` scripts that use the Fix14 `labBuilding()` fixture. They were deleted
afterwards. **Oracle:** each slab is the drawn box `[F − 1, F]` on its tile. The sprite is sampled
every 1 px vertically and about 1 px across (at x1), and a point counts as covered when its view
ray leaves such a box through a drawn surface (the top, or a drawn south or east face). Only floors
above `viewerH + 4` count, because those are the ones the cut must remove.

| # | Where | Finding |
|---|---|---|
| F1 | `marchRay` via `bodyHits` | A floor hits only when the ray is at `h = floor_h` over that tile: the top plane. The drawn south and east faces are not tested. At `(25.0, 45.2, 2)`, as built, `bodyHits` finds nothing and `isCovered` is false. The head ray crosses the east face of slab tile `(25, 46)` at `x = 26`, `h = 7.5`. At `(24.5, 45.5)` it hits the top and the cut works: that is the user's "a bit to the left" |
| F2 | `PROBE_HEIGHTS = [1, 2.5, 3.5]` | The sprite runs from 0.125 to 3.75 (`fillRoundedRect(−9, −60, 18, 58)`, 16 px per unit). A slab whose lower edge crosses between 3.5 and 3.75 is missed, and so is one that covers only the feet (below 1). Before Fix14, the feet ray caught the feet case |
| F3 | Measured in the ring x 12..31, y 40..58 at a 0.1 step, 29,400 positions outside the footprint | As built, **1,064** positions are covered by a high floor and not cut, with up to 551 sampled px. With faces and a 3.75 top probe: 675 and 253 px. With faces and probes `0.125, 1, 2, 3, 3.75`: **4** positions and at most **6 px**, a far wood corner grazing the feet row 6 tiles away |
| F4 | Fix14's sweep test | It checks `bodyHits` against `bodyHits`, so it cannot see what the probes miss. The oracle must be independent |
| F5 | Spawn `(21.5, 41.5, 2)` with the independent oracle | It has 185 covered samples before cutaway. The tracker selects the lab and cuts the slab; preserving an uncut spawn would contradict the sprite-coverage oracle. The user approved honoring this geometry and updating affected baselines |

## 1. Decisions

| Topic | Decision | Why |
|---|---|---|
| Slab test | New exact `slabHit`. The ray hits a slab tile `(X, Y)` with floor `F ≤ cutoffAt(X, Y)` when it passes through the box `[X, X+1] × [Y, Y+1] × [F − 1, F]` and **leaves** it through a drawn surface. The top counts always, the east face (`x = X + 1`) only when the east neighbour's floor on that level is not `F`, and the south face (`y = Y + 1`) the same way | That is exactly what `chunkRenderer` draws. When a face is not drawn, the ray goes on into the neighbour's box, which is tested in its own right |
| Where | `bodyHits` = today's `marchRay` hits (objects, ground, floor tops) **plus** `slabHit`. `marchRay` and picking do not change | Picking keeps its behaviour. Occlusion gains the faces |
| Probe heights | `0.125, 1, 2, 3, 3.75`, three columns each (`−W, 0, +W`, `W = 9/32`). That is 15 rays | The sprite's bottom and top rows, and nothing more than 1 unit apart. F3: 4 positions left, 6 px at worst |
| Feet probe | The 0.125 probe counts only `slabHit` results with `F > viewerH + 4`, for both selection and the silhouette | Its job is high slabs seen from far away. Counting low ground and steps would light the silhouette behind every 0.5 m terrace |
| Residual | The 4 positions and 6 px of F3 are accepted | Denser sampling costs rays every frame for a corner 6 tiles away |
| Tracker, cutoff, release | Unchanged (Fix14) | `slabHit` returns a `RayHit` of kind `"floor"` for its tile, so selection and the flood fill work as they are |

## 2. Changes

**web.world** (`web/src/world/occlusion.ts`)
- `PROBE_HEIGHTS = [0.125, 1, 2, 3, 3.75]`, with a comment deriving the ends from the sprite rect in
  `MapScene`. `FEET_PROBE = 0.125`.
- `slabHit(store, x0, y0, h0, maxH, cutoffAt): RayHit | null`. The ray is
  `(x0 + k, y0 + k, h0 + 2k)` for `k ≥ 0` until `h0 + 2k > maxH` (`viewerH + 40`). Walk the plan
  tiles it crosses: start at `(⌊x0⌋, ⌊y0⌋)` and step into the neighbour across whichever of the
  next x or y boundary is nearer, taking both on a tie. For each tile and each level with a floor
  `F ≤ cutoffAt(X, Y)`:
  - `kIn = max(X − x0, Y − y0, (F − 1 − h0) / 2, 0)`
  - `kOut = min(X + 1 − x0, Y + 1 − y0, (F − h0) / 2)`
  - if `kIn < kOut`, the exit plane is the one that set `kOut`: top, east or south. Return
    `{ x: X, y: Y, h: F, kind: "floor", z: level.z }` if that surface is drawn [Sec. 1].
  Return the first hit in walk order. Any hit is enough for occlusion.
- `bodyHits`: for each probe height and column, the probe point is `(x + ds/2, y − ds/2,
  viewerH + p)`. Keep the `marchRay` call, except for `FEET_PROBE`, then add `slabHit` from that
  point. For `FEET_PROBE`, keep only `slabHit` results with `h > viewerH + 4`. `isCovered` and the
  tracker already build on `bodyHits`.

**web.game**: no change. Per frame this is 15 `marchRay` + 15 `slabHit` walks for selection, and
the same again for the silhouette. Each walk crosses about 40 tiles with a few levels each.

No `server.*`, `ray.ts`, `pick`, `chunkRenderer`, `wallPainter` or `MapScene` change.

## 3. What must not break

- Picking: `marchRay` is untouched.
- Every Fix14 guarantee: selection reads the uncut world, the one-tile release, and the cut at
  `viewerH + 4` with the fallback.
- The silhouette does not light up for 0.5–1 m steps or terraces in front of Niko's feet, or
  for the building he just walked past once it is cut.
- At the lab spawn `(21.5, 41.5, 2)`, the oracle's 185 uncut pixels are removed by the tracked slab
  cut. Re-shoot only the affected `building-x1/x2` baselines after checking that Niko is no longer
  covered; `spawn-x1/x2/x4` use the separate test generator and should stay unchanged. The changed
  lab-spawn cut is the approved outcome.

## 4. Acceptance

### Automated

1. `web/tests/occlusion.test.ts` (Fix14's fixture):
   - **Reported spot:** at `(25.0, 45.2, 2)`, `bodyHits` on the uncut world includes tile
     `(25, 46)` at `h = 8` and `isCovered` is true. The tracker cuts the building at 6, and after
     that `isCovered` is false.
   - **Faces:** a single floating slab tile. A ray that leaves through its drawn east face hits.
      With an east neighbour at the same `F`, fewer hits resolve to the first tile and at least one
      continues into the neighbour, through its top or face.
   - **Feet:** a position where only the sprite's bottom rows are behind a high slab is
     selected. A 0.5 m ground step in front of the feet does not make `isCovered` true.
   - **Oracle sweep (replaces Fix14's sweep):** a test-local `coveredPixels(store, x, y, h,
     cutoffAt)` samples the sprite every 1/16 unit in height and `W/9` across, and uses the box
     rule of [Sec. 0]. It does not use `bodyHits`. Over the ring of F3 at a 0.1 step, after a fresh
     tracker update, the positions with covered pixels are at most 4 and at most 6 px each. The
     test prints them on failure.
   - The other Fix14 and Fix06 cases stay green.
2. `npm run check`, plus `npm run check:visual`: only `building-x1/x2` may change to show the
   approved spawn cut; `spawn-x1/x2/x4` stay unchanged.

### Manual

3. Manual (GUI, x1 and x2, the user's lab save): stand at the third screenshot and spawn positions,
   then walk slowly along the outside of the north and west walls, past the corners, and back 3–6
   tiles. Pass when the tracked slab cut keeps its covered pixels off Niko, there is no silhouette
   for steps and terraces, and walking feels as smooth as before. Manual because it covers the user's
   save and frame feel, and headless shots cannot walk Niko.

## 5. Docs to update in the same change

- `CONTEXT.md`, the Fix14 occlusion pitfall: add "slabs are 0.5 m boxes, so occlusion rays test
  their drawn faces, not only the top; probes span the whole sprite (0.125 to 3.75); sweep tests
  need an oracle independent of the probes".
- `docs/PENDING.md`: merge manual 3 into the Fix14 manual entry, which covers the same walk.
- `docs/utils/VERSION.md`, the Notion Systems Index, the Dev Blog entry and the daily Work Report.

## 6. Modules and versions

| Module | Change | Version |
|---|---|---|
| web.world | `slabHit`, probe heights, feet probe rule | v0.0.11 → v0.0.12 |

Overall project version: assigned at release by `docs/utils/COMMITS.md` [Sec. 2]. It lands after
the Fix14 release commit, in any order with Fix15. Both are web-only and touch different
functions. Dev-023's clean-tree precondition applies as in Fix15 [Sec. 6].

## 7. Todo

- [x] `occlusion.ts`: `slabHit`, `PROBE_HEIGHTS`, `FEET_PROBE`, `bodyHits`
- [x] `occlusion.test.ts`: the cases of [Sec. 4] item 1 and the independent oracle sweep
- [x] `npm run check`; `npm run check:visual` passes after updating only `building-x1/x2`
- [x] Manual 3 merged into the Fix14 entry of `docs/PENDING.md`
- [x] `CONTEXT.md`, `docs/utils/VERSION.md`
- [x] Notion
- [x] Move this doc to `docs/done/`

## 8. Out of scope

- Walls in the occlusion rays (Fix14 [Sec. 8]). Front-wall stubs are Fix15.
- Objects drawn taller than their `height`, and Extras.
- The 6 px residual of F3.

---

## TL;DR

Fix14's rays only hit a slab's top, but slabs are drawn as 0.5 m boxes. Behind the lab's north wall
near its east end, Niko's head passed through the slab's east face, so the building never cut.
The probes also stopped short of the sprite's top and had no feet probe. Occlusion now tests the
drawn box surfaces exactly, and the probes run 0.125 to 3.75 across 3 columns. Around the lab this
cuts uncut coverage from 1,064 positions to 4 (6 px at worst). An independent oracle sweep now
guards it. `web.world` only.
