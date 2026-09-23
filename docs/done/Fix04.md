# Fix04: Fix03 review (event lines dropped, step drift below 20 fps)

[Bugfix] [Logging] [Prediction]

Review of Fix03 as implemented in `v0.5.1` (`docs/done/Fix03.md`). The code follows the plan: timed
steps `{dx, dy, dt}`, replay prediction, `walk_cost` in the client, a flush before actions and
pauses. Every check passes: 60 server tests, ruff, format, pyright, stable generated types and the
web build. Two defects remain. The first makes Fix03's acceptance 6 fail today.

## 1. Findings

### F1: Event lines never reach `server.log`
The live save holds more than 500 `actor.moved` events, but `logs/server.log` has no `event …`
line. `log_event` in `server/src/etherbound/app.py` writes to the `etherbound.events` logger,
which has no handler of its own. Nobody configures the app's loggers. Uvicorn configures only
`uvicorn*`, and Alembic's `fileConfig` (`server/alembic.ini`) leaves root at `WARN`. The effective
level is therefore WARNING, and every INFO record is dropped. Reproduced under uvicorn's
`LOGGING_CONFIG`: `info()` prints nothing, while `warning()` prints.

The test `test_websocket_input_duration_validation_and_event_logging` (`server/tests/test_api.py`
line 53) hides this. It monkeypatches `logger.info` to collect the lines, so it proves that
`log_event` calls `info`, never that anything is printed.

### F2: Below 20 fps the prediction drifts and the last step is lost
`MapScene.update` (`web/src/game/MapScene.ts` line 184) sends at most one 50 ms step per frame.
When a frame lasts longer than 50 ms (frames are capped at 100 ms), `inputElapsed` grows every
frame and never comes back down. At 15 fps it grows by about 17 ms per frame.

After 3 s of walking, the rendered partial step puts the prediction about 3 m ahead, while the
server has moved only 50 ms per frame. On release, `flushPartialStep` sends `dt ≈ 0.75`.
`InputMessage` requires `dt ≤ 0.1`, so the server answers with an `error` that carries no
sequence and no ack. The client ignores it and the walked time is lost. The step stays in
`pending` until the next ack, so `idle()` is false and the tick resync does not run. When Niko
walks again, he snaps back about 3 m.

## 2. Decisions to approve

| Topic | Decision | Why |
|---|---|---|
| App logging | `create_app` gives the `etherbound` logger its own `StreamHandler` on `sys.stderr`, at `INFO`, with `propagate = False` and uvicorn's formatter (`DefaultFormatter("%(levelprefix)s %(message)s", use_colors=False)`). The call is idempotent, so the handler is added once however many times `create_app` runs | The app's logs no longer depend on whoever configured root last (uvicorn, Alembic, pytest). The launcher already captures stderr into `server.log`. The event-bus failure log (`events/bus.py`, same logger) benefits too |
| Step cadence | `if (inputElapsed >= INPUT_INTERVAL)` becomes `while`. With the 100 ms frame cap that is at most two steps per frame, and the remainder is always under 50 ms | Server time then equals client time at any frame rate, and the final partial step is always a valid `dt`. The client can no longer produce an input the server rejects |
| Rejected input | No change. An invalid `input` keeps answering `error` without an ack | After the `while`, the browser never sends an invalid `dt`. Acking rejected inputs is a protocol change with no current caller |

## 3. Changes

### 3.1 `server.app`
- `app.py`: add `configure_logging()`:
  - Get `logging.getLogger("etherbound")`.
  - If it has no handler tagged `_etherbound` (an attribute set on the handler), add a
    `StreamHandler(sys.stderr)` with the formatter above and set that tag.
  - Set the logger to `INFO` and `propagate = False`.
- Call it at the top of `create_app`. `log_event` is unchanged.

