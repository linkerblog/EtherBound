# Fix13: Dev-020 review — anchored wall faces and a 1/4 m wall body

[Bugfix] [Rendering] [World] [Review]

Dev-020 shipped in `v2.1.0` and walls still read as thin sheets, now with loose bits: a light line
painted across the brick one unit below the wall's top, dark slivers standing in the air at wall
ends, a grey band under every front wall and wall stubs poking out of the lab building's corners.
The cause is not the thickness value. The wall faces are drawn one run too high, so Dev-020's
correctly placed top strip ends up below the brick. This doc anchors the faces, then raises the
body to 1/4 m, which Dev-020 [Sec. 1] already named as the fallback. It also fixes the lab
building's corners and adds the visual shot whose absence let this ship.

Reference: `docs/done/Dev-020.md` [Sec. 1–2] (geometry, still valid); `docs/utils/VISION.md`
[Sec. 3] (1 m tile = 64×32 px, 0.5 m = 16 px); `CONTEXT.md` "Walls stay thin planes".

## 0. Findings (measured 24/09/2026 at `272d711`)

The user's GUI shot, plus headless renders of the lab `structure` bay at x1/x2 on a throwaway
database. The fix below was tried in a throwaway worktree, rendered and then discarded.

| # | Where | Finding |
|---|---|---|
| F1 | `tileMasks.ts` `wallMask`, `shearWallCell` | The plane rises from the edge line: rows `topRow − 16u .. topRow − 1`. Drawn at height `H`, it covers `[H, H + u]` |
| F2 | `wallPainter.ts` `drawWallUnits`, `drawWallRun` | Both draw at the run's **top** (`unitTop`, `current + units`), like the ground faces (`faceMask` hangs down). So brick covers `[base + 1, top + 1]` and a flat run of 4 + 2 covers `[base + 4, base + 8]` |
| F3 | `wallEndMask` | Same convention as F1 and the same call path, so end faces float up to 4 units: the slivers in the air |
| F4 | `wallTopMask`, `wallPostMask` | Placed at the true top. The strip is right; the brick above it is the F2 overshoot (measured: exactly 1 unit, 20 px at the user's 1.25 scale) |
| F5 | Front walls | The empty unit at the foot shows the room floor behind the wall: the grey band |
| F6 | Window glass | Drawn by `drawWallRun`, so it sits at `base + 4 .. base + 6`, not `base + 2 .. base + 4` |
| F7 | `wallTopMask` | Vertical 4-px columns on `x = 32..63`, not the sheared parallelogram of Dev-020 [Sec. 2]: its ends are cut square. Invisible at 4 px, a notch at 8 px |
| F8 | `wallPostMask` | Hard-coded 4-px rhombus rows; it cannot follow `WALL_T_PX` |
| F9 | `lab.py` `_structure` | Walls loop `range(y0, y1 + 1)` and `range(x0, x1 + 1)` at `x0` and `y0`, while the floor covers `x0 + 1 .. x1 − 1`. Result: a floorless strip inside the north and west walls and a one-tile stub past the NE, SW and SE corners |
| F10 | Tests | `tile-masks.test.ts` checks shapes, never where a mask lands; no visual spec frames a wall (Dev-020 [Sec. 5] item 5). F1–F6 predate Dev-020 (`v2.0.0` has the same code), hidden while walls had only a 1-px `line` |

## 1. Decisions

| Topic | Decision | Why |
|---|---|---|
| Anchor | Wall-face masks **hang down** from their draw height, like `faceMask`: plane and brick cells cover rows `topRow .. topRow + 16u − 1`, end faces shift down 16u the same way | Call sites keep passing the run top, so `blitterFor(h)` still picks the layer by the top, as for ground faces. Only masks move; `drawWallRun` and `drawWallUnits` are untouched |
| Thickness | `WALL_T` = 1/4 m: `WALL_T_PX = 8` at x1 (16 at x2, 32 at x4) | The user asked for thicker walls; 25 cm is a real brick wall. It stays under Niko's 0.3 m clearance, so no body standing behind a wall sinks into its drawn top |
| Strip | Rasterized from the Dev-020 parallelogram [Sec. 2] | Square ends leave an 8-px notch at every run end (F7) |
| Post | Generated from `WALL_T_PX` | F8 |
| Direction, joints, tints | Unchanged: outward growth, `wallJoints`, flat tints ×1.12 / 0.66 / 0.82 | The geometry was right; only the placement was wrong |
| Lab building | Walls enclose exactly the floor; the building moves next to the bay's north path so the spawn frames it | F9, and it gives F10 a shot without a camera path |
| Visual shot | New paused spec of the lab building at x1 and x2 | The shot that would have caught F1–F6 |

## 2. Geometry at x1

`T = WALL_T_PX = 8`. The mask frame is the tile's local frame (top vertex at `(32, 0)`, right
vertex `(64, 16)`, left vertex `(0, 16)`); a pixel is in a shape when its centre `(px + 0.5,
py + 0.5)` is. `topRow(x)` is the existing helper.

