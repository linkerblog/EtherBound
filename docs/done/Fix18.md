# Fix18: Smooth movement at any frame rate

[Bugfix] [Rendering] [Input] [Review]

Walking feels laggy at a steady 75 fps. The frame rate is not the problem: Niko's position changes
only 20 times a second, and the client's smoothing does not bridge the gap. It snaps to each new
position in about one frame, then stands still until the next one, so the camera and the whole
world move in stop-and-go steps. This doc replaces that smoothing with timed interpolation. It also
cuts the delay before the first step and the hitch when crossing a chunk border.

Reference: `docs/done/Dev-025.md` (Movement and Pixels rows of [Sec. 1]); `docs/utils/VISION.md`
[Sec. 4] (the client interpolates, no prediction); `docs/utils/PITFALLS.md` [Sec. 3] (movement and
the host channel); `CONTEXT.md` (host contract).

## 0. Findings (read from the v3.0.0 code plus the uncommitted working tree, 26/09/2026)

| # | Where | Finding |
|---|---|---|
| F1 | `WorldClient.SendMovement` | One `Move` every `1/20` s. `MoveOp.WalkingSpeed = 4.0` (`Engine/Ops/BodyOps.cs`), so each step moves Niko 0.2 m: about 9 art px, or 18 screen px at x2 |
| F2 | `WorldClient._Process` | Actors chase their target with `MoveToward(target, delta * 12f)`. That is 12 m/s, three times the walking speed. At 75 fps a frame covers 0.16 m, so a 0.2 m step is done in about 1.25 frames, then Niko stands still for about 2.5 frames. The result is a stop-and-go cycle 20 times a second |
| F3 | `WorldClient.FollowPlayer` | The camera follows the displayed capsule, so F2 moves the whole screen, not only Niko |
| F4 | Frame cadence | 75 / 20 = 3.75 frames per step. Steps go out every 3 or 4 frames (40 or 53 ms), which makes the rhythm uneven as well |
| F5 | `SendMovement` | `_moveAccumulator` starts at 0, so the first `Move` waits 50 ms after the key is pressed. The sim wake, `Publish` and the next `_Process` add about one more frame, so movement starts 65 to 80 ms after the key goes down |
| F6 | Extras | Extras move once per clock tick (1 s at x1) up to 4 m. At 12 m/s they slide for 0.33 s, then freeze for 0.67 s |
| F7 | `WorldClient.RebuildChunks` | Any chunk-set change frees and remeshes **all** chunks (a 5×5 set) on the main thread. Crossing a chunk border changes the set, so every crossing remeshes 25 chunks. Measured 26/09/2026 (ExportRelease build, 6 runs each, fresh save): **75–99 ms** on `test` (25 chunks, 61 meshes) and **47–57 ms** on `lab` (16 chunks, 28 meshes). `WorldDump.FromFrame` takes 0.1 ms, so meshing is the whole cost: 4 to 7 dropped frames per crossing |
| F8 | `project.godot` (uncommitted) | `vsync_mode=1` together with `run/max_fps=75`. On a monitor that is not exactly 75 Hz, the frame cap and vsync disagree and frame pacing becomes uneven |

## 1. Decisions

