# Fix17: Radial menu grouped by verb, targets by direction

[Bugfix] [UI] [Accessibility] [Review]

Dev-017's `radius=1` scan unions the entries of up to nine tiles into one flat list, and
`radialSlots` lays that list on rings of eight. In the user's GUI shot of 25/09/2026 (Niko next
to a chest, on grass between two roads) the radial showed about 28 slots on 4 rings, covering the
map around him. Ten of them read just `INSPECT` and eight read just `DIG`, with no way to tell
which tile each one acts on. This doc makes the radial a two-level menu: first the verb, then its
targets, each placed in the screen direction of its tile. The server still builds every entry;
the client groups and places them.

Reference: `docs/done/Dev-017.md` (radial menu, `radius=1` scan); `docs/utils/STYLEGUIDE.md`
[Sec. 8] (context menu) and [Sec. 16] (keyboard paths); `web/src/game/iso.ts` (`toScreen`);
`CONTEXT.md` (Menus contract).

## 0. Findings (measured 25/09/2026 on the Dev-017 working tree)

| # | Where | Finding |
|---|---|---|
| F1 | `build_menu(radius=1)` | Entries come out in catalog order, then candidate order, over up to 9 tiles. A tile's surface gets `inspect` and `dig` of its own, so an open 3×3 alone gives 9 `INSPECT` and up to 9 `DIG` |
| F2 | `MenuEntry` | It does not say which tile an entry belongs to. `subject` is empty for tile targets, and `ObjectTarget` carries only an id, so the client cannot work the tile out |
| F3 | `radialSlots` | 28 entries means 4 rings (104 to 284 px). Slots are up to 132 px wide, so the outer rings overlap each other and the objects around Niko |
| F4 | The shot | 12 distinct ops (`hit`, `dig`, `break`, `drag`, `inspect`, `climb`, `wait`, `close`, `open`, `take`, `pull`, `push`); 10 of them have exactly one entry |

## 1. Decisions

| Topic | Decision | Why |
|---|---|---|
| Level 1 | One slot per distinct `op`, in first-appearance order. That is catalog order, so a verb keeps its place from one open to the next. Laid out by the existing `radialSlots` (8 per ring) | 12 short labels on 2 rings instead of 28 on 4; grouping is presentation, not reordering |
| Single entry | A verb with exactly one entry shows it whole (`OPEN` / `CHEST`) and picking it runs that entry, or shakes the hub with its reason | `WAIT` has no target to choose; `OPEN CHEST` is the only open. No empty second step |
| Several entries | Picking the verb opens level 2 with its entries | "Every entry, available or not" still holds: each one is at most two picks away |
| Verb dimming | A level-1 slot is `off` only when **every** entry under it is unavailable. An `off` verb with several entries still opens level 2, so each reason is readable | Same rule as today ("dim, never hidden"), applied to the group |
| Verb markers | `illegal`/`ether`/`violent` come from the union of the group's tags | Tags are per op spec today, but the union stays right if that changes |
| Level 2 layout | Each entry goes to its tile's **screen octant**, the direction `toScreen(dx, dy, 0)` points from Niko: `(-1,-1)` up, `(0,-1)` up-right, `(1,-1)` right, `(1,0)` down-right, `(1,1)` down, `(0,1)` down-left, `(-1,1)` left, `(-1,0)` up-left. Several entries on one tile form a short column at that octant, growing away from the hub (up for the upper three, down for the lower three, centred for left/right) | "Inspect up and to the right" becomes muscle memory; the column keeps a tile's options together |
| Own tile | Entries on Niko's own tile (`dx = dy = 0`: self, held, worn, his surface) are rows inside the hub, under the verb header. The ring radius grows with the hub: `max(RADIAL_BASE_RADIUS, hubHalfHeight + 56)` | The hub is drawn on Niko, so "here" lives there; no octant is left free for it |
| Column anchors | Up and down columns start at `(0, ∓R)`, with `R` the ring radius above. The other six sit on two side lanes at `x = ±X`, `X = max(RADIAL_SLOT_W + RADIAL_COLUMN_GAP, RADIAL_HUB_W / 2 + RADIAL_COLUMN_GAP + RADIAL_SLOT_W / 2)` (156 px): left/right centred on `y = 0`, and the diagonals start at `y = ∓max(R / 2, sideHalf + RADIAL_COLUMN_GAP + RADIAL_SLOT_H / 2)`, where `sideHalf` is half the height of that lane's left/right column | Measured while implementing: on a plain 104 px ring, the up and up-right slots (132 × 34) overlap by 58 × 4 px. The lanes keep each direction's sign and read like the iso diamond (up-right lands at about -18°, the tile at -27°), and no two columns can share space |
| Level 2 labels | `entry.subject`, or the tile's place label when there is none (`GRASS`, `ROAD`). The verb is the hub header | The verb is already chosen; the label says *what* |
| Map marker | While a level-2 entry, or a single-entry verb, has focus, its tile gets an outline on the map | The player sees which tile the direction means, even when the camera crops the ring |
| Keyboard, level 1 | As now: ←/↑ and →/↓ cycle, `1`–`9` focus by position, Enter/Space pick, `V`/`Esc` close | Unchanged from Dev-017 |
| Keyboard, level 2 | ←/↑ and →/↓ cycle all entries (hub rows first, then octants clockwise from up); `1`–`9` are the numpad directions (`8` up, `9` up-right, `6` right, `3` down-right, `2` down, `1` down-left, `4` left, `7` up-left, `5` the hub rows); pressing the same digit again steps down that column. Enter/Space pick. `Esc`/`Backspace` go back to level 1; `V` closes | The numpad is the 3×3 around Niko on screen; STYLEGUIDE [Sec. 16] |
| Pointer | Hover focuses, click picks; clicking the hub header in level 2 goes back, in level 1 it closes | Same as today, plus a way back |
| Right-click | Unchanged: `radius=0`, flat `ContextMenu` list | Exact-tile behaviour, one tile, few entries |