- **Plane** `wallMask(edge, u)` and `shearWallCell`: columns as today, rows `topRow(x) ..
  topRow(x) + 16u − 1`. In `shearWallCell`, `minY = min(topRow)`, `maxY = max(topRow) + 15`,
  `localY = topRow(x) + r − minY`. The pixel count is unchanged (`512 × u`).
- **North strip** `wallTopMask("n")`: with `dx = px + 0.5 − 32`, `dy = py + 0.5`,
  `a = (dx / 2 + dy) / 32`, `b = (dx / 2 − dy) / T`, keep `0 ≤ a < 1` and `0 ≤ b ≤ 1`. That is the
  parallelogram `(32, 0)`, `(64, 16)`, `(64 + T, 16 − T/2)`, `(32 + T, −T/2)`: 256 px.
  **West strip**: the mirror, pixel `(63 − px, py)`.
- **End face** `wallEndMask("e", u)`: columns `64 .. 64 + T − 1`; column `c` spans rows
  `round(16 − (c + 0.5) / 2) .. + 16u − 1`, i.e. it hangs from the slanted top edge
  `(64, 16) → (64 + T, 16 − T/2)`: `128 × u` px. **`"s"`**: the mirror, `(63 − px, py)`.
- **Post** `wallPostMask()`: centre `(32, −T/2)`, keep `|dx| / T + |py + 0.5 + T/2| / (T/2) ≤ 1`
  with `dx = px + 0.5 − 32`: the rhombus `(32, 0)`, `(32 − T, −T/2)`, `(32, −T)`, `(32 + T, −T/2)`,
  64 px, touching both strips without overlapping them.

Bounding boxes barely change (the strip goes from 32×19 to 38×20 px, the post from 6×4 to
14×8); every mask stays within the atlas mask row.

## 3. Changes

**web.game** (`tileMasks.ts` only for rendering)
- `WALL_T_PX = 8` and its comment (1/4 m). `wallMask`, `shearWallCell`, `wallEndMask` hang down;
  `wallTopMask` and `wallPostMask` follow [Sec. 2]. Doc comments say "hangs from its draw height".
- `wallPainter.ts`, `wallJoints.ts`, `terrainAtlas.ts`, `chunkRenderer.ts`: no change. Verify that
  `terrainAtlas` still packs the taller strips inside the mask row.

**server.world** (`world/gen/lab.py`)
- `_structure`: origin `x0, y0 = 17, 45` (was 14, 54); `x1, y1 = x0 + 9, y0 + 7` as today, so the
  floor stays `x0 + 1 .. x1 − 1` × `y0 + 1 .. y1 − 1` (8 × 6 tiles). Walls: `west` at `x0 + 1`
  and `x1` for `y` in `y0 + 1 .. y1 − 1`; `north` at `y0 + 1` and `y1` for `x` in
  `x0 + 1 .. x1 − 1`. The doorway moves to `(x0 + 1, (y0 + y1) // 2)` and the windows to row
  `y0 + 1`. Stairs, stairwells and the roof keep their offsets from `x0, y0`.
- `GEN_VERSION` 1 → 2. A running lab save regenerates its chunks on the next start
  (`engine/world.py`, `meta.gen_version < spec.version`); the lab is a test map, so that is intended.

**Tests**: see [Sec. 5]. No `web.world`, `web.net`, `web.ui`, `server.engine` or schema change.

## 4. What must not break

- Physics, picking, occlusion and the cutaway still treat a wall as its edge (Dev-020 [Sec. 4]).
- Ground faces, floors, objects and the spawn baselines: no wall is in frame, so
  `spawn-x1/x2/x4` stay identical. If they change, find out why before re-shooting.
- Brick courses and the `cap` cell on the real top unit read as before, one unit lower.
- A front wall near Niko is still a one-unit stub with a strip on its cut; doorways stay open.
- The lab stays deterministic, standable at every spawn bay and reachable from the default spawn.

