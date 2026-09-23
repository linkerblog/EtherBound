# Fix01: Dev-002 review

[Bugfix] [World] [Engine] [Networking] [Client]

Review of the uncommitted Dev-002 implementation (`docs/done/Dev-002.md`). The checks pass and the
data model is sound, but movement freezes on any slope, slope and material cost have no effect,
the material registry is not append-only in practice, `new_game` never reaches the client, and the
default world (`seed = 0`) has no route into the building. Several acceptance items in Dev-002
[Sec. 9] are ticked but do not hold.

Everything below was reproduced against the working tree; the measurements in each item are real,
not estimates. Nothing in the repository was changed by the review.

## 1. Decisions to approve

Three items need an answer before the code changes, because the plan itself is ambiguous or the
generator deviates from it.

| Topic | Question | Proposal |
|---|---|---|
| Cutaway | Dev-002 [Sec. 5.2] says "floor slabs and walls more than 2 m above Niko are hidden" **and** "outside you see roofs". Those contradict each other, and the code follows the first, so from the road you see the basement slab painted over the building | Hide a slab when it is more than 2 m above Niko **and** Niko is under it (there is another slab in that column between him and it). Outdoors nothing is above him, so roofs stay visible |
| Test building | The generator has basement (`h = 6`), ground floor (`h = 12`) and roof (`h = 18`). Dev-002 [Sec. 4.4] asks for ground floor **+ first floor** + roof, and a basement exposed on the downhill side | Add the first floor at `h = 18` and move the roof to `h = 24`, with the stairs running ground → first → roof; give the basement a second flight or an exposed side |
| Body radius | Dev-002 [Sec. 3] asks for the `r = 0.3` body circle and walls as segments 0.1 m thick; the code collides the centre point only, which is a regression from the deleted `TestMap` | Restore the radius: no body centre closer than 0.3 m to a blocked edge |

A decision that changes the model updates `utils/VISION.md` [Sec. 5] first, as always.

## 2. Blocking defects

### B1 — Movement freezes on slopes and stairs
`server/src/etherbound/engine/movement.py` [lines 61-87].

The sub-step that crosses into a tile of a different height advances only `0.6 x` (up) or
`0.85 x` (down) of the sub-step, but takes the new tile's `h` immediately. The body therefore ends
the sub-step still inside the old tile while carrying the new tile's `h`. On the next sub-step
`can_step` filters the source tile's standing surfaces to that exact `h`, finds none, and blocks
every move out of the tile. Only an in-tile move (a sideways key) re-syncs `h`.

Measured with game-sized inputs (0.2 m per input, 20 Hz): **60 of 100 uphill runs and 100 of 100
downhill runs** stop permanently at the first height change. `test_uphill_walking_covers_less_
distance_than_flat` passes *because* of this bug — it travels 0.48 m against 8.5 m on the flat.

Fix: never change `h` without changing tile. Resolve the arrival surface only when the body's
centre actually enters the new tile, and take `h` from that tile at that moment.

### B2 — Slope and material cost have no effect
`movement.py` [lines 70-87] and `web/src/net/prediction.ts` [line 357].

The server scales the sub-step by the cost and then subtracts the already scaled step from
`remaining`, so the total distance is unchanged: sand covers 8.5 m, exactly like grass. The client
has the same cancellation with the multiplier inverted, so it predicts a body that walks *faster*
uphill.

Fix: charge the cost against the budget, not against the displacement — advance `base` and
subtract `base / multiplier` (or the equivalent), on both sides. The cost also has to apply to
every sub-step inside a sloped or costly tile, not only to the crossing sub-step; as written, a
whole metre of mud costs 5 % of one sub-step.

### B3 — Material ids are not append-only
`server/src/etherbound/engine/world.py` [line 79].

The engine builds the registry with `MaterialRegistry.load()` and never passes the ids stored in
the `material` table, although `from_file` accepts `existing_ids`. `_sync_materials` then inserts
missing keys with the id the TOML order happens to give them. Reproduced:

- a new material inserted anywhere but at the end of `materials.toml`: the server fails to start
  with `IntegrityError` on `material.id`;
- the TOML merely reordered: the save decodes with the wrong materials, silently — the road stops
  having a standing surface and Niko falls out of the world.

