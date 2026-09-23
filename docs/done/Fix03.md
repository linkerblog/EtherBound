# Fix03: Dev-007 movement review (prediction jumps, moves missing from the log)

[Bugfix] [Net] [Prediction] [Tooling]

Review of the walking reported after `v0.5.0` (`docs/done/Dev-007.md`): "I still don't see the
move in the logs" and "the character moves oddly, in jumps". Dev-007 made the browser's inputs
reach the server for the first time. The client prediction was written when no ack ever arrived,
so its reconciliation had never run against a real server. It does now, and it jumps.

Checked on the live save (seed 781227069): the server does move Niko. It holds 276 events, most of
them `actor.moved`, and Niko is on the building's first floor (137.1, 147.3, `h = 12`).

## 1. Findings

### F1: A jump forward then back at the start of every walk
`ClientPrediction.reconcile` rebases the prediction to the ack position and then advances it by
`min(now - lastServerUpdate, 0.25)` seconds. `lastServerUpdate` only moves on an ack, so the first
ack after standing still adds 0.25 s: the prediction lands 1 m ahead of the server. The next ack
(50 ms later) rebases it to server + 0.2 m, so it jumps 0.8 m back. This happens every time a walk
starts. It is the most visible cause.

### F2: A snap back when the key is released
The server moves only in whole steps: each `input` is 1/20 s of walking (0.2 m). The time a key
stays held after the last input never reaches the server. On release the prediction is up to
0.2 m ahead, and the ack to the zero vector snaps it back.

### F3: Jitter on every ack
Inputs are frame-quantised, so at 60 fps the gap between them is 33 ms or 50 ms, and more at lower
frame rates. Every ack rebases the prediction to server + (time since the previous ack), which
varies from ack to ack, while the server always moves exactly 50 ms. The result is ±0.1 m on each
of the 20 acks a second.

### F4: The prediction ignores material cost
The server divides speed by the material's `walk_cost`: asphalt 0.9 (11 % faster), topsoil 1.1,
sand 1.35, shallow water 1.6. `web/src/world/rules.ts` knows only the slope multipliers. Every ack
corrects the difference, so walking off the road visibly changes the hitching.

### F5: Moves are not in `server.log` (not a bug)
Events go to the `event` table and are read with `GET /api/events` (for example
`/api/events?type=actor.moved`). Nothing writes them to `logs/server.log`. That log holds only
uvicorn's access lines, which is why no move ever shows up there.

## 2. Approved decisions

| Topic | Decision | Why |
|---|---|---|
| Input is a finished step | `input {sequence, dx, dy, dt}` means "walked `(dx, dy)` for `dt` seconds". `dt` is validated as `0 < dt ≤ 0.1` and defaults to `0.05`. The hub submits it with `delta_seconds = dt` | The server and the prediction integrate exactly the same time, so releasing a key loses nothing (F2), and the step length no longer depends on the frame rate (F3) |
| Sending | While a key is held, the client sends one 50 ms step, carrying the remainder so the average stays exact. When the direction changes or the key is released, it sends one final partial step for the old direction, covering the time since the last step. No zero vector is sent any more | A walk ends with the exact time walked. A zero-length move is still "not an action" on the server, so nothing about interruption changes |
| Prediction | `predicted = authoritative + replay(unacked steps) + current partial step`. An ack sets `authoritative` to the ack position and drops the steps up to its sequence. No wall-clock time is involved | This is standard client-side prediction with input replay. It removes F1 and F3, and an ack in steady state changes nothing on screen |
| Material cost | `rules.ts` divides the step by the surface's `walk_cost`, just as `move_in_world` does. `ChunkStore` materials carry `walk_cost` | Mirrors the server (F4). The server still wins on every ack |
| Flush before an action | `sendAction` first sends the partial step walked so far | Otherwise, choosing `DIG` while a key is still held and then releasing it sends a step that interrupts the dig for walking that happened before it was chosen (Dev-007 [Sec. 4] trap 5). Walking after the action still interrupts it, as it should |
| Events in the log | A bus subscriber writes every logged event to `server.log` at INFO: `event 812 actor.moved niko {"from_tile": …, "to_tile": …, "mode": "walk"}`. Unlogged events (`clock.ticked`, `chunk.changed`) are skipped | Answers F5 at a glance. It produces one line per tile crossed, not per input, which is tolerable. The alternative is to leave it and point to `/api/events` |
| Trust | `dt` is clamped by validation only. There is no rate limit | The game is local and single-player. A rate limit belongs to multiplayer, if that ever exists; it is noted in `PENDING.md` |

## 3. Changes

### 3.1 `server.net`
- `InputMessage.dt: float = Field(default=0.05, gt=0, le=0.1)`.
- `WebSocketHub.handle`: `engine.submit(PLAYER_ID, MoveAction(...), delta_seconds=message.dt)`.
- Regenerate `schema.json` and `schema.d.ts`.

### 3.2 `server.app` (events in the log)
- `app.py`: `bus.subscribe(Event, log_event, name="log")`, where `log_event` skips unlogged
  events and writes `logger.info("event %d %s %s %s", seq, type, actor_id or "-", json(data))`
  to the `etherbound.events` logger, which uvicorn's handlers already print.

### 3.3 `web.world`
- `ChunkStore.setMaterials` takes `{walkable, walk_cost}`. New `store.walkCost(x, y, h)` returns
  the cost of the standing surface at `h`, or 1.