## 5. Acceptance

### Automated

1. `web/tests/tile-masks.test.ts`, rewritten for the wall masks:
   - `wallMask(edge, u)` and every `shearWallCell` pixel lie at `topRow(x) .. topRow(x) + 16u − 1`
     for their column: the plane hangs from its draw height.
   - **Placement**: at draw heights `base + 1 .. base + 6`, the unit cells tile the band between
     the edge line at `base` and at `base + 6` with no gap or overlap; the strip at `base + 6`
     sits directly above the top cell; an end face drawn at `h` spans the same heights as a plane
     unit drawn at `h`. This is the check that would have caught F1–F6.
   - Strips: 256 px each, mirror images, parallelogram corners as in [Sec. 2].
   - `wallEndMask(face, u)`: `T` columns, `128 × u` px.
   - `wallPostMask()`: 64 px, touches both strips, overlaps neither nor the diamond.
2. `web/tests/wall-thickness.test.ts` unchanged and green (joints do not depend on `T`).
3. `server/tests/test_worldgen_lab.py`: the structure's walls enclose exactly its floor (no wall
   on a tile edge that borders no floor tile), and every existing lab test stays green.
4. `web/tests/visual/building.spec.ts`: new game `generator: "lab"`, `spawn_bay: "structure"`,
   paused; shots `building-x1.png` and `building-x2.png` with the HUD hidden as in
   `spawn.spec.ts`. The user approves both baselines.
5. `npm run check` and `npm run check:visual`.

### Manual

6. Manual (GUI, x1/x2/x4) in the user's own lab save, at the spot of their screenshot: walls sit
   on the floor, the top reads as a solid 25 cm slab, no grey band under front walls, no slivers
   in the air, windows at 1–2 m, corners closed. Reason: taste, and the user's zoom and scaling.

## 6. Docs to update in the same change

- `CONTEXT.md` "Walls stay thin planes": `WALL_T` is 1/4 m; add "wall-face masks hang from their
  draw height (the run top), like ground faces".
- `docs/PENDING.md`: replace the Dev-020 manual entry with item 6 and the baseline approval.
- `docs/Dev-021.md`: its lab bay bumps `GEN_VERSION` to 3; its `WALL_T` references mean 1/4 m.
- `docs/utils/VERSION.md`, the Notion Systems Index, the Dev Blog entry and the daily Work Report.

## 7. Modules and versions

| Module | Change | Version |
|---|---|---|
| web.game | Hanging wall masks, `WALL_T_PX = 8`, parallelogram strip, generated post | v0.1.15 → v0.1.16 |
| server.world | Lab building walls and position, `GEN_VERSION` 2 | v0.0.8 → v0.0.9 |

Overall project version: assigned at release by `docs/utils/COMMITS.md` [Sec. 2]. Lands before
Dev-021, which draws midlines and diagonals with these masks.

## 8. Todo

- [x] `tileMasks.ts`: hanging `wallMask`, `shearWallCell`, `wallEndMask`; `WALL_T_PX = 8`;
      parallelogram `wallTopMask`; generated `wallPostMask`
- [x] `tile-masks.test.ts`: shape and placement tests from [Sec. 5] item 1
- [x] `lab.py`: structure walls, origin, `GEN_VERSION`; `test_worldgen_lab.py` enclosure test
- [x] `building.spec.ts` and its x1/x2 baselines
- [x] `npm run check`, isolated `check:visual` suite (spawn baselines unchanged)
- [ ] Manual 6 and the baseline approval remain open in `docs/PENDING.md`
- [x] `CONTEXT.md`, `Dev-021.md`, `docs/utils/VERSION.md`, Notion
- [x] Move this doc to `docs/done/`

## 9. Out of scope

- Physical thickness (collision, picking, occlusion by the body).
- Textured (brick) tops and end faces; they stay flat tints.
- The post when north and west walls have different drawn tops (Dev-020 [Sec. 9]).
- The seeded city's buildings: not measured here; they use the same masks and get the fix for free.

---

## TL;DR

Dev-020's thick walls looked broken because the wall faces were drawn one run too high: the brick
rose a unit above the correct top strip, end faces floated and front walls showed a gap at their
foot. The wall masks now hang from their draw height like ground faces, the body grows from 1/8 m
to 1/4 m with a true parallelogram strip and a generated post, the lab building's corner stubs go
away, and a new lab visual shot guards the result. `web.game` and `server.world` (lab only).