### 3.2 `web.game`
- `MapScene.update`: `while (this.inputElapsed >= INPUT_INTERVAL) { send a 0.05 s step;
  inputElapsed -= INPUT_INTERVAL; }`. `flushPartialStep` is unchanged; its `dt` is now always
  below 0.05.

### 3.3 Docs
- `CONTEXT.md`, measured pitfalls:
  - "Moves are logged by the event subscriber": say the `etherbound` logger has its own INFO
    handler because root is `WARN` after Alembic's `fileConfig`.
  - "Held keys repeat": steps are sent in a loop, so a slow frame sends two.
- `PENDING.md`: replace this doc's entry once it is done. Fix03's acceptance runs after Fix04.

## 4. Modules and versions

| Module | Version |
|---|---|
| server.app | v0.0.6 → v0.0.7 |
| web.game | v0.1.1 → v0.1.2 |

Fix04 alone would move the overall project from `v0.5.3` to `v0.5.4`. Since this prerequisite lands
with Dev-008, the combined feature release is `v0.6.0`.

## 5. What must not break

- The uvicorn access and error lines in `server.log` keep their format. Nothing is printed twice:
  that is why `propagate = False`.
- Tests that create several apps do not stack handlers.
- At 60 fps and above, nothing changes: the loop runs at most once per frame.
- Fix03's flush before actions and pauses.

## 6. Tests and checks

Server, `test_api.py`:
1. Replace the `monkeypatch` of `logger.info` in
   `test_websocket_input_duration_validation_and_event_logging`. Inside the `TestClient` context,
   so after the migrations and Alembic's `fileConfig`:
   - Take the tagged handler from `logging.getLogger("etherbound").handlers`.
   - `handler.setStream(io.StringIO())`, walk, and assert that the buffer holds an
     `event … actor.moved niko` line and no `clock.ticked` line.
   - Restore the stream.
2. `create_app` called twice leaves exactly one tagged handler.

Checks: server pytest, ruff, format, pyright; the web build.

Manual acceptance with `EtherBound.exe`:
1. Walk for a few seconds. `logs/server.log` shows `INFO:     event <seq> actor.moved niko {…}`
   lines, and uvicorn's lines look as before.
2. In Chromium DevTools, Performance, set CPU throttling to 20× so the frame rate drops below
   20 fps. Walk for 3 s and release. `POS` matches `GET /api/game/state` within 0.05 m, and the
   next walk starts with no snap.
3. Then run Fix03's acceptance 1–6 (`docs/done/Fix03.md` [Sec. 6]).

## 7. Todo

### Decisions
- [x] Approve [Sec. 2]

### Code
- [x] `configure_logging()` in `create_app` [Sec. 3.1]
- [x] Step loop in `MapScene.update` [Sec. 3.2]

### Checks
- [x] Server tests 1–2 [Sec. 6], server checks and web build
- [ ] Manual acceptance 1–3 [Sec. 6]

### Closing
- [x] `CONTEXT.md` and `PENDING.md` [Sec. 3.3], `docs/utils/VERSION.md` [Sec. 4]
- [x] Notion: Work Report for the date and a Dev Blog page
- [x] Move this doc to `docs/done/`

## 8. Out of scope

- A logging config file or log levels in `Settings`.
- Acking rejected inputs.
- Rotating or filtering event lines (one per tile crossed is acceptable for now).

---

## TL;DR

Fix03 is faithful, but two things break it:
- **Event lines are dropped.** The `etherbound.events` logger has no handler and its effective
  level is WARNING, and the test hides this with a monkeypatch. The fix gives the `etherbound`
  logger its own INFO handler on stderr, added only once, and makes the test read the handler's
  real output.
- **Walking drifts below 20 fps.** Only one step is sent per frame, so the remainder grows and
  the final step's `dt` passes 0.1: the server rejects it, and Niko snaps back meters. The fix
  sends steps in a `while` loop.

Two modules get a bump, and the project moves to `v0.5.2`.