This is the exact failure Dev-002 [Sec. 2.1] exists to prevent. `test_material_ids_append_to_
existing_mapping` only exercises `from_file` in isolation, so it does not catch it.

Fix: read `material.key → id` from the database, pass it as `existing_ids`, assign the next free
id to genuinely new keys, and persist them. Startup must fail loudly if a key present in the
database is missing from the TOML.

### B4 — `new_game` never reaches the client
`server/src/etherbound/net/ws.py` [lines 105-112] and `web/src/world/ChunkStore.ts` [line 45].

`new_game` regenerates the world but the hub keeps `_known_chunks`, and regenerated chunks start
again at `revision = 0`, so the push loop skips every one of them. Even if they were sent, the
client store rejects a chunk whose revision is not greater than the one it holds. Measured: after
`POST /api/game/new`, walking across a chunk border pushes **0 chunks**. The client keeps rendering
and predicting against the previous world.

Fix: clear the per-connection chunk state on `new_game` (and on any regeneration) and push the
radius again; have the client drop its store when a snapshot announces a new world. A world
generation counter in the snapshot is the cheapest way to express "everything you hold is stale".

### B5 — The default world has no route into the building
`server/src/etherbound/world/gen/testworld.py` [lines 69-80, 147-152].

Flood-filling the standing spots from spawn: the building interior is reachable with seeds 123
(the one the tests use), 1, 2 and 7, but **not with seed 0 — the seed the game actually starts
with** — nor with 3 or 42. The graded approach starts at `x = 127` while the road ends at
`x = 123`, leaving three tiles of raw noise in between; whether they line up is luck.

The same section raises the road by 1.5 m where the terrace crosses it (`gy` 88-90 is applied
after the road is levelled), so the road is cut by a step no body can climb **in every seed**, and
the terrace is a 3-tile ridge rather than a step with a ramp.

Fix: carve the approach from the road itself and grade it tile by tile, exclude the road from the
terrace lift (or ramp the road through it), and make the terrace a real step. Then assert
reachability in a test for several seeds, including 0.

## 3. High

### H1 — No body radius, and the avatar is drawn half a tile off
The body is a point: Niko stops with his centre exactly on the wall line (measured `x = 5.0`
against a wall at `x = 5.0`). On top of that, `web/src/game/MapScene.ts` [line 113] adds
`TILE_SIZE / 2` to a position that is already continuous, so the avatar is drawn 0.5 m down and
right of where he is. The two together make him look like he is standing inside the wall. The
offset predates Dev-002, but edge walls are what make it visible. Fix both: the radius in
`movement.py` and the client mirror, the offset in the scene.

### H2 — The client rules do not mirror the server
`web/src/world/rules.ts` and `ChunkStore.standingH`. The client ignores `walkable` (it walks into
deep water), ignores headroom, ignores the `void` flag for floors, and treats a wall as blocking
for any level in `z-1 .. z+1` instead of testing the wall's vertical span. It also never draws a
wall stored on a tile with no floor (`MapScene.ts` [line 224] skips the cell), while the server
still collides with it. Each difference is a snap-back. Mirror the server rule, including the
material lookup, and keep both in step when the rule changes.

### H3 — The cutaway does not refresh
`MapScene.ts` [lines 91-94, 112]. `update()` overwrites `viewerH` from the prediction every frame,
so by the time the ack arrives `previousH` already equals the new value and the redraw almost never
fires. Trigger the redraw where `viewerH` actually changes, and do it once per changed value, not
per frame. This is separate from the cutaway rule itself ([Sec. 1]).

