# Fix14: Occlusion follows Niko's body

[Bugfix] [Rendering] [Review]

Niko can still stand behind a building and lose his head under its upper floors, with no cutaway
and no silhouette (user's GUI shot, lab `structure` bay, 24/09/2026). Fix06 designed the probes as
rays through Niko's body at three heights. The code traces the ray through his **feet** at all
three and only shortens it, so it sees what covers his feet and misses what covers his head.
When the building is selected, its stair steps also push the cut above the first-floor slab. This
doc makes the probes follow the body, cuts a selected structure exactly like the one Niko stands
in, and moves selection from the tile centre to his real position.

Reference: `docs/done/Fix06.md` [Sec. 2], [Sec. 3.3] (the design this restores);
`docs/utils/VISION.md` [Sec. 5] "Buildings" (unchanged: "hides its storeys above him in the same
way"); `CONTEXT.md` "Occlusion probes follow Niko's body".

## 0. Findings (measured 24/09/2026 on the Fix13 working tree)

Measured with throwaway `tsx` scripts that call `occlusion.ts` on a copy of the lab building
(floors `h` 2/8/14 on x 18..25, y 46..51, enclosing walls, both stair flights). The scripts were
deleted afterwards.

| # | Where | Finding |
|---|---|---|
| F1 | `occlusion.ts` `occludingStructures`, `isCovered` | Every probe uses `t = x + y − viewerH` (the feet ray) and only changes the stop height. A floor that covers the head but not the feet is never seen. At `(24.5, 45.5, 2)`, just behind the north wall's east end, the slab at `h = 8` covers the head. The result is no structure and `isCovered = false`, so no cut and no silhouette. Around the building, **20 tiles** miss a selection that body rays find |
| F2 | `structureFrom` cutoff | `F` is the highest floor `≤ viewerH + 4` and counts stair steps. In the lab the steps at 3..6 give `F = 6` and a cutoff of 10, so the slab at 8 stays. Measured at `(21.5, 45.5, 2)`: selected, cut at 10, still covered. VISION [Sec. 5] asks for "in the same way" as roofed, i.e. `viewerH + 4` |
| F3 | `MapScene.setViewer` | Selection probes the tile centre and reruns only when the tile changes. Niko can be anywhere in the tile, so the centre's answer is wrong for part of it |
| F4 | `occlusion.test.ts` | The building is 5 tiles ahead, where the feet and body rays agree. The single-tile hill only touches the feet ray (at `h = 12` it lies exactly on the hill's back corner), so that test passes only because of F1 |

## 1. Decisions

| Topic | Decision | Why |
|---|---|---|
| Probe rays | Each probe traces the ray through `(x, y, viewerH + p)`: `s = x − y`, `t = x + y − (viewerH + p)`, marched from `viewerH + 40` down to `viewerH + p + 0.5`, `p` in 1, 2.5, 3.5 | Fix06 [Sec. 3.3] as written. Everything on that ray above the probe is in front of that body point |
| Body width | Each height is probed at three columns, `s + ds` with `ds` in `−W, 0, +W` and `W = 9 / 32`. That is Niko's 18 px body (`MapScene`) in ray units | A slab edge that covers his side counts |
| Selection point | `occludingStructures` takes Niko's rendered `(x, y)`, not the tile | F3. It is the same point `isCovered` uses, so "selected" and "covered" cannot disagree |
| Selection timing | Probed every frame against the uncut world (global `cutoff` only). A floor hit outside the kept structures selects its structure at once. A kept structure is released when Niko is more than one tile (x or y) from the last tile where it covered him, or when `viewerH` changes and it no longer covers him | A building is never uncut while it covers him. The one-tile margin stops cut/uncut flashing while walking along a wall. Probing the uncut world still cannot feed back on itself |
| Structure cutoff | `viewerH + 4` when the structure has any floor `≤ viewerH + 4`. Otherwise its lowest floor + 4, the Fix06 fallback, unchanged | F2. Same cut as roofed, as VISION says. Stair steps and plinths no longer lift it. The fallback stays because cutting all floors shows the void ground under the building as a black hole (Fix06 [Sec. 2]). The silhouette covers that case |
| Low occluders | Floors in `(viewerH, viewerH + 4]` stay drawn (stair steps, a porch) and may cover his legs. The silhouette shows | These are not "high floors". They are what he walks on next |
| Vision | No change | VISION [Sec. 5] already describes the fixed behaviour |

## 2. Changes

**web.world** (`web/src/world/occlusion.ts`)
- `BODY_HALF_S = 9 / 32`, with a comment tying it to the Niko body in `MapScene`.
- New `bodyHits(store, x, y, viewerH, cutoffAt): RayHit[]` returns the 9 probes of [Sec. 1].
  `isCovered` returns true when any probe hits, stopping at the first hit.
- `occludingStructures(store, x, y, viewerH, cutoff)` takes the exact position, calls
  `bodyHits` with `() => cutoff` and flood-fills the floor hits as today. `Structure` gains
  `seed: {x, y}`, the hit tile it was filled from.
- `structureFrom`: `cutoff = eligible.length > 0 ? viewerH + 4 : min(floors) + 4` (a structure with
  no floor at all keeps `viewerH + 4`).
- New `StructureTracker` (pure, no Phaser):
  - `update(store, x, y, viewerH, cutoff): StructureBounds[]`. In order:
    1. When `viewerH` or `cutoff` changed since the last call, rebuild every kept structure from
       its `seed`. A seed that no longer has a structure cell is dropped.
    2. Run `bodyHits` against `() => cutoff`. A floor hit inside a kept structure sets that
       structure's `anchor` to Niko's tile. A floor hit outside all of them flood-fills a new one
       (anchor = Niko's tile). The fill runs only in that case.
    3. Release every structure whose anchor is more than one tile from Niko's tile in x or y. On
       an `h` change, also release those not hit in step 2.
    4. Return the bounds of added, released and rebuilt-with-a-new-signature structures (tiles and
       cutoff, as the signature in `MapScene` today). The array is empty when nothing changed.
  - `rebuild(store)`: step 1 on demand, for chunk updates. It returns bounds the same way.
  - `cutoffAt(x, y, globalCutoff)`: `min(globalCutoff, cutoff of the kept structure holding the
    tile)`. `touches(cx, cy, size)`: whether a chunk intersects any kept structure. `clear()`.

**web.game** (`web/src/game/MapScene.ts`)
- `structures`, `refreshStructures` and `chunkTouchesStructure` give way to one
  `StructureTracker`.
- `setViewer` calls `tracker.update(chunks, x, y, h, nextCutoff)` **every frame**, with the
  rendered position. Returned bounds go to `structureChunkKeys` and then `markDirty`. The tile and
  cutoff dirty logic stays as it is.
- `cutoffAt` delegates to `tracker.cutoffAt(x, y, this.cutoff)`. On a snapshot the tracker is
  cleared; in `onChunk`, `tracker.touches` → `tracker.rebuild`.
- `isCovered` stays one call per frame (its signature does not change). Per frame that is 9 rays
  for the silhouette plus 9 for selection, about 75 steps each. The flood fill runs only on a new
  hit.

No `server.*`, schema, `chunkRenderer`, `wallPainter`, `cutaway.ts` or `ray.ts` change.

## 3. What must not break

- Selection reads the uncut world. Cutting a structure never changes what selects it
  (`CONTEXT.md` pitfall), and nothing flickers while Niko stands still.
- The roofed cutaway (`cutoffH`), the front-wall stubs, picking and Extras visibility (all through
  `cutoffAt`) behave as before.
- At the lab `structure` spawn `(21.5, 41.5, 2)` nothing is selected. Measured: no body probe hits
  there. So `building-x1/x2` and `spawn-x1/x2/x4` stay pixel-identical. If one changes, find out
  why before re-shooting.
- A structure behind Niko is never selected. A hill still shows the silhouette and never counts as
  a structure.
- No redraw on frames where the tracker returns no bounds.

## 4. Acceptance

### Automated

1. `web/tests/occlusion.test.ts`, new fixture `labBuilding()`: the Fix13 lab building in chunk
   `(0, 1)`, as listed in [Sec. 0], on flat ground `h = 2`. It mirrors `_structure` in `lab.py`,
   including both stair flights and their stairwells.
   - **Reported spot:** at `(24.5, 45.5, 2)`, `isCovered` on the uncut world is true. The tracker
     selects one structure with cutoff 6, and `isCovered` through `tracker.cutoffAt` is false.
   - **Sweep:** for every position on a 0.25-tile grid over x 12..31, y 40..58 outside the
     footprint, a fresh tracker is updated once. After that, no `bodyHits` result through its
     `cutoffAt` has `h > viewerH + 4`. The prototype ran 4,704 positions, selected at 1,167 and
     left 53 hits, all stair steps `≤ 6`. This is the test that would have caught F1 and F2.
   - **Stairs:** at `(21.5, 45.5, 2)` the cutoff is 6, not 10.
   - **Spawn:** at `(21.5, 41.5, 2)` the tracker selects nothing.
   - **Hysteresis:** select at `(24.5, 45.5)`, then update at `(25.5, 44.5)`, which is within one
     tile and uncovered (the test asserts that first). The structure is kept and no bounds are
     returned. At `(27.5, 43.5)`, also uncovered, it is released and its bounds are returned.
   - **Cutoff:** the Fix06 cases keep their structures. "Behind Niko" stays empty; floors `[6, 12]`
     at `viewerH = 4` now cut at 8 (was 10). Floors `[10, 16]` at `viewerH = 0` still cut at 14 (the
     fallback, now pinned).
   - **Hill:** the hill becomes a 4 × 4 block at `h = 12` on tiles 6..9 (F4). `isCovered` is true
     and no structure is selected.
2. `npm run check`, plus `npm run check:visual` with every baseline unchanged.

### Manual

3. Manual (GUI, x1 and x2, the user's lab save): walk the outside of the lab building along
   all four walls and stand at the spot in the user's screenshot. Pass when no floor more than
   2 m above Niko hides any part of him, walking along a wall causes no cut/uncut flashing, the
   silhouette shows only for steps, hills and objects, and walking feels as smooth as before.
   Manual because it is a taste and frame-feel check the headless shots cannot move Niko to see.

## 5. Docs to update in the same change

- `CONTEXT.md`: a pitfall, "Occlusion probes trace the ray through each body point
  (`t = x + y - (viewerH + probeH)`), never the feet ray shortened", and selection is per frame from
  Niko's position with a one-tile release. The `web.world` row is unchanged.
- `docs/PENDING.md`: add manual item 3.
- `docs/utils/VERSION.md`, the Notion Systems Index, the Dev Blog entry and the daily Work Report.

## 6. Modules and versions

| Module | Change | Version |
|---|---|---|
| web.world | Body probes, `StructureTracker`, `viewerH + 4` structure cutoff | v0.0.9 → v0.0.10 |
| web.game | `MapScene` uses the tracker every frame | v0.1.16 → v0.1.17 |

Overall project version: assigned at release by `docs/utils/COMMITS.md` [Sec. 2]. It lands after
the Fix13 release commit (Fix13 is implemented in the working tree but not yet committed), and
before Dev-021.

## 7. Todo

- [x] `occlusion.ts`: `bodyHits`, exact-position `occludingStructures`, `seed`, cutoff rule
- [x] `occlusion.ts`: `StructureTracker` (`update`, `rebuild`, `cutoffAt`, `touches`, `clear`)
- [x] `MapScene.ts`: tracker wiring in `setViewer`, `cutoffAt`, snapshot and `onChunk`
- [x] `occlusion.test.ts`: lab fixture and cases of [Sec. 4] item 1; update the Fix06 cases
- [x] `npm run check`, `npm run check:visual` (no baseline change)
- [x] Manual 3 recorded in `docs/PENDING.md`
- [x] `CONTEXT.md`, `docs/utils/VERSION.md`
- [x] Notion
- [x] Move this doc to `docs/done/`

## 8. Out of scope

- Walls in the ray. Walls still occlude only through the front-wall stubs and the cut. A tall
  wall in front of Niko that no floor selects is not handled here.
- Structures whose every floor is above `viewerH + 4`. They keep the Fix06 fallback, and the
  silhouette shows.
- Extras hidden under floors, and terrain (hills) cutaways.

---

## TL;DR

The occlusion probes traced the ray through Niko's feet at all three heights, so a slab over his
head went unseen. When a building was selected, its stair steps lifted the cut above the
first-floor slab. The probes now follow his body (3 heights × 3 columns). A covering structure is
cut at `viewerH + 4`, like the building he stands in. Selection runs every frame from his real
position against the uncut world, and a structure is released one tile late. A lab-building sweep
test pins "no floor above 2 m ever covers him". `web.world` and `web.game`; no server or baseline
change.
