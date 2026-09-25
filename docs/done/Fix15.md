# Fix15: Front-wall stubs stop at walls

[Bugfix] [Rendering] [Review]

After Fix14 the user walked up to the lab building's east wall from outside. A one-tile section of
the **south** wall, on the other side of the corner, dropped to a stub, and the room's floor
showed through the notch (GUI shot, 24/09/2026). The stub rule picks every wall in a screen window
in front of Niko's tile. It never asks whether that wall is on his side of the walls around him.
This doc adds that check and nothing else. The window, the stub height and the redraw logic stay
as they are.

Reference: `docs/utils/VISION.md` [Sec. 5] "Buildings" ("Walls on his floor that stand in front
of him are cut down to a stub", unchanged); `docs/done/Fix14.md` (occlusion, lands first);
`web/src/game/wallPainter.ts` `isFrontWall`.

## 0. Findings (measured 24/09/2026 on the Fix14 working tree)

| # | Where | Finding |
|---|---|---|
| F1 | `wallPainter.ts` `isFrontWall` | A wall is stubbed when it is south of Niko's tile (`n`) or east of it (`w`), its midpoint is 0..8 ahead in depth (`x + y`) and within ±3 in screen column (`x − y`), and its base is in his band. Nothing checks what lies between him and the wall |
| F2 | The user's shot | Niko is at about `(26.2, 50.5, 2)`, tile `(26, 50)`, outside the east wall (x = 26). South-wall tile `(25, 52)` has depth 0.5 and column offset 2.5, so it is stubbed. Tile `(24, 52)` is at 3.5 and is not. That is exactly the one-tile notch. The east wall at `(26, 50..51)` stands between him and that section |
| F3 | The same rule, first shot of Fix14 | Standing outside the north wall stubbed south and east walls across the whole building (depth ≤ 8), not only the north wall in front of him |
| F4 | Tests | No test covers `isFrontWall`. `wall-thickness.test.ts` only checks the joints of a wall already stubbed |

## 1. Decisions

| Topic | Decision | Why |
|---|---|---|
| Line of sight | A wall that passes F1 is stubbed only if the floor-plan segment from Niko's tile centre `(nx + 0.5, ny + 0.5)` to the wall's midpoint crosses **no other shown wall** in his band | "In front of him" means on his side. A wall behind another wall is already hidden by that wall's stub, or it does not cover him |
| Shown wall | Its material is non-zero, it is not a doorway, its base is in Niko's band (`base < viewerH + 4 && base + 6 > viewerH`, the F1 band test) and it is on any level. Window walls block | That is exactly what is drawn at his height |
| Corner hits | When the segment passes exactly through a grid corner, it is blocked if either wall edge on the crossed line at that corner is shown | Conservative. A room corner never leaks the next wall |
| Origin | The tile centre, not the rendered position | The result then depends only on the tile and `viewerH`, as today. The existing `tileChunkKeys` redraw stays sufficient, with no per-frame redraw |
| Window | `CUT_DEPTH = 8` and `CUT_WIDTH = 3` unchanged | Inside a room with no interior walls, the result is identical to today. That is pinned by a test |
| Home | The rule moves to `web/src/world/cutaway.ts` as a pure `isFrontWall(store, edge, x, y, base, nikoTile, viewerH)` | Next to `cutoffH`, testable without Phaser. `WallPainter` delegates to it |

## 2. Changes

**web.world** (`web/src/world/cutaway.ts`)
- `isFrontWall(store, edge, x, y, base, nikoTile: {x, y}, viewerH): boolean`: the F1 test moved
  unchanged, then `wallInTheWay(store, from, to, viewerH, band)`. It walks the segment:
  - every integer `X` strictly between the two x values is a `w` edge of tile
    `(X, floor(y at X))`;
  - every integer `Y` strictly between the two y values is an `n` edge of tile
    `(floor(x at Y), Y)`.
  The target lies on its own line at the segment's end, so it is never crossed. The tile centre
  is never on a line. Corners follow [Sec. 1]. A shown wall is read with `store.levelsAt` and
  `store.levelCell`, with `wall_n`/`wall_w`, `edge_flags` and `wallBaseH` as `chunkRenderer`
  uses them. The segment is at most about 9 tiles long, so there are at most about 18 edge
  lookups per candidate wall.
- `CUT_DEPTH`, `CUT_WIDTH` and `MAX_WALL_H` are re-exported from `cutaway.ts`, the one source.
  `wallPainter.ts` imports them.

**web.game** (`web/src/game/wallPainter.ts`)
- `WallPainter.isFrontWall` becomes a thin call to the `cutaway.ts` rule, with
  `view.nikoTile` parsed once. Its callers (`chunkRenderer.ts` lines 135/139, `drawnWallTop`) do
  not change. `MapScene`, `dirty.ts` and the masks do not change.

No `server.*`, schema, occlusion or tracker change.

## 3. What must not break

- Inside a room, the stub window is the same set of walls as before, including the wall Niko
  faces and the stubs next to it. A front wall near Niko is still a one-unit stub with its strip
  on the cut (Fix13).
- Standing outside a wall that faces him, that wall's section in the window is still stubbed.
- Stubs change only when the tile or `viewerH` changes, so there is no new redraw path.
- `building-x1/x2`: at the spawn `(21.5, 41.5, 2)` the stubs are north-wall tiles 23 and 24,
  both with a clear line, so the images are identical. `spawn-x1/x2/x4` (seed-7 city) may lose
  stubs only on walls that stand behind another wall. If a shot changes, confirm that is the only
  reason before re-shooting and asking the user.
- Fix14's occlusion and silhouette, picking and Extras are untouched.

## 4. Acceptance

### Automated

1. `web/tests/front-walls.test.ts` (new). It uses the Fix14 lab building, moved from
   `occlusion.test.ts` into `web/tests/fixtures/labBuilding.ts` and shared by both files.
   - **Reported spot:** Niko's tile `(26, 50)`, `h = 2`: south-wall tile `(25, 52)` is not a front
     wall. The old rule said it was; the test states that as a comment, not an assertion.
   - **Room unchanged:** for every tile inside the footprint at `h = 2`, and every wall in the
     building, the new result equals the F1-only result. This is a copy of the old predicate, kept
     in the test.
   - **Outside north:** tile `(21, 44)`: the north-wall tiles in the window are front walls. No
     south-wall, east-wall or west-wall tile is (F3).
   - **Doorway:** outside the west doorway, tile `(16, 48)`: the west wall's tiles in the window are
     front walls. A south-wall tile such as `(19, 52)` is not: the line crosses the west wall at
     `(18, 50)`. A synthetic 4 × 4 room with a doorway checks that a line through the doorway edge
     is open.
   - **Corner:** a segment through a room corner is blocked when either edge on the crossed line
     is a wall.
2. `wall-thickness.test.ts`, `tile-masks.test.ts` and `occlusion.test.ts` stay green.
3. `npm run check`, plus `npm run check:visual`, with baselines unchanged or explained as in
   [Sec. 3].

### Manual

4. Manual (GUI, x1 and x2, the user's lab save): walk along the outside of all four walls,
   around each corner and through the doorway. Pass when no wall section on the far side of a
   wall disappears, the wall in front of Niko still drops to a stub, and inside the building the
   cut looks as before. Manual because it is a taste check in the user's save, and the headless
   shots cannot walk Niko.

## 5. Docs to update in the same change

- `CONTEXT.md`: a pitfall, "A front-wall stub needs a clear floor-plan line from Niko's tile
  centre; the screen window alone cuts walls behind other walls".
- `docs/PENDING.md`: add manual item 4. It is closely related to the Fix14 manual entry and can be
  walked in the same session.
- `docs/utils/VERSION.md`, the Notion Systems Index, the Dev Blog entry and the daily Work Report.

## 6. Modules and versions

| Module | Change | Version |
|---|---|---|
| web.world | `isFrontWall` with line of sight in `cutaway.ts` | v0.0.10 → v0.0.11 |
| web.game | `WallPainter` delegates the rule | v0.1.17 → v0.1.18 |

Overall project version: assigned at release by `docs/utils/COMMITS.md` [Sec. 2]. It lands after
the Fix14 release commit. It is web-only, so it does not conflict with Dev-023's server
refactor. Dev-023's "clean tree" precondition means Fix15 is committed before Dev-023 starts, or
waits until it ends.

## 7. Todo

- [x] `cutaway.ts`: `isFrontWall`, `wallInTheWay`, shared cut constants
- [x] `wallPainter.ts`: delegate, import the constants
- [x] `fixtures/labBuilding.ts` shared by `occlusion.test.ts`; `front-walls.test.ts` per [Sec. 4]
- [x] `npm run check`; visual specs pass on isolated ports because the user's GUI server holds the
      standard ports
- [x] Manual 4 recorded in `docs/PENDING.md`
- [x] `CONTEXT.md`, `docs/utils/VERSION.md`
- [x] Notion
- [x] Move this doc to `docs/done/`

## 8. Out of scope

- The size of the stub window (`CUT_DEPTH`, `CUT_WIDTH`), and stubbing by exact body cover.
- Walls in the occlusion rays (Fix14 [Sec. 8]).
- Interior walls that face Niko inside one room: they already have a clear line and stay
  stubbed, as today.

---

## TL;DR

The front-wall stub cut every wall in a wide screen window ahead of Niko's tile, even a wall behind
another wall. Walking up to the lab's east wall from outside cut a section of the south wall on the
far side of the corner. A wall is now stubbed only if the floor-plan line from Niko's tile centre
reaches it without crossing another shown wall. The window, the stub and the redraws are
unchanged, and inside a room the result is identical. `web.world` and `web.game` only.