| Topic | Decision | Why |
|---|---|---|
| Model | Timed interpolation, no prediction. Each new sim position starts a linear segment from where the actor is **drawn now** to the new target, lasting a known duration | `VISION.md` [Sec. 4]: the client interpolates between snapshots and never predicts. Starting from the drawn position means a segment never makes the actor jump |
| Player duration | `1 / MoveHz` (50 ms) | That is how often a `Move` step arrives. At the speed the sim moves Niko, the segment ends just as the next step arrives |
| Extras duration | `1 / frame.Speed` s, the clock tick at the current speed (`SimClock.Interval`, with `TimeScale = 1`) | Extras move once per tick, so each segment lasts exactly one tick |
| Snap | If the new target is farther than `2 × WalkingSpeed × duration + 0.5 m` (0.9 m for the player, 8.5 m for an Extra at x1), the actor jumps straight to it | This covers spawns, new game and climbs without a long glide, while any legitimate step still interpolates |
| Early step | When a step arrives before the segment ends (40 ms spacing), the new segment starts from the drawn position | Movement stays continuous. The small speed-up is invisible |
| `MoveHz` | New `public const int MoveHz = 20` on `SimulationHost`. `TryMove`'s default and the client's `moveInterval` read it | There is one source for the rate. Today `1.0 / 20` is written in two places |
| Logic location | The pure segment math lives in `sim/EtherBound.Host/ActorMotion.cs`. It uses doubles only and never touches the sim | `sim.Tests` can test the host without Godot (Architecture: no Godot in host). It is presentational only and never a source of truth (`PITFALLS.md` [Sec. 3]) |
| First step | When the WASD direction goes from zero to non-zero, `_moveAccumulator = Min(moveInterval, sinceLastStep)`. The client also tracks `sinceLastStep`, the time since the last step it sent | Movement starts on the key-down frame. Tapping faster than 20 Hz never gains extra steps, so tapping is never faster than holding |
| Remesh | A frame's chunk diff rebuilds only chunks that are new or have a changed `HostChunk` reference, plus the rendered 4-neighbours `(cx±1, cy)`, `(cx, cy±1)` of every new, changed or removed chunk. Removed chunks free their meshes. If the set of roof bands changes, everything is rebuilt | `ChunkMesher` reads one cell past the chunk on every side: south and east faces read `y+1`/`x+1`, and wall corners read `x-1`/`y-1`. Roof bands are global ("any chunk flags a roof in it"), so a new roof flag can change walls anywhere; `ChunkMesher.RoofBands(world)` becomes public so the client can compare. A border crossing then builds 10 chunks (5 new plus 5 neighbours) instead of 25 |
| Remesh gate | First, measure the current full rebuild with a `Stopwatch` around `RebuildChunks`. If the worst case stays under 13 ms, skip the incremental remesh and record the numbers in [Sec. 0] F7 | Only fix a hitch that the numbers show exists. Result: 47–99 ms (F7), so the incremental remesh stays |
| Frame cap | Remove `run/max_fps=75` and keep `vsync_mode=1` | Vsync already paces frames to the monitor. A cap that differs from the refresh rate adds judder (F8). The line comes from the user's uncommitted change, so the user confirms this at approval |

## 2. Changes

**sim.host** (`sim/EtherBound.Host/`)
- `SimulationHost.MoveHz = 20`. `TryMove(dx, dy, deltaSeconds = 1.0 / MoveHz)`.
- New `ActorMotion` (sealed class, pure):
  - It holds `From`, `To` (`(double X, double Y, double Z)`), `StartedAt` and `Duration`.
  - `static ActorMotion At(pos, now)`: a motionless actor at `pos`.
  - `Sample(double now)`: `From.Lerp(To, clamp((now - StartedAt) / Duration, 0, 1))`.
  - `Retarget(pos, double duration, double maxStep, double now)`. If `pos == To`, nothing
    changes. If the distance from `Sample(now)` to `pos` is more than `maxStep`, the actor snaps
    to `pos`. Otherwise it sets `From = Sample(now)`, `To = pos`, `StartedAt = now` and
    `Duration = duration`.
  - `static double MaxStep(double duration) => 2 * MoveOp.WalkingSpeed * duration + 0.5`.

**game.app** (`game/app/WorldClient.cs`, `game/project.godot`)
- `_actorTargets` becomes `Dictionary<string, ActorMotion>`. `UpdateActors` calls `Retarget` with
  the actor's duration (player: `1.0 / SimulationHost.MoveHz`, others: `1.0 / frame.Speed`). A
  newly created actor starts with `ActorMotion.At`.
