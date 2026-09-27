# Fix19: Constant-speed walking with a step playout buffer

[Bugfix] [Rendering] [Input] [Persistence] [Review]

Fix18 removed the stop-and-go chase, but walking still does not feel fluid. A frame-by-frame trace
of the real client shows why. Niko's drawn speed still swings from frame to frame, and the sim's
positions arrive at irregular times, because every 0.2 m step waits on a synchronous disk commit.
This doc draws Niko on the steps' own schedule instead of on their arrival times, and moves the
save to WAL so that a step costs well under a millisecond.

Reference: `docs/done/Fix18.md` (`ActorMotion`, first step, remesh); `docs/utils/VISION.md`
[Sec. 4] (the client interpolates, no prediction); `docs/utils/PITFALLS.md` [Sec. 1] and [Sec. 3];
`CONTEXT.md` (host contract).

## 0. Findings (measured 26/09/2026 on the Fix18 working tree)

Method: an ExportRelease copy of the client in the scratchpad, `lab` world, fresh save, x2, a
75 Hz monitor with vsync. A temporary hook (removed) held D for 6.5 s and logged every frame; a
second one timed the sim thread.

| # | Where | Finding |
|---|---|---|
| F1 | Frame pacing | Fine. Every frame lands at 13.3 ± 0.3 ms, so the stutter is not a rendering problem |
| F2 | Niko's drawn speed | Per frame, as a ratio of 4 m/s: most frames 1.0, but 51 of about 580 frames at 0.76–0.78, 80 at 1.18–1.22, 11 at 1.3–2.0 and **6 at 0**. The screen scroll swings between 3 and 7 px per frame against an ideal 4.83 |
| F3 | Why F2, part 1 | `ActorMotion` starts a fixed 50 ms segment when a position **arrives**, and arrivals only happen on frame boundaries (every 3 or 4 frames at 75 Hz). The Fix18 test already allowed ±25 %. Replaying it gives the same beat: 0.75× once every 4 frames at 75 Hz, and a 0.21× near-stop once every 7 frames at 144 Hz. Only 60 Hz is clean |
| F4 | Why F2, part 2 | Positions arrive at irregular gaps: every 4 frames 86 times, every 3 frames 26 times, every 2 frames 13 times, every frame 3 times, and one gap of 9 frames (120 ms) |
| F5 | Sim thread | A single `Move` `Submit` costs **4.56 ms p50, 7.0 ms p95, 26.4 ms max**. Clock ticks cost 3.5–28.5 ms, and `Publish` costs 0.07 ms. So a step sent at a frame boundary often misses the next frame |
| F6 | Why F5 | `Database` opens SQLite with its defaults (rollback journal, `synchronous=FULL`). Every step is one write batch, and every batch commit fsyncs the save several times |
| F7 | WAL, measured | With `journal_mode=WAL` and `synchronous=NORMAL`, `Submit` drops to **0.74 ms p50, 1.9 ms p95** (13 ms max on the first, cold steps). Ticks fall to 0.4–6 ms after warm-up |
| F8 | Pixels | At x2, 4 m/s is 4.83 screen px per frame, so the scroll alternates 4 and 5 px even with perfect motion. That is the integer pixel grid, and this doc does not change it |
| F9 | After Fix19 (same method, `--trace-walk`) | `lab`: **398 of 398 walking frames within 5 %** (0.97–1.03×), no stopped frames, and a scroll of 4 or 5 px only. `Submit` takes 0.71 ms p50 and 1.75 ms p95 (`test`: 0.84 and 2.28 ms). On the `test` spawn, D walks into an edge, and the sim's own slide zigzags ±0.2 m on each step (X velocity +0.79/−0.79). The playout draws that path faithfully, so it goes to `PENDING.md` as a `sim.engine` bug |

## 1. Decisions

