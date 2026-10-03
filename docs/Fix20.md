# Fix20: A crowd of 200 and the all-actor scan

[Bugfix] [Performance] [Engine] [Tooling]

The crowd tick has regressed since the cut-over: `Bench -- --extras 200` costs ~160 ms per tick
(179 ms with every Extra needy) and 1,000 Extras cost ~3.7 s, against 64.9 ms recorded at Dev-025.
At x10 a tick is due every 100 ms (`SimClock.Interval = TimeScale / Speed`), so even 200 Extras
overrun it. This plan does two things: it sets **200 Extras** as the crowd the sim is built and
gated for (1,000 is more than the world needs: 200 fill a district), and it removes the
quadratic cost that stops the sim from meeting that target.

## 1. Findings

Read from the source; **nothing here was run**. Stage 0 confirms or corrects every item.

- **Every `move` scans the whole crowd.** `MoveOp.Resolve` passes `ctx.Session.Actors()` (all
  actors but the mover) to `Movement.MoveInWorld`, which sorts them again (`OrderBy`) and then
  `CanMoveBody` walks all of them on each 0.05 m substep. Cost per move is O(n log n) plus
  O(n x substeps), so a tick of n Extras is superlinear in n.
- **Every `Submit` pays for a copy of the crowd.** A `Session` is created per call (`NewSession`);
  its first `Actors()` clones every stored `ActorRow` and every later call filters and sorts the
  whole dictionary again (`Session.cs`, `Actors()`).
- **Other callers share the pattern.** `OpBase.OthersOn` (`BodyOps.cs` 166), `BodyOps.cs` 184,
  292, 317, 357, `HandlingOps.cs` 30 and 41, `PhysicsOps.cs` 326 and `Menu.cs` 98 each ask for
  the full list to look at one tile or a few. `WorldEngine.cs` 109, 489, 551 and 692 are
  all-actor passes by nature (load cache, tick, snapshot) and stay.
- **A pure per-tile lookup already exists in spirit.** `WorldEngine.cs` 551 builds `actorBuckets`
  by tile for the minimap/frame; the engine has no index it keeps between calls.
- **Not caused by needs.** Dev-011 measured the same figure with and without needs.
- **Not yet known:** the commit between Dev-025 (v3.0.0) and `v5.2.0` that introduced it
  (collision against other bodies, Dev-025 era, is the suspect) and how much of the cost is the
  clone versus the substep loop. Stage 0 settles both before any code changes.

## 2. Decisions

- **D1. The target crowd is 200 Extras.** The bench's headline row, the doc figures and the
  budget below are 200. Larger rows (1,000) stay as a stress run, with no gate.
- **D2. Budget at 200 Extras, Release, x10, `needs: start`: average <= 50 ms per tick and p95 <=
  100 ms** (half and all of the 100 ms interval). Worst case (`--needy`) <= 1.10 x the
  `start` figure, the bound `PENDING.md` already asks for. **Approval needed**: say if you want a
  tighter or looser number.
- **D3. The fix is an index, not a cache of results.** The engine keeps a derived spatial index of
  committed actor positions; a session answers "actors near (x, y)" from it plus its own
  uncommitted changes. The index is rebuilt on load, never persisted, so there is no schema change
  and no migration.
- **D4. Same answers, same order.** Whatever the index returns is ordered by id, as `Actors()` is
  today, so collision results, events and replays are byte-identical.
- **D5. The timing gate is not part of `npm run check`.** Wall-clock numbers are noisy on a
  shared machine. The bench prints PASS/FAIL against D2 and a person runs it; the regression is
  guarded in `check` by an operation-count test instead ([Sec. 5]).
  **Approval needed.**

## 3. Scope

### Stage 0. Measure first

- Run `Bench -- --extras 200` and `-- --extras 1000` at `v5.2.0` and today, with `--trace` and
  with `--without-brain`, and record the split between `Actors()` clone/sort, the `MoveInWorld`
  sort and `CanMoveBody`.
- Bisect between the Dev-025 close and `v5.2.0` for the commit that crossed 100 ms at 200
  Extras. Record it in this doc ([Sec. 6]); do not revert it blindly, it may hold collision
  behaviour we want.
- Capture the **golden before touching code**: seed 7, `lab`, 200 seeded Extras, 100 ticks at
  x10 with the brain on (300 took 50 s on the old engine); store a digest of the event log and the
  final `actor` rows as the expected value of `CrowdReplayRules` ([Sec. 5]).

### Stage 1. The index

