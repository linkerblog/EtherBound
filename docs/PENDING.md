# Pending

Open decisions and loose ends. When one is settled, record the decision where it belongs
(`utils/VISION.md` or the relevant `Dev-XYZ.md`) and delete it from here.

## Open design questions

- [ ] **Abilities.** How common they are among hybrids, and which ones exist.
- [ ] **City authoring.** Procedural from seed, hand-made in Tiled, or authored landmarks on a
      procedural base.
- [ ] **Carry-over from NikoStory.** Whether Halverton, the authored NPCs and the prompts come
      along. The roll formula already does (`utils/VISION.md` [Sec. 3]).
- [ ] **Event log retention.** The `event` table grows without limit. Today that is one row per
      tile walked; once Extras walk, it multiplies. Decide on pruning or compaction before the
      Phase 2 block with its ~20 Extras. It ties into memory compaction (`utils/VISION.md`
      [Sec. 8], Life cycle). Deferred by `done/Dev-005.md` [Sec. 8].

## Open fixes

- [ ] **Fix01 (Dev-002 review) is half done.** Blocking B1–B5 and High H1–H4 are fixed. Still
      open (`Fix01.md` [Sec. 4-6, 9]):
      - Levels are not indexed by `(cx, cy)`. A* from spawn to the roof took 3.3 s, so this
        must land before NPCs pathfind.
      - Headroom is off by one (it needs 2.5 m instead of 2 m).
      - Diagonals are refused on slopes, or can climb 1 m.
      - Walls on floorless tiles take their base from the band.
      - Ladders (`LEVEL_CLIMBABLE`) are unused.
      - The hub reads the database on every input instead of using `submit`'s result.
      - Dead code and the minor items of [Sec. 5].
      - The tests of [Sec. 6].
      - Closing: `CONTEXT.md` pitfalls, `VERSION.md`, the Dev-002 acceptance on seed 0, and
        Notion.

      Its [Sec. 1] decisions were applied (H1, H4) but never ticked.
- [ ] **Fix02 (Dev-005 review) closing.** The code, checks and launcher banner (`v0.4.2`) are
      done. The Notion Work Report for 22/09/2026 was updated under `16:18`. H3 (a loaded actor with a stale `h`
      frozen in its tile) is fixed and the user's save now loads correctly. Still open: rerun the
      Dev-005 GUI acceptance (H2) from item 2, then move the doc to `docs/done/`.

## Deferred work

- [ ] **RNG stream persistence.** `RNGStreams` restarts from the seed on every launch, so a
      loaded game does not continue its random sequence. Save each stream's state together with
      the first dice roll, in the character-sheet doc (`done/Dev-007.md` has no rolls). Deferred by `done/Dev-005.md` [Sec. 1].
- [ ] **Input replay.** The event log stores outcomes, not the 20 Hz inputs, so a run cannot
       yet be replayed exactly (`utils/VISION.md` [Sec. 11]). This depends on RNG persistence.
- [ ] **Input rate limit (multiplayer only).** Timed movement input is bounded per message, but
      the local single-player game has no rate limit. Add one only if multiplayer is introduced.
- [ ] **Events to the client.** Only one event reaches the WebSocket: the `activity` notice when
      one of Niko's activities finishes (`done/Dev-007.md`). Everything else stays server-side
      until there is something to narrate (witnesses).
- [ ] **Activity progress is lost on interruption.** Walking away from a half-dug hole throws
      the progress away; partial progress that survives is a later refinement
      (`done/Dev-007.md` [Sec. 1]).

## Manual checks not run

- [ ] **Dev-004 zoom:** acceptance 1–10 in Chromium and Firefox (`done/Dev-004.md` [Sec. 6]).
- [ ] **Dev-003 launcher:** `O` with the browser closed opens the game, and quitting the
      launcher leaves the browser open (`done/Dev-003.md` [Sec. 9]).
- [ ] **Dev-006 new game:** manual GUI acceptance 1–10, including same-seed regeneration and
      reconnect redraw (`done/Dev-006.md` [Sec. 7]). The automated web build and server checks pass.

- [ ] **Dev-007 ops:** manual GUI acceptance 1–8 (`done/Dev-007.md` [Sec. 7]). The server
      tests (59), the WebSocket dig test and the web build pass, and the live server serves the
      new menu. Also confirm that walking moves Niko on the server (`GET /api/game/state` after a
      walk): until v0.5.0 the browser's inputs were all rejected, so every earlier GUI check of
      walking (Dev-004, Dev-005, Dev-006) only saw the prediction.
- [ ] **Fix03 walking:** run manual acceptance 1–6 in `docs/done/Fix03.md`; automated timed-input
      validation and event logging checks pass, but the Phaser GUI has no test runner.

The Dev-005 GUI acceptance is tracked in Fix02.

## Loose ends

- [ ] **Notion.** `Dev-018` and `Dev-019` from NikoStory still hang under the EtherBound page;
      delete them.
- [ ] **NikoStory working tree.** 11 modified files uncommitted in the old repo (`schema.ts`,
      `movement.ts`, `grid.ts`, `Dev-018.md`, ...). Commit them as the closing state or discard
      them.
- [ ] **Fractional-DPR canvas blur.** At Windows display scaling of 125% or 150%, the browser
      rescales the Phaser canvas unevenly at every integer zoom. A DPR-aware canvas is a separate
      rendering change; see `docs/done/Dev-004.md` [Sec. 3].

---

## TL;DR

Four design questions (abilities, city authoring, carry-over, event log retention); the op
list is settled by `done/Dev-007.md`. Two open fixes: Fix01's medium and minor items,
including the slow A* that blocks NPC pathfinding, and Fix02's closing (H3 blocking). Five
deferred items (RNG persistence, input replay, multiplayer input rate limit, events to the
client, lost activity progress). Five manual acceptances not run (Dev-004 zoom, Dev-003 browser,
Dev-006 new game, Dev-007 ops, Fix03 walking). Three loose ends (NikoStory Notion pages,
NikoStory's uncommitted changes, fractional-DPR blur).