| Topic | Decision | Why |
|---|---|---|
| Schedule, not arrival | Every step the client sends gets its **ideal time** `T_k`: the moment the accumulator crossed the interval (`now - leftover`), not the frame it went out on. The position after step `k` is drawn as reached at `T_k + 1/MoveHz`. The ideal times of consecutive held steps are exactly 50 ms apart, so equal steps give exactly constant speed | This removes F3 at every refresh rate. It is still interpolation between committed positions, with no prediction (`VISION.md` [Sec. 4]) |
| Matching steps to frames | `WorldFrame` gains `long MovesApplied`: the number of `Move` commands the host has applied since it started. The client counts the steps `TryMove` accepted, so frame `m` holds the position after the client's step `m` | Without it the client cannot tell which step a frame contains. It is one counter, and the sim is untouched |
| Several steps in one frame | One sample at `T_m + 1/MoveHz`. The segment from the previous sample spans several intervals of time and distance, so the speed stays the same | This absorbs F4's 1-frame and 2-frame gaps |
| Playout delay | The player is drawn at `r = now - D`, where `D` covers how late positions arrive: the largest lateness in the last second plus 4 ms, clamped to 10–100 ms. Lateness is measured from the ideal time of the **first** step a frame brings (`T_m - (steps - 1)/MoveHz`). With no recent history, `D` is two frames (`2 × delta`) plus 4 ms: about 31 ms at 75 Hz and 18 ms at 144 Hz | A sample must be in hand before `r` reaches the start of its segment, and with several steps in one frame that segment starts at the first of them. Measuring from the last step starved the playout (0.72× every 200 ms in the test) |
| Moving `D` | `r` never jumps. Each frame it advances by `delta × rate`, with `rate` in [0.98, 1.02], steering towards `now - D` | If `D` rose by a sudden jump, Niko would slide backwards. At ±10 % the speed visibly breathed while `D` settled, and ±2 % keeps every frame within 3 % |
| Starvation | If `r` reaches the newest sample (a stall such as F4's 120 ms), Niko holds there. There is no extrapolation. `D` then grows, and `rate` catches up | The client never draws a position the sim has not committed |
| Start of a walk | The first step adds an anchor sample: the drawn position at `T_first`. From idle this restarts the playout. Mid-playout it only adds a hold, and only when no step is still in flight (otherwise their samples come first and a hold would warp them). The first movement shows about `D` (30 ms) after key-down | Fix18's key-down step stays. It gives a little latency in exchange for constant speed |
| Moves outside WASD | If the player's position changes while `MovesApplied` stays the same (actions, climb, new game), or a sample is farther than `ActorMotion.MaxStep` per step, the buffer clears and `ActorMotion` takes over from the drawn position. The next scheduled step restarts the playout, so a walk does not stay on `ActorMotion` | Those moves have no step schedule |
| Extras | Unchanged: `ActorMotion` with a one-tick duration | Tick arrival jitter (a few ms) is under 1 % of a 1 s segment |
| Logic location | A new pure `StepPlayout` in `sim/EtherBound.Host/`, next to `ActorMotion` | It can be tested without Godot, like `ActorMotion` (Fix18 [Sec. 1]) |
| WAL | For a file save, `Database` sets `PRAGMA journal_mode=WAL` and `PRAGMA synchronous=NORMAL` right after opening. `:memory:` is untouched. The connection string gets `Pooling = false`, so `Dispose` really closes the file and SQLite checkpoints and removes `-wal` | F7: a step costs about 6× less. With NORMAL, a power cut can lose the last committed moments, but it never corrupts the save. A game crash loses nothing. Without pooling, the sidecar files do not outlive the session |
| Schema | No migration. The journal mode is a file setting, not schema, and any SQLite 3.7 or later opens a WAL save | `AGENTS.md` asks for migrations on model changes only |
| Diagnostics | `--trace-walk PATH` becomes a permanent client flag, like `--shots`. It walks east for 6 s after a 1.5 s settle, writes one CSV row per frame (time, delta, arrival, drawn position, camera, remainder, scale), then quits. `scripts/trace-walk.mjs` turns the CSV into the numbers of [Sec. 0] | Feel cannot be checked headless. This makes F2 and F4 repeatable numbers instead of an impression |

## 2. Changes

**sim.db** (`sim/EtherBound.Sim/Db/Database.cs`): for a file save, set `Pooling = false` on the
builder, and run the two pragmas after `Open()`.

**sim.host** (`sim/EtherBound.Host/`)
- `WorldFrame` gains `long MovesApplied`. `SimulationHost.Run` counts each `Move` it handles
  (accepted or blocked by the sim), and `Publish` stamps the count on the frame.
- New `StepPlayout` (sealed class, pure, doubles only):
  - `Sent(long index, double idealAt)`: records `T_index`.
  - `Applied(long movesApplied, (double X, double Y, double Z) position, double now) → bool`:
    adds the sample at `T_m + 1/MoveHz` and updates the lateness window. It returns `false`
    (the caller falls back to `ActorMotion`) when the move count did not advance but the
    position changed, or when the jump is over `MaxStep × steps`.
  - `Anchor(position, double at)`: the start-of-walk sample.
  - `Sample(double now, double delta) → (double X, double Y, double Z)?`: advances `r` as in
    [Sec. 1] and interpolates. It returns `null` when idle.
  - Constants: `MinDelay = 0.010`, `MaxDelay = 0.100`, `DelayMargin = 0.004`,
    `LatenessWindow = 1.0`, `RateLimit = 0.02`.

**game.app** (`game/app/WorldClient.cs`)
- `SendMovement` passes each accepted step to `StepPlayout.Sent` with its ideal time
  (`Now - _moveAccumulator` after the subtraction). The first step of a walk also calls `Anchor`.
- `UpdateActors`: for the player, `Applied(frame.MovesApplied, …)` is tried first, and
  `ActorMotion.Retarget` is used when it returns `false`.
- `_Process`: the player is drawn from `StepPlayout.Sample` when it is not `null`, otherwise from
  `ActorMotion`. When the playout goes idle, `ActorMotion` is reset to `At(last sample)`, so the two
  never disagree.
- `--trace-walk PATH` as in [Sec. 1].

**tooling**: `scripts/trace-walk.mjs PATH` prints the speed-ratio histogram, the scroll histogram
and the arrival gaps.

## 3. What must not break

- The client never simulates: `StepPlayout` only draws positions the sim has committed, a little
  later, and it never extrapolates.
- Fix18 holds: `SendMovement` never skips or doubles a step, tapping never beats holding, the
  first step leaves on key-down, and Extras interpolate over one tick.
- Niko never moves backwards on screen. A stall shows as a hold, never as a rewind.
- Saves: an existing save opens, plays and reopens (`SaveCompatibility.cs`), and no `-wal` or
  `-shm` file is left behind after `Dispose`. `:memory:` worlds are unchanged.
- `--shots` output is unchanged.

## 4. Acceptance

### Automated

1. `sim/EtherBound.Sim.Tests/StepPlayoutRules.cs`:
   - Held walking replayed at 60, 75 and 144 fps, with each arrival late by a random 0–2 frames
     plus 1 ms (seeded `Rng` stream): after the first 100 ms, every frame's speed is within 3 % of
     4 m/s.
   - Several steps in one frame keep the speed. A 120 ms stall gives a hold at the last sample,
     never a backward step, and the speed is back within 3 % in under 1 s.
   - A position change with no new moves returns `false`. So does a jump over `MaxStep × steps`.
   - The anchor makes the first segment start from the drawn position.
2. `sim/EtherBound.Sim.Tests/HostRules.cs`: `MovesApplied` rises by one per `TryMove`, including a
   blocked one.
3. `sim/EtherBound.Sim.Tests/SaveCompatibility.cs`: a file save reports `journal_mode = wal` and
   `synchronous = 1`, and after `Dispose` no `-wal` or `-shm` file remains. A `:memory:` database
   reports `memory`.
4. `npm run check`.

### Manual

5. Manual, with numbers: `--trace-walk` on `lab` at x2 through `scripts/trace-walk.mjs`, on the
   ExportRelease build. At least 98 % of walking frames are within 0.95–1.05 of 4 m/s, and none are
   at 0 except around a chunk-border remesh (still 23–37 ms, `PENDING.md`). The `Submit` p95 is
   under 2.5 ms (a temporary host timer, as in F5). Manual because it needs the GPU client.
6. Manual, feel: the user walks in all 8 directions, taps, and crosses a border. Manual because
   feel is the goal. This replaces the Fix18 feel item in `docs/PENDING.md`.

## 5. Docs to update in the same change

- `docs/utils/PITFALLS.md` [Sec. 3]: draw Niko on the step schedule (`MovesApplied`, ideal times,
  `D`, no rewinds), and use `--trace-walk` to check. [Sec. 1]: saves are WAL, so close the client
  before copying one, and a leftover `-wal` means a session did not close.
- `CONTEXT.md`: `WorldFrame.MovesApplied` in the host contract, the WAL setting in the persistence
  notes, and `--trace-walk` in the commands.
- `docs/PENDING.md`: the Fix18 feel item becomes Fix19 manual 6.
- `docs/utils/VERSION.md`, the Notion Systems Index, the Dev Blog entry and the daily Work Report.

## 6. Modules and versions

| Module | Change | Version |
|---|---|---|
| sim.db | WAL, `synchronous=NORMAL`, no pooling for files | v0.0.2 → v0.0.3 |
| sim.host | `MovesApplied`, `StepPlayout` | v0.0.4 (see below) |
| game.app | Player playout, `--trace-walk` | v0.0.7 (see below) |
| tooling | `scripts/trace-walk.mjs` | v0.0.20 (see below) |

Fix18 and the user's own pending changes are not committed yet, and `check-versions` allows
+0.0.1 per module per commit against `HEAD`. So in one commit `sim.host` stays v0.0.4 and
`tooling` stays v0.0.20, both already bumped in the working tree. `game.app` is v0.0.7 in the
working tree against v0.0.4 at `HEAD`, because the user's change and Fix18 each bumped it. Set it
to v0.0.5 if everything goes in one commit. The overall version follows `docs/utils/COMMITS.md` [Sec. 2].

## 7. Todo

- [x] `Database`: pragmas and `Pooling = false`; `SaveCompatibility.cs` cases. `ExtrasBrainRules.cs`
      copied a save while it was open, so it now copies the `-wal` too
- [x] `WorldFrame.MovesApplied` and the count in `SimulationHost`; `HostRules.cs` case
- [x] `StepPlayout` and `StepPlayoutRules.cs` (7 cases)
- [x] `WorldClient`: ideal times, anchor, playout for the player, `ActorMotion` fallback
- [x] `--trace-walk` and `scripts/trace-walk.mjs`
- [x] `npm run check`: 189 sim tests, game build. Manual 5 run on 26/09/2026, results in F9
- [ ] Manual 6 (feel): for the user, in `docs/PENDING.md`
- [x] Docs of [Sec. 5]. Notion goes with the release commit
- [x] Move this doc to `docs/done/`

## 8. Out of scope

- **Sub-pixel scrolling (F8).** At x2 the view moves in whole screen pixels by design
  (`PixelView`, Dev-025 Pixels row), so 4/5 px alternation stays.
- **Meshing off the main thread.** Border crossings still cost 23–37 ms (`PENDING.md`). It is the
  largest remaining hitch after this doc and gets its own.
- **The edge-slide zigzag (F9).** A `sim.engine` change against the parity goldens, in `PENDING.md`.
- **Batching the event log.** One write batch per step stays. Grouping steps would change what the
  log records per `Submit`.
- **Prediction** stays ruled out by `VISION.md` [Sec. 4].

---

## TL;DR

A per-frame trace shows the stutter is not frame pacing (13.3 ± 0.3 ms). There are two causes.
First, Fix18 starts each 50 ms segment when a position arrives, and arrivals follow frame
boundaries, so Niko's speed beats between 0.76× and 1.35× (worse at 144 Hz). Second, every step
fsyncs the save (4.6 ms p50, up to 26 ms), so arrivals come 1, 2, 3, 4 or 9 frames apart. The
fix: the host echoes `MovesApplied`, and the client draws Niko on each step's ideal 50 ms schedule,
about 30 ms behind, never rewinding and never predicting. The save moves to WAL with
`synchronous=NORMAL` (0.71 ms p50 per step). Measured after the change: every walking frame on
`lab` is within 5 % of walking speed, against 0.76–2.0× before. A permanent `--trace-walk` makes
smoothness a number.