### H4 — The test world does not match Dev-002 [Sec. 4.4]
Beyond B5: there is no first floor (the stairs lead to the roof, so the tests that claim "first
floor" verify the roof), the basement has no access at all, and the walls at `x = 160` / `y = 160`
are stored by including those tiles in the footprint, which leaves a 1 m ledge of floor outside the
east and south walls on every level, roof included, plus a parapet with windows and a doorway on
the roof. Store the border walls without giving those tiles floors, and settle the building layout
per [Sec. 1].

## 4. Medium

- **Level lookups are linear in the whole world.** `server/src/etherbound/world/grid.py`
  [lines 113, 140]: `solid_at` and `standing_surfaces` iterate every level of the world on every
  call. With only 12 levels a `standing_surfaces` call costs ~50 us and `find_path` from spawn to
  the roof takes **3.3 s** for a 51-step path. Index levels by `(cx, cy)` before NPCs need the A*.
- **Headroom is off by one.** `grid.py` [line 127] checks `h+1 .. h+4`, but a floor slab is solid
  at its own `h`, so a ceiling exactly 2 m up is rejected and the real requirement is 2.5 m. Decide
  one convention for what a slab occupies and apply it to both ground and floors.
- **Diagonals.** `grid.py` [lines 197-202]: with an `h` argument both detour legs are tested
  against the *original* `h`, so every diagonal on a slope is refused; with `h = None` two legs of
  +0.5 m each let a diagonal climb a full metre.
- **A wall on a floorless tile** falls back to `z * 6` as its base (`grid.py` [lines 167-169]),
  which is wrong for any building whose floor is not on a band boundary.
- **Ladders are missing.** `LEVEL_CLIMBABLE` is defined and never used, and `nav.py` has no ladder
  edges, although Dev-002 [Sec. 3] lists them as the only vertical link above 0.5 m.
- **Hand-written types.** `web/src/world/types.ts` re-declares `WorldChunk`, `ChunkLevel` and
  `Material`, which already exist in the generated `schema.d.ts`. `AGENTS.md` forbids this.
- **`ruff format --check` fails** on `server/alembic/versions/0002_world.py` [line 15], so the
  "all checks pass" claim in Dev-002 [Sec. 9] is not true as it stands.
- **The hub reads the database on every input.** `ws.py` [lines 94-100, 160] calls
  `engine.get_state()` to find the player's chunk although `submit` already returned the position.

## 5. Minor

- Dead code: `grid._levels_at` (its filter is always true), `gen.testworld.generate`,
  `gen.noise.value_noise`, and the `encode_i16` / `decode_u8` aliases in `chunk.py`.
- `grid.chunk()` does not load or generate lazily, so the API shape of Dev-002 [Sec. 4.2] is not
  there yet.
- Rendering uses a `Graphics` object per chunk instead of the render texture of [Sec. 5.2], and the
  darker strip on east-west walls is missing.
- Cliff lines on a chunk border are not redrawn when the neighbouring chunk arrives later.
- `ChunkStore.topmostZ` includes the `NO_FLOOR` sentinel among its candidates.
- `StandingSurface.z` comes from the level the slab is stored in, while the actor's `z` is
  `h // 6`; the top stair step reports `z = 2` and the actor standing on it reports `z = 3`.
- `docs/utils/VERSION.md` still lists `server.testmap`; Dev-002 [Sec. 6] removes the row.
- `utils/VISION.md` [Sec. 5] still describes pathfinding as "per-level A* plus vertical edges", while
  `nav.py` searches standing spots.
- The Notion updates required by `AGENTS.md` were not verified in this review.

## 6. Tests to add

- Movement: a straight uphill and downhill walk across ten 0.5 m steps ends at the far side, from
  several starting offsets (catches B1 — the current test passes on the frozen body).
- Movement: the same walk over sand and asphalt differs in distance from grass (catches B2).
- Materials: start the engine, add a key at the **top** of a test TOML, restart, and assert the
  existing chunks still decode to the same materials (catches B3).
- Networking: `new_game` followed by a chunk-border crossing pushes the regenerated chunks, and the
  client store accepts them (catches B4).
- Generation: from spawn, the building interior, the first floor, the basement, the pit bottom and
  the north side of the terrace are all reachable, for seeds 0, 123 and three more (catches B5).
- Generation: the road is walkable end to end.
- Walls: the body stops 0.3 m short of the wall line, not on it.
- Client/server parity: one table of cases (deep water, low ceiling, void, wall span, doorway,
  window) asserted against both `grid.py` and `rules.ts`, so H2 cannot drift again.
- Nav: no path crosses a wall; generation of 8x8 chunks stays under 1 s (currently 0.58 s).
- Determinism: compare **all** blobs, including `floor_mat`, `edge_flags`, `flags` and `strata`;
  `TestWorld.blob_bytes` currently omits them.

## 7. What must not break

- The engine stays the only writer; these fixes add no new writer.
- One action API: no `climb` shortcut appears while fixing B1.
- Determinism: generation keeps using only the `worldgen` stream, and the fixes to B5 change the
  test world's bytes, so `gen_version` goes to 2 and an existing save regenerates.
- An existing Phase 0 save still opens without a wipe.
- The 20 Hz loop still performs no database reads for world lookups (and, with the hub fix, one
  fewer for the actor).
- TypeScript types stay generated; removing `web/src/world/types.ts` must not break the build.

## 8. Modules and versions

| Module | Path | Version |
|---|---|---|
| server.engine | `server/src/etherbound/engine/` | v0.0.2 → v0.0.3 |
| server.world | `server/src/etherbound/world/` | v0.0.1 → v0.0.2 |
| server.net | `server/src/etherbound/net/` | v0.0.2 → v0.0.3 |
| server.db | `server/src/etherbound/db/`, `server/alembic/` | v0.0.2 → v0.0.3 |
| web.world | `web/src/world/` | v0.0.1 → v0.0.2 |
| web.game | `web/src/game/` | v0.0.3 → v0.0.4 |
| web.net | `web/src/net/` | v0.0.2 → v0.0.3 |

`server.app` only changes if the menu or the routes are touched; bump it then.

## 9. Todo

### Decisions
- [ ] Approve [Sec. 1] (cutaway rule, building layout, body radius) and write the outcome into
      `utils/VISION.md` [Sec. 5] if the model changes

### Blocking
- [x] B1: `h` changes only when the body changes tile
- [x] B2: cost charged against the budget, every sub-step, server and client
- [x] B3: registry ids come from the database, new keys appended and persisted
- [x] B4: chunk state cleared and re-pushed on regeneration, client store reset
- [x] B5: approach carved from the road, road excluded from the terrace lift, terrace as a step

### High
- [x] H1: body radius restored, avatar offset removed
- [x] H2: `rules.ts` mirrors the server rule (walkable, headroom, void, wall span, floorless walls)
- [x] H3: cutaway redraw fires on every `viewerH` change
- [x] H4: building rebuilt per [Sec. 1], border walls without ledges

### Medium and minor
- [ ] Levels indexed by `(cx, cy)`
- [ ] One slab-occupancy convention; headroom exactly 2 m
- [ ] Diagonals: per-leg `h`, no 1 m climb
- [ ] Wall base from the floor, not from the band
- [ ] Ladders: `climbable` honoured by `can_step` and by `nav.py`
- [x] `web/src/world/types.ts` replaced by the generated types
- [x] `ruff format` on the migration
- [ ] Hub uses the position `submit` returns
- [ ] Dead code, `topmostZ` sentinel, `StandingSurface.z`, `VERSION.md` row, `VISION.md` A* line

### Tests
- [ ] Every item in [Sec. 6]

### Closing
- [ ] `CONTEXT.md`: corrected pitfalls (slope movement, material ids, chunk invalidation)
- [ ] `docs/utils/VERSION.md` per [Sec. 8]
- [ ] Re-run the acceptance list of Dev-002 [Sec. 9] against seed 0, not only seed 123
- [ ] Notion: Systems Index, Work Report for the date, Dev Blog page
- [ ] Move this doc to `docs/done/`

## 10. Out of scope

The Dev-002 exclusions still hold: objects, doors as objects, ops beyond `move`, the event bus,
line of sight, physics, water simulation, LimeZu art, the city generator, lifts, NPCs. This doc
adds no feature; it only makes Dev-002 true.

---

## TL;DR

Dev-002 landed with the right data model and a movement layer that freezes: any height change can
leave the body holding a `h` its own tile does not have, and it stops dead (60 % of uphill runs,
100 % of downhill ones). Slope and material cost cancel themselves out on both sides of the wire,
the material registry loses its append-only guarantee the moment someone edits the TOML, a new game
never invalidates the client's chunks, and the world the game actually boots (`seed = 0`) has no
way into the building — the acceptance list was signed off against seed 123. Five blocking fixes,
four high, a handful of medium, one decision to make about the cutaway, and a test suite that fails
on the bug instead of passing on it.