- `WorldStore` holds `ActorsByTile`: `Dictionary<(int X, int Y), List<string>>` of committed
  actor ids, kept in `Session.Apply` (insert, delete, and move when `UpdatedActors` changes a
  tile) and rebuilt by `Reindex`/load.
- `Session.ActorsNear(double x, double y, double radius)` returns the actors whose body can
  overlap that circle, in id order:
  committed candidates from the index, then the session's own working copies override or remove
  them (a row moved, added or deleted in this session is judged by its working position).
  It clones only the rows it returns, so a session no longer copies the crowd to move one
  actor.
- `Session.Actors()` keeps its meaning for the all-actor callers.

### Stage 2. The callers

- `MoveOp.Resolve` asks for `ActorsNear(actor position, step distance + 2 x BodyRadius + margin)`
  instead of `Actors()`, and `MoveInWorld` stops re-sorting an already ordered list.
- `OthersOn`, `BodyOps` (the four scans above), `HandlingOps`, `PhysicsOps` 326 and `Menu` 98
  move to `ActorsNear` where they look at one tile or a short path; each keeps its current rule
  and message.
- `CanMoveBody` keeps its logic (an existing overlap may be escaped without worsening it); only
  the list it walks gets shorter.
- Anything that needs every actor (`WorldEngine` load cache, tick loop, snapshot) is untouched.

### Stage 3. Bench and gate

- `EtherBound.Bench` defaults to `200` (rows `200`, `1,000` as stress; 5,000 and 10,000 stay
  behind `--extras`), prints the D2 verdict for the 200 row and keeps `--needy` and `--no-needs`.
- The header comment and the command line in `CONTEXT.md` change from "1,000/5,000/10,000" to
  the new default.

### Stage 4. Needy worst case, only if needed

If `--needy` at 200 stays above 1.10 x `start` after stage 2, cache which chunks hold drinkable
ground or scan on a longer minute (`PENDING.md`, "Needs: worst-case crowd cost"). If it already
meets D2, close that item without code.

## 4. What must not break

- **Determinism and replay.** `ActorMovementRules`, `NeedsReplayRules`, `InputReplay` goldens and
  `SaveCompatibility` pass unchanged; the stage 0 golden (`CrowdReplayRules`) matches before and
  after the change.
- **The engine is the only writer.** The index is derived state written only from `Apply`;
  no system reads it to decide an outcome that the engine would not reach through the session.
- **One action API.** Player and Extras go through the same `Submit`; the player gets no
  shortcut from the index.
- **Standing rule and contact.** Actor contact still blocks overlapping movement, never pushes or
  damages, and an existing overlap may be escaped without worsening it.
- **Session isolation.** A discarded session leaves the index untouched; an uncommitted move by
  one session is invisible to another.
- **No schema change.** No migration, no new column, no new table.
- **Event order.** Same events, same `seq`, same order for the same inputs.

## 5. Acceptance

- [x] **Equivalence.** `ActorIndexRules.cs`: for random crowds, `ActorsNear` equals a brute-force
      filter of `Actors()` over radii and positions, including actors moved, added and deleted
      inside the same uncommitted session, and across a commit and a discarded session.
- [x] **Identical behaviour.** `CrowdReplayRules.cs`: the stage 0 golden (200 Extras, 100 ticks)
      matches byte for byte; `ActorMovementRules.cs` and `NeedsReplayRules.cs` unchanged.
- [x] **No crowd copy per move.** `CrowdCostRules.cs`: a `move` submit allocates the same in a
      200-actor world as in a 50-actor one (bytes, not time: the old engine allocated 1.5 MB and
      5.3 MB, the new one ~49 KB for both), so a reintroduced all-actor scan fails `npm run check:sim`.
      A 1,000-actor world was dropped from it: the old engine's counter was not monotonic there.
- [ ] **Budget (partly met, see [Sec. 6]).** Manual, because wall time is noisy: `Bench -c Release` at 200 Extras shows
      average <= 50 ms, p95 <= 100 ms, and `--needy` within 1.10 x of `start`; the numbers are
      pasted in [Sec. 6].
- [x] **Scaling shape.** Manual: the 200 and 1,000 rows grow roughly linearly (5x the crowd, 3.7x the time) (the
      3.7 s figure at 1,000 drops by an order of magnitude); no hard gate at 1,000.
- [ ] **Play check.** Manual: `lab` at x10 with 200 Extras keeps the sim thread within the
      interval (the HUD clock does not lag the wall clock for a minute of play).

## 6. Results

Release, x10, `needs: start`, 8 logical cores, same bench before and after.

