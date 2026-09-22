# Fix02: Dev-005 review

[Bugfix] [Engine] [Events] [Docs]

Review of the Dev-005 implementation as committed in `v0.4.0` (`f57ca8d`, `docs/done/Dev-005.md`).
The code follows the plan and every trap in Dev-005 [Sec. 3] is respected. Fix 02 implements the
fresh-save clock event, documents drain semantics, and covers reentrant/concurrent dispatch. All
automated checks pass (41 server tests, lint, formatting, types, generated client types, web build,
and launcher tests). The manual GUI acceptance remains blocked because a launcher/server was
already active in this checkout; it was left running and its save was not manipulated.

Nothing in the repository was changed by the review.

## 1. Decisions to approve

| Topic | Question | Proposal |
|---|---|---|
| Drain from another task | `EventBus.drain()` returns at once if *any* drain is running, including one in another task. So when the clock task is broadcasting a tick, `new_game`, `submit` and `set_clock` return before their subscribers run. Dev-005 [Sec. 2.4] says the `new_game` subscribers run "before `engine.new_game` returns", and that is not guaranteed | **Accept the behaviour and document it.** Order is still exact, and nothing depends on the timing today. Making a drain from another task wait would chain one player's ack to another task's slow broadcast (Dev-005 [Sec. 3], trap 4) |
| Initial clock on a fresh save | `new_game` logs `world.generated`, `actor.spawned`, `clock.changed`. The first generation in `ensure_world` logs only the first two, so a run started on an empty database has no initial clock in its log | **Emit `clock.changed` in `ensure_world` whenever it generates chunks**, so both paths start the log the same way |

## 2. High

### H1: The overall project version is stale
`docs/utils/VERSION.md` [line 3] reads `Overall project version: v0.3.0`. The latest commit is
`v0.4.0`. `AGENTS.md` requires the overall version to match the version of the latest commit,
and the launcher reads this line for its banner and console title
(`launcher/src/Program.cs` [lines 76-85]), so the launcher shows the wrong version. The line
has been stale since it was added in `v0.3.3`.

Fix: set the line to the version of the commit that ships this fix (for example `v0.4.1`), then
confirm the launcher banner shows it. The module versions in the table are correct and stay as
they are, apart from the bumps in [Sec. 5].

### H2: The Dev-005 acceptance was never run in the GUI
`docs/done/Dev-005.md` [Sec. 7] leaves "Manual acceptance 1–5" unticked, yet the doc was moved
to `docs/done/`. The isolated smoke test covered startup logs, the API, the `new_game` reset
and persistence. It never covered the GUI. `docs/done/` is an archive, so the result goes here.

Run with `EtherBound.exe`, with no other server running:

1. `logs\server.log` shows uvicorn's startup line after the migrations.
2. Walk about 10 m. `GET /api/events?type=actor.moved` lists about 10 rows, one per tile, with
   consecutive `from_tile` / `to_tile`.
3. Pause and resume from the HUD: exactly two new `clock.changed` rows. The HUD shows the right
   state at each step, and x1/x3/x10 behave as before.
4. `POST /api/game/new {"seed": 7}` while the client is open: the log restarts at `seq` 1 with
   `world.generated`, `actor.spawned`, `clock.changed`, and the client redraws the new world
   without a reload.
5. Quit and relaunch: the log is intact, and the next stored `seq` is the last one + 1.

Any failure becomes a blocking item in this doc before the fix closes.

## 3. Medium

### M1: `ensure_world` logs the initial clock (per [Sec. 1])
`server/src/etherbound/engine/world.py`, `ensure_world`. Inside the `if not self._has_chunks(...)`
branch, after `_ensure_actor`, append `ClockChanged(speed=meta.speed, paused=meta.paused)`. The
order must be `world.generated`, `actor.spawned`, `clock.changed`, the same as `new_game`. An
existing save that already has chunks emits nothing new.

The lifespan in `app.py` keeps its explicit `clock.load(...)` after the drain. Existing saves
still need it, because they emit no `clock.changed` at startup.

Test impact: `test_event_filters_and_new_game_websocket_refresh` asserts `len(initial) == 2`.
It becomes 3. Its `after_seq=1, actor_id=niko` filter still returns `actor.spawned` first.

### M2: Document the drain semantics (per [Sec. 1])
- `CONTEXT.md`, "Event bus" contract: add that a `drain()` called while another task is draining
  returns immediately, and that task dispatches the caller's events in `seq` order, possibly after
  the caller has returned. Nothing guarantees that subscribers have run when `submit`,
  `set_clock` or `new_game` returns.
- `server/src/etherbound/events/bus.py`, the `if self._draining: return` guard: one comment giving
  the reason (a nested drain would dispatch recursively; a waiting drain would deadlock a handler
  that submits an action).

### M3: Test the re-entrant path with a real `submit`
Dev-005 [Sec. 6] item 2 asks for "a handler that submits an action during a drain".
`test_bus_drains_reentrant_actions_fifo_without_nesting` only calls `bus.enqueue` + `bus.drain`,
so it never exercises the engine lock. Add an engine-level test:

- Subscribe a handler to `ClockChanged` that calls
  `await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=0.2)` (this crosses a tile
  from the spawn at x = 121.5), and a second handler on `Event` that records types.
- `await asyncio.wait_for(engine.set_clock(speed=3), timeout=2)`. The timeout turns a deadlock
  into a failure, not a hang.
- Assert the recorded order is `clock.changed` then `actor.moved`, the move is stored with
  `seq` = the clock's `seq` + 1, and the submit returned `accepted=True`.

Keep the existing bus-level test.

## 4. Minor

- **Transient `seq` reuse.** `clock.ticked` takes a `seq` that is never stored. `_next_seq` is
  reloaded as the stored max + 1, so after a restart a new event can reuse the `seq` of a tick
  dispatched before it. Harmless today. Add to the `CONTEXT.md` bus contract: "`seq` is unique
  among stored events; a transient event's `seq` may be reused after a restart, so never key
  state on it."
- **`Event.type` is `Any`.** Dev-005 [Sec. 2.1] specifies `type: str`. If `str` passes `pyright`
  with the `Literal` overrides in the subclasses, use `str`. Otherwise keep `Any` and add a
  one-line comment saying why (subclasses narrow it to a `Literal`).
- **Concurrent drain test.** Pin the behaviour accepted in [Sec. 1]. Task A drains with a handler
  that awaits an `asyncio.Event`. Task B enqueues and drains, and returns at once. Release A.
  All events are dispatched in `seq` order, and each exactly once.

## 5. Modules and versions

| Module | Path | Version |
|---|---|---|
| server.engine | `server/src/etherbound/engine/` | v0.0.4 → v0.0.5 (M1) |
| server.events | `server/src/etherbound/events/` | v0.0.1 → v0.0.2 (M2 comment, `Event.type`) |

Overall project version per H1. `server.app`, `server.net`, `server.db` and every web module do
not change. There is no schema change and no migration.

## 6. What must not break

- Dispatch order: one global `seq`, FIFO, never nested; `enqueue` inside the lock, `drain`
  outside.
- `new_game` still leaves exactly `seq` 1 `world.generated`, 2 `actor.spawned`,
  3 `clock.changed`.
- Opening an existing save emits no new event and does not touch its log.
- The `Clock` still starts with the saved speed and pause, on fresh and existing saves.
- Rejections still emit nothing; `clock.ticked` is still not stored.
- All existing server tests pass, plus the new ones. The web build and `gen:types` show no diff.

## 7. Todo

### Decisions
- [x] Approve [Sec. 1] (drain semantics accepted as documented; initial `clock.changed` in
      `ensure_world`)

### High
- [ ] H1: overall project version set to `v0.4.1`; launcher banner not verified because the active
      launcher has no visible window/title in this session
- [ ] H2: manual GUI acceptance not run; startup-log item 1 is verified at 15:59:11, but the
      remaining walkthrough would mutate the active save or require stopping the user's launcher

### Medium
- [x] M1: `ensure_world` emits `clock.changed` on first generation; API test asserts 3 events
- [x] M2: drain semantics in `CONTEXT.md` and a reason comment on the guard
- [x] M3: engine-level test with `submit` inside a handler, guarded by a timeout

### Minor
- [x] Transient `seq` reuse documented in `CONTEXT.md`
- [x] `Event.type` remains `Any` with a reason comment because `str` fails pyright for Literal overrides
- [x] Concurrent drain test

### Checks
- [x] `cd server && uv run pytest && uv run ruff check && uv run ruff format --check && uv run pyright`
- [x] `cd web && npm run gen:types && git diff --exit-code src/net/schema.d.ts && npm run build`

### Closing
- [x] `CONTEXT.md`: bus contract (M2, `seq` reuse), fresh-save log order, test count
- [x] `docs/utils/VERSION.md` per [Sec. 5] and H1
- [x] Notion: Systems Index and Dev Blog page updated
- [ ] Notion Work Report for 22/09/2026: existing report page is not accessible to the configured
      integration; it was not duplicated
- [ ] Move this doc to `docs/done/`

## 8. Out of scope

Everything Dev-005 [Sec. 8] excludes still holds: new verbs, RNG persistence, input replay,
witnesses, streaming events to the client, log pruning, `decision.*` events. Waiting drains and
per-task queues are also out of scope, unless [Sec. 1] is answered the other way.

---

## TL;DR

Dev-005 is sound: the pipeline, the ordering rules and the migration match the plan. Fix 02
implements the missing initial clock event and documents/tests the accepted drain behavior. To
close the remaining manual acceptance:

- Verify the launcher banner for `v0.4.1` after a safe restart.
- Run the Dev-005 GUI acceptance without an existing launcher/save in use.

There is no schema change. `server.engine` and `server.events` get one bump each.