## 2. Changes

**server.engine** (`engine/actions.py`, `engine/menu.py`, `engine/payloads.py`)
- `MenuEntry` gains `tile_dx: int = 0` and `tile_dy: int = 0`: the offset from the origin tile of
  the tile whose candidate built the entry. Self, held and worn objects, and contents of an open
  container on the own tile, are `0, 0`; contents of a container one tile away carry that tile's
  offset.
- `build_menu` keeps each candidate paired with its `(dx, dy)` from the scan loop and stamps it on
  every entry that candidate builds. Order and content of `entries` do not change.
- `MenuPayload` gains `places: tuple[MenuPlace, ...]`, one per scanned offset (walled-off tiles
  are not scanned, so they have none): `MenuPlace(dx, dy, h, label)`, where `label` is the
  surface material name (`Grass`) and `h` its height, both from `_standing_surface` as the origin
  `target` label already does. With `radius=0` it holds only `(0, 0)`.

**server.app** (`routes/api.py`): `MenuResponse` gains `places: list[MenuPlace]`.

**web.net**: regenerate `schema.d.ts`; export `MenuPlace` from `protocol.ts`.

**web.ui** (`radialMenu.ts`, `ContextMenus.tsx`, `App.tsx`, `styles.css`)
- `radialMenu.ts` (pure):
  - `groupByVerb(entries): VerbGroup[]` gives `{ op, label, entries, available, tags }` in
    first-appearance order.
  - `octantOf(dx, dy): number | null` maps an offset to its octant 0–7 (0 = up, clockwise) as in
    [Sec. 1], and `(0, 0)` to `null`.
  - `targetSlots(entries, hubHalfHeight): TargetSlot[]` gives hub rows plus octant columns, with
    `x`, `y` and `digit`, using `RADIAL_SLOT_W = 132`, `RADIAL_SLOT_H = 34`,
    `RADIAL_COLUMN_GAP = 4` and `RADIAL_HUB_W = 172` (the hub's CSS width), anchored as in the
    Column anchors row of [Sec. 1].
  - `digitFocus(slots, digit, current)` gives the slot a numpad digit focuses: the first of its
    column, or the next one when `current` is already in it.
  - `radialSlots`, `firstAvailable` and `stepFocus` stay, and level 1 uses them on the groups.
- `RadialMenu`: `level` state (`null` = verbs, or the open `VerbGroup`), with focus reset on each
  level change. Level 1 renders groups through `radialSlots`. Level 2 renders the header, the hub
  rows and the octant columns through `targetSlots`. The lines run from the hub to the first slot
  of each column only. Keys as in [Sec. 1]. `onFocusTile(tile | null)` reports the focused tile.
- `RadialState` gains `places` and the origin `{ x, y, z }`. `openRadial()` stores them from the
  response.
- `styles.css`: `.radial-hub .radial-row` (same states as `.radial-slot`), `.radial-header`, and a
  level-2 variant of `.radial-slot` with no subject line.

**web.game** (`MapScene.ts`): `markTile(tile: { x: number; y: number; h: number } | null)` draws a
1 px cyan diamond outline on that tile's top at `h`, above the ground and below actors, and clears
it with `null`. `App.tsx` wires `onFocusTile` to it and clears it on close.

No change to `ContextMenu`, `submit`, the op handlers, or `in_close_reach`.

## 3. What must not break

- The client never adds, drops or rewrites an entry. Every entry of the response is reachable by
  pointer and by keyboard, in at most two picks, and is sent back with `entry.action` unchanged.
- Unavailable entries stay visible and focusable, and show their reason.
- The right-click menu, `radius=0`, and its entries stay as they are. The new fields default to
  `0` and `(0, 0)`.
- `/api/menu` stays a lock-free read. Self, held and worn entries still appear exactly once.
- `V` typed in the input line does nothing. A late response is dropped by its token. The follow
  interval still re-anchors the centre.
- The map marker is gone after close, pick, and `new_game`.

## 4. Acceptance

### Automated

1. `server/tests/test_physics.py`: `test_menu_entries_carry_their_tile_offset`. With `radius=1`,
   an object east of Niko gives entries with `(tile_dx, tile_dy) == (1, 0)`, and self or held
   entries give `(0, 0)`. `places` lists exactly the scanned offsets, so a walled-off neighbour
   has no place. With `radius=0`, every entry is `(0, 0)` and `places` has one item.
2. `web/tests/radial-menu.test.ts`:
   - `groupByVerb` keeps first-appearance order and loses no entry. A group is `available` iff any
     of its entries is, and its tags are the union.
   - `octantOf` matches the table of [Sec. 1] for all 8 offsets and agrees with the sign of
     `toScreen(dx, dy, 0)`. `(0, 0)` is `null`.
   - **Shot fixture:** the 28 entries of [Sec. 0] (built in the test with offsets). Level 1 has 12
     groups on 2 rings. For each group, `targetSlots` places every entry exactly once, and no two
     slot rectangles (`RADIAL_SLOT_W × RADIAL_SLOT_H`) overlap each other or the hub.
   - Numpad digits reach every octant column, and repeating a digit steps down its column.
   - The existing `radialSlots`, `firstAvailable` and `stepFocus` cases stay green.
3. `npm run check:web` (schema export, `schema.d.ts` diff, tests, build) and `npm run check`.
   `npm run check:visual`: baselines unchanged, because the radial is closed in every shot.

### Manual

4. Manual (GUI, x1 and x4, the chest scene of [Sec. 0]): `V` shows about 12 verbs and nothing
   overlaps. `OPEN` runs at once. `INSPECT` opens its targets in their map directions, with Niko
   and his own tile in the hub, and the focused tile is outlined on the map. Numpad `9` reaches the
   up-right tile, `5` the hub. `Esc` goes back, and `V` closes. A dim `DIG` column shows each reason.
   Manual because it is about layout legibility and feel.
5. Manual: keyboard-only and Narrator reach every entry. Manual because focus behaviour needs a
   browser.

## 5. Docs to update in the same change

- `docs/Dev-017.md`: the Layout, Centre and Keyboard rows of [Sec. 1] and manual 4 point to this
  doc as superseded. Its remaining todo (STYLEGUIDE, Notion) is written against the Fix17 layout.
- `docs/utils/STYLEGUIDE.md`, "Radial menu (`V`)": the two levels, octants, hub rows, digits, and
  the new CSS rules.
- `CONTEXT.md`, Menus contract: `tile_dx`/`tile_dy` and `places`. The radial groups entries by op
  and places them by tile; it still never adds, removes or reorders one within a group.
- `docs/PENDING.md`: replace the Dev-017 radial entry with manual 4 and 5 of this doc if they are
  not run.
- `docs/utils/VERSION.md`, the Notion Systems Index, the Dev Blog entry and the daily Work Report.

`docs/utils/VISION.md` needs no change: the radial still reaches every generated action with `V`
and no aiming.

## 6. Modules and versions

| Module | Change | Version |
|---|---|---|
| server.engine | `MenuEntry.tile_dx/tile_dy`, `MenuPlace`, `MenuPayload.places` | v0.0.14 → v0.0.15 |
| server.app | `MenuResponse.places` | v0.0.10 → v0.0.11 |
| web.net | `schema.d.ts`, `MenuPlace` export | v0.0.14 → v0.0.15 |
| web.ui | Grouping, octant layout, two levels, keys, styles | v0.0.12 → v0.0.13 |
| web.game | `markTile` | v0.1.18 → v0.1.19 |

Versions are read from the Dev-017 working tree of `docs/utils/VERSION.md`. Overall project
version: assigned at release by `docs/utils/COMMITS.md` [Sec. 2]. It lands after Dev-017's release
commit. If Dev-017 is still uncommitted then, both ship in one release and the `schema.d.ts` diff
gate clears with it. They shipped together in `v2.2.0`: one commit bumps each module once
(`check-versions` allows +0.0.1 per commit), so the versions above include Dev-017's change.

## 7. Todo

- [x] `engine`: `tile_dx`/`tile_dy` on `MenuEntry`, offset-tagged candidates in `build_menu`,
      `MenuPlace` and `MenuPayload.places`
- [x] `routes/api.py`: `MenuResponse.places`; regenerate `schema.d.ts`; `MenuPlace` in `protocol.ts`
- [x] `radialMenu.ts`: `groupByVerb`, `octantOf`, `targetSlots`, `digitFocus`, `targetName`,
      slot-size constants
- [x] `RadialMenu`: levels, hub rows, octant columns, keys, `onFocusTile`; `RadialState` and
      `openRadial()`
- [x] `MapScene.markTile` wired from `App.tsx` (also cleared on snapshot)
- [x] `styles.css`: hub rows, header, level-2 slot
- [x] Tests of [Sec. 4] items 1–2. Server: ruff, pyright, pytest 150 passed. Web: `tsc`, 112
      tests, `vite build`, `npm run check:visual` 3 passed (baselines unchanged),
      `check:versions`/`check:docs`/`check:sizes` ok. The `schema.d.ts` `git diff --exit-code`
      gate of `check:web` clears only with the release commit, as for Dev-017
- [x] Manual 4–5: run and passed by the user on 25/09/2026; removed from `docs/PENDING.md`
- [x] Docs of [Sec. 5]: `Dev-017.md`, `STYLEGUIDE.md`, `CONTEXT.md`, `PENDING.md`, `VERSION.md`.
      Notion (Systems Index, Dev Blog, Work Report) goes with the release commit
- [x] Move this doc to `docs/done/`

## 8. Out of scope

- Refreshing the entries while Niko walks with the radial open. Only the centre follows him, as in
  Dev-017, so the offsets describe where he stood when it opened.
- Reordering verbs by use or frequency. Catalog order is the stable order.
- Grouping in the right-click list menu.

---

## TL;DR

Dev-017's 3×3 scan made the `V` radial show about 28 slots on 4 overlapping rings, with ten
unlabelled `INSPECT` and eight `DIG`. The radial now shows one slot per verb (12 in that scene,
2 rings). A verb with one entry runs directly; otherwise it opens its targets, each placed in the
screen direction of its tile, with Niko's own tile in the hub and the focused tile outlined on the
map. The numpad maps to the 3×3. The server only tags each entry with its tile offset and lists a
label per scanned tile; the right-click menu is unchanged.