- `_Process` sets `actor.Position = Sample(now)`, converted to Godot `Vector3`. `now` is
  `Time.GetTicksUsec() / 1e6`, read once per frame. The `MoveToward(..., 12f)` line goes.
- `SendMovement` changes as in the First step row of [Sec. 1]. `moveInterval` comes from `MoveHz`.
- `ApplyFrame`/`RebuildChunks` change as in the Remesh row of [Sec. 1]: `RebuildChunks` works out
  the keys to build, frees their old meshes and the meshes of removed chunks, and builds only
  those. `WorldClient` keeps the last roof-band set and rebuilds everything when it changes.

**game.render** (`game/spike/ChunkMesher.cs`): the roof-band scan inside `IsRoofLevel` moves to a
public `static HashSet<int> RoofBands(WorldDump world)`, and `IsRoofLevel` calls it once per mesher,
as it does today.
- `project.godot`: remove `run/max_fps=75`.

No change to the sim, to `WorldFrame`, to the command channel, to the meshes the mesher builds, or
to `PixelView`. The camera
already follows the drawn capsule, so it becomes smooth together with it.

## 3. What must not break

- The client never simulates. `ActorMotion` only decides where to *draw* a position the sim has
  already committed. The collision, the standing rule and the step length stay sim-side.
- `SendMovement` still never skips or doubles a step on a frame hitch (`PITFALLS.md` [Sec. 3]).
  Holding a key produces exactly `MoveHz` steps per second, and tapping never produces more.
- The HUD focus, view switch and scripted runs still block movement: `CanMoveWorld`, `_scripted`
  and `ViewChanged` reset the accumulator.
- `--shots` output is unchanged. Actors spawn at their target, and shots are taken with no motion.
- `PlayerScreenPosition`, the radial anchor and the cutaway use the drawn position, as they do
  today.
- Every chunk in the frame is meshed, and none is left over after a new game or a border crossing.
  A chunk's border faces and wall corners match what a full rebuild would draw.

## 4. Acceptance

### Automated

1. `sim/EtherBound.Sim.Tests/ActorMotionRules.cs`:
   - `Sample` is `From` at `StartedAt`, `To` at `StartedAt + Duration` and after it, and linear in
     between.
   - A `Retarget` halfway through starts from the halfway point, with no jump.
   - A target farther than `MaxStep` snaps. Re-sending the same target is a no-op.
   - A 20 Hz stream of 0.2 m steps sampled at 75 fps moves at 4 m/s ± 25 % on every frame, and
     never stops for a whole frame while steps keep arriving. Today's `MoveToward(12)` fails this
     check (F2).
2. `sim/EtherBound.Sim.Tests/HostRules.cs`: `TryMove`'s default delta equals `1.0 / MoveHz`.
3. `npm run check` (includes `check:game` and `check:sim`).

### Manual

4. Manual (lab world, x2, 75 Hz and one other refresh rate if available): hold WASD in all 8
   directions, then tap. Niko and the camera glide without stepping, and the first step shows on
   the key-down frame. Manual because it is about feel and frame pacing, which a headless test
   cannot see.
5. Manual: Extras at x1 and x10 walk continuously, with no slide-and-freeze.
6. Manual, with numbers: the `RebuildChunks` timing before and after, written into F7 (see the
   Remesh gate row of [Sec. 1]). Manual because it needs the GPU client.

## 5. Docs to update in the same change

- `docs/utils/PITFALLS.md` [Sec. 3]: describe the interpolation (`ActorMotion`, duration per
  actor, the snap rule) and the first-step rule.
- `CONTEXT.md`, host contract: `MoveHz` is a `SimulationHost` constant. In the module table,
  sim.host also holds the `ActorMotion` helper.
- `docs/done/Dev-025.md`, Movement row: "if stage 3 feels laggy, raise `MoveHz`" was answered by
  Fix18 with interpolation, not a higher rate.