- `rules.ts`: `stepMultiplier(deltaH, walkCost) = slopeMultiplier(deltaH) / walkCost`, used for
  every sub-step as on the server.

### 3.4 `web.net`
- `sendInput(direction, dt, sequence)` sends `{type: "input", sequence, dx, dy, dt}`.
- `ClientPrediction`:
  - `pending: {sequence, direction, dt}[]` and `base` (authoritative + replay, recomputed only when
    pending or authoritative change).
  - `pushStep(step)`: appends and advances `base` by that step.
  - `reconcile(position, sequence)`: `authoritative = position`, drops steps `≤ sequence`, then
    recomputes `base` by replaying the rest from `authoritative`.
  - `render(direction, partialSeconds)`: `base` plus the partial step, recomputed every frame from
    `base`, never accumulated into it.
  - `idle()`: no pending steps, zero direction, zero partial.
  - The `performance.now()` bookkeeping is removed.
- `sendAction(action)`: the scene is asked to flush the partial step first (a `beforeAction` hook
  that `MapScene` registers on the client).

### 3.5 `web.game`
- `MapScene.update`: `partial += seconds` while a direction is held.
  - Held and `partial ≥ 0.05`: send a 0.05 s step, `partial -= 0.05` (capped, so one step per
    frame at most).
  - Direction changed or released: if the old direction was non-zero and `partial > 0`, send a
    step of `partial` for the old direction, then `partial = 0`.
  - Render `prediction.render(direction, partial)`.
- The idle resync on ticks stays as it is and uses the new `idle()`.

### 3.6 Docs
- `CONTEXT.md`: the input contract (`dt`, a step per 50 ms, the final partial step), the
  prediction model, `server.log` event lines, and the "Held keys repeat" pitfall rewritten.
- `PENDING.md`: "input rate limit (multiplayer only)" under Deferred work.

## 4. Modules and versions

| Module | Version |
|---|---|
| server.net | v0.0.5 → v0.0.6 |
| server.app | v0.0.5 → v0.0.6 |
| web.world | v0.0.2 → v0.0.3 |
| web.net | v0.0.6 → v0.0.7 |
| web.game | v0.0.8 → v0.0.9 |

The overall project version moves from `v0.5.0` to `v0.5.1`.

## 5. What must not break

- The server stays authoritative. Only `dt` is new, and it is bounded.
- Zero-length moves still never interrupt an activity. An action submitted mid-walk is not
  interrupted by walking that happened before it.
- Dev-007's climb and dig resync (idle and ticks) and the snapshot reset from Dev-006.
- An old client without `dt` still works, since it defaults to 0.05.

## 6. Tests and checks

Server (`test_api.py`):
1. An `input` with `dt = 0.1` moves Niko twice as far as one with `dt = 0.05` on flat asphalt.
2. `dt = 0.2` and `dt = 0` get an `error` and move nothing. A missing `dt` behaves as 0.05.
3. A logged event produces one `etherbound.events` INFO record (`caplog`); `clock.ticked` produces
   none.

Checks: server pytest, ruff, format, pyright; `etherbound-schema`, `gen:types`, web build.

Manual acceptance with `EtherBound.exe` (the web client has no test runner):
1. Walk along the road for 5 s and stop: no jump at the start or at the stop.
2. After stopping, the `POS` readout matches `GET /api/game/state` within 0.05 m.
3. Walk from asphalt onto grass and back: the speed changes with no hitch.
4. Walk at x10 and paused: no drift and no stepping while paused.
5. Hold a key, right-click, choose `DIG` on the tile ahead, then release the key: the dig keeps
   running. Walking again cancels it.
6. `logs/server.log` shows `event … actor.moved niko …` lines while walking.

## 7. Todo

### Decisions
- [x] Approve [Sec. 2], including the event lines in `server.log`

### Code
- [x] `InputMessage.dt` and the hub [Sec. 3.1]
- [x] Event log subscriber [Sec. 3.2]
- [x] `walk_cost` in the prediction rules [Sec. 3.3]
- [x] Step protocol, replay prediction, flush before an action [Sec. 3.4]
- [x] Step cadence and render in `MapScene` [Sec. 3.5]
- [x] Regenerate `schema.json` and `schema.d.ts`

### Checks
- [x] Server tests 1–3 [Sec. 6], server checks and web build
- [ ] Manual acceptance 1–6 [Sec. 6]

### Closing
- [x] `CONTEXT.md`, `PENDING.md` [Sec. 3.6] and `docs/utils/VERSION.md` [Sec. 4]
- [x] Notion Systems Index, Work Report for the date and Dev Blog page
- [x] Move this doc to `docs/done/`

## 8. Out of scope

- Server-side interpolation or a fixed-rate server movement tick.
- Rendering other actors (there are none yet).
- An input rate limit.

---

## TL;DR

Moves do reach the server (276 events on the live save). They live in `GET /api/events`, not in
`server.log`, and this fix also writes them there as INFO lines. The jumps come from the
prediction, which had never run against real acks:
- A 0.25 s wall-clock advance on the first ack of every walk throws Niko 1 m ahead and then 0.8 m
  back.
- The server moves only in whole 0.2 m steps, so releasing a key snaps him back.
- Frame-quantised sends cause jitter on every ack.
- The client ignores `walk_cost`.

The fix makes each input a finished step `{dx, dy, dt}` and predicts as server position + replay
of unacked steps + the current partial step. It mirrors `walk_cost` and flushes the partial step
before any action. The project moves to `v0.5.1`.
