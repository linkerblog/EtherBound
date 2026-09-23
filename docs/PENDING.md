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

- [ ] **Fix02 (Dev-005 review) closing.** The code, checks and launcher banner (`v0.4.2`) are
      done. The Notion Work Report for 22/09/2026 was updated under `16:18`. H3 (a loaded actor with a stale `h`
      frozen in its tile) is fixed and the user's save now loads correctly. Still open: rerun the
      Dev-005 GUI acceptance (H2) from item 2, then move the doc to `docs/done/`.

- [ ] **Fix04 (Fix03 review) awaiting approval.** Event lines never reach `server.log` (the
      `etherbound` logger has no handler, effective level WARNING), and below 20 fps the
      movement steps drift until the final `dt` is rejected. Plan in `Fix04.md`.

## Planned work

- [ ] **Dev-008 (isometric renderer) awaiting approval.** The camera moves to 2:1 isometric
      (VISION already updated); the web client swaps its renderer with placeholder art, a split
      painter's order, the roofed cutaway, front-wall stubs, height-aware picking and
      screen-relative WASD. Lands after Fix04. Plan in `Dev-008.md`.

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

- [ ] **Fix03 walking:** acceptance 1 (no jump at start or stop) was confirmed by the user on
      22/09/2026 ("walking feels smooth"), and 5 (releasing the keys after `DIG` does not
      cancel it) in the Dev-007 check. 3 was confirmed too: smooth at 4 m/s on grass and
      4.44 m/s on asphalt (4 / 0.9, the server's `walk_cost`). 2 was confirmed too: after stopping, the HUD read
      `X 125.46 · Y 144.40 · Z 0 (1.0 m)`, exactly the server position. 4 passed as well: paused, Niko can neither walk
      nor act, and x10 leaves walking at real-time speed (the clock speed only scales game
      minutes). Still open in `docs/done/Fix03.md` [Sec. 6]: 6 (event lines in `server.log`, which fails until Fix04).

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
list is settled by `done/Dev-007.md`. One planned doc awaiting approval (Dev-008, isometric
renderer). Two open fixes: Fix02's closing and Fix04 (event lines in `server.log`, step drift
below 20 fps) awaiting approval. Five
deferred items (RNG persistence, input replay, multiplayer input rate limit, events to the
client, lost activity progress). Four manual acceptances not run (Dev-004 zoom, Dev-003 browser,
Dev-006 new game, Fix03 walking); Dev-007 was fully accepted on 22/09/2026. Three loose ends (NikoStory Notion pages,
NikoStory's uncommitted changes, fractional-DPR blur).