- `docs/utils/VERSION.md`, the Notion Systems Index, the Dev Blog entry and the daily Work Report.

`docs/utils/VISION.md` needs no change. It already says the client interpolates and never
predicts.

## 6. Modules and versions

| Module | Change | Version |
|---|---|---|
| sim.host | `MoveHz`, `ActorMotion` | v0.0.3 → v0.0.4 |
| game.app | Interpolation, first step, incremental remesh, frame cap | v0.0.6 → v0.0.7 |
| game.render | `ChunkMesher.RoofBands` made public | v0.0.4 → v0.0.5 |

The versions come from the uncommitted working tree of `docs/utils/VERSION.md`, where `game.app`
is already v0.0.6. If that change and this fix go out in one commit, `game.app` is bumped only
once in total (`check-versions` allows +0.0.1 per commit). The overall version is assigned at
release by `docs/utils/COMMITS.md` [Sec. 2].

## 7. Todo

- [x] Measure the current `RebuildChunks` cost and record it in F7 (Remesh gate)
- [x] `SimulationHost.MoveHz`; `TryMove` default
- [x] `ActorMotion` and `ActorMotionRules.cs`; the `MoveHz` case in `HostRules.cs`
- [x] `WorldClient`: `ActorMotion` per actor, sampling in `_Process`, `MoveToward` removed
- [x] `WorldClient.SendMovement`: first step on key-down, tap guard
- [x] `WorldClient`: incremental remesh; `ChunkMesher.RoofBands`
- [x] `project.godot`: remove `run/max_fps` (confirmed by the user at approval, 26/09/2026)
- [x] `npm run check`: versions, docs, 178 sim tests, game build (headless import skipped, no
      `GODOT_BIN`). `--shots` on the ExportRelease DLLs renders as before
- [x] Manual 6, run 26/09/2026 with a temporary hook (removed) that walked Niko across borders and
      compared every rebuilt mesh with a from-scratch build: 0 mismatches. A crossing costs
      **23–37 ms** (`test` 22.7–36.6, `lab` 28.7–32.7) against 75–99 ms before
- [ ] Manual 4–5 (feel): not run by the implementer, listed in `docs/PENDING.md`
- [x] Docs of [Sec. 5]: `PITFALLS.md` [Sec. 3] and [Sec. 5], `CONTEXT.md`, `Dev-025.md`,
      `VERSION.md`. Notion (Systems Index, Dev Blog, Work Report) goes with the release commit
- [x] Move this doc to `docs/done/`

## 8. Out of scope

- **Raising `MoveHz`.** A higher rate shortens the steps but does not remove the stop-and-go
  pattern, and each step costs a full `Publish`. Interpolation fixes the cause.
- **Client-side prediction.** `VISION.md` [Sec. 4] rules it out, and with no network there is no
  latency to hide.
- **Stepping movement on the sim thread from a held intent.** It would change the host contract,
  and the Windows wait granularity (about 15.6 ms) would make the cadence worse, not better.
- **Meshing off the main thread.** Manual 6 still shows 23–37 ms per crossing, 2 to 3 frames, so
  this is the follow-up, listed in `docs/PENDING.md`.

---

## TL;DR

Movement feels laggy at 75 fps because Niko only moves 20 times a second in 0.2 m steps, and the
client covers each step in about one frame at 12 m/s, then waits. The screen moves in stop-and-go
steps. Each actor now interpolates linearly from where it is drawn to the new sim position, over
the time until the next update arrives (50 ms for Niko, one clock tick for Extras), and snaps on
teleports. The first step goes out on key-down instead of 50 ms later. A chunk-border crossing
rebuilds only the changed chunks and their neighbours (10 instead of 25), since a full rebuild
measured 47–99 ms.
`run/max_fps=75` is removed so vsync paces the frames. There is no prediction and no sim change.