| Extras | before (avg / p95) | after (avg / p95) |
|---|---|---|
| 200 | 164.8 / 278.6 ms | 34-41 / 78-107 ms (3 runs; 35.1 / 89.6 in the final one) |
| 1,000 | ~3,700 ms (`PENDING.md`) | 130.5 / 207.5 ms |
| 200, `--needy` | ~179 ms (`PENDING.md`) | 53-59 / 115-160 ms |

- **Cause.** Confirmed by allocation, not by a profiler: a `move` allocated ~26 KB per actor in the
  world (1.5 MB at 50 actors, 5.3 MB at 200) because `Actors()` cloned every row, and `MoveInWorld`
  then sorted and walked all of them. After the change it allocates ~49 KB whatever the crowd.
- **Bisect: not done.** Once the cost was tied to those scans there was nothing to revert; the
  commit that introduced it is not recorded.
- **Budget (D2).** Average: met (35-41 ms against 50). p95: met in the final run (89.6 ms) but it
  swings between 78 and 107 ms across runs; the slowest ticks are the first ones after warm-up
  (JIT) and ticks that run a gen-1 collection, not a steady cost. Needy worst case: **not met**,
  about 1.45x of `start` against the 1.10x asked for. A scan minute (Menu 0.43 ms plus
  Percepts 0.12 ms per Extra, one in five) is inherent work now that the move cost is gone; making
  it 5x cheaper means caching drinkable ground or making Extras back off after an empty scan, and
  the second changes what an Extra decides, which is out of scope. Left in `PENDING.md` with
  these numbers.
- **Tried and dropped:** caching the sorted object list in `Session.Objects()` for the Menu read;
  no measurable change in the needy case.
- **Manual play check:** not run (needs the Godot client).

## 7. Docs and versions

- `docs/PENDING.md`: delete "Crowd tick cost has regressed since the cut-over"; edit or delete
  "Needs: worst-case crowd cost is +12 %" per stage 4.
- `CONTEXT.md`: the Commands line for the bench, the Modules text for `sim.engine` (the actor
  index) and the Data model note that the index is derived; the Dev-025 sentence keeps its
  1,000-Extra figure as history.
- `docs/utils/VERSION.md`: `sim.engine` v0.0.7 -> v0.0.8, `sim.db` v0.0.6 -> v0.0.7 (the load
  invalidates the index), `tooling` v0.0.24 -> v0.0.25, and the overall version at commit time
  per `COMMITS.md` [Sec. 2]. `sim.minds` stays: the brain's logic does not change, only what its
  submits cost.
- `docs/utils/VISION.md` does not state a crowd size and is not touched.
- System map: update `docs/excalidraw/system-map.excalidraw` (the engine gains a derived actor
  index).
- Notion: record the fix per `docs/utils/NOTION.md`.

## 8. Out of scope

- **Food for Extras in the endless world.** In the manual Dev-011 test the Extras reached zero
  because the streaming world places no objects and water is read only within 12 m; that is the
  missing supply/city layer ("Recipes and supply", "City authoring" in `PENDING.md`), not a
  needs bug. Cheap stopgaps (a bigger starting kit, scattered food) are a separate decision.
- Parallelising the tick, a chunk-based crowd LOD and any change to what an Extra decides.
- Raising the crowd above 200 later: it needs its own measurement.

## 9. Todo

- [x] Stage 0: golden recorded; cause confirmed by allocation; bisect skipped ([Sec. 6]).
- [x] Stage 1: the per-tile actor index in `WorldStore` (`ActorTiles`), `Session.ActorsNear`/`ActorsOn`.
- [x] Stage 2: `MoveOp` and the one-tile callers read `ActorsNear`/`ActorsOn`.
- [x] Stage 3: bench default of 200, 1,000 as a stress row, the D2 verdict line.
- [x] Stage 4: needy worst case measured at ~1.45x; not met, left in `PENDING.md` ([Sec. 6]).
- [x] Tests: `ActorIndexRules.cs`, `CrowdReplayRules.cs`, `CrowdCostRules.cs`.
- [x] Versions, `CONTEXT.md`, `PENDING.md` and the system map.
- [ ] Manual play check and Notion; then move this doc to `docs/done/`.

---

## TL;DR

200 Extras is the target crowd and the bench's gate (<= 50 ms average, <= 100 ms p95 per tick at
x10). Each `move` scans and re-sorts the whole crowd and each `Submit` clones it, which is why
the tick is superlinear. Measure and bisect first, then add a derived per-tile actor index with
`Session.ActorsNear`, move the one-tile callers to it, and prove identical behaviour with a
before/after replay golden and an operation-count test. No schema change.
