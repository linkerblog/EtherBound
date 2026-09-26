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
      tile walked; now that Extras walk it multiplies by their number. Decide on pruning or compaction
      before the Phase 2 block with its ~20 Extras. It ties into memory compaction (`utils/VISION.md`
      [Sec. 8], Life cycle). Deferred by `done/Dev-005.md` [Sec. 8].
- [ ] **NPC construction.** Leaning (23/09/2026): the three questions go to three brains. *Why*
      build comes from utility (Extras, organizations) or the LLM (Agents). *What* to build: the
      LLM or a table writes a building program (use, rooms, floors, material, budget) and a
      seeded code generator turns it into tiles that obey `utils/VISION.md` [Sec. 5]; Jev may
      only pick among generated candidates, and no model places tiles. *How*: a plan of ops
      (`buy`, `take`, `put`, `dig`, `build`) posted as a task or contract, with bought and carried
      materials, ownership or a permit, and progress as an activity. Construction far from Niko
      advances per day at lower detail. The same generator could answer City authoring. Needs
      needs, organizations, tasks and ownership first; Phase 4.
- [ ] **Recipes and supply.** Leaning (23/09/2026): Zomboid-style items stay on the Matter
      primitive. `cook` and `craft` read recipes as data rows (inputs, tools, station, skill,
      minutes, outputs). Inputs, tools and stations are asked for by tag or component
      (`flour`, `tool.cut`, `heat.oven`), never by a named kind, so any object with the property
      serves. An organization's stock is a need: without flour the recipe fails, no pizzas means
      no delivery job, and the organization posts a supply task while local prices move. Open:
      stock far from Niko is kept as numbers and becomes physical objects only when relevant
      (LOD), and the first tag list is kept short. Lands with the Phase 2 pizzeria.

- [ ] **Jev runtime after Dev-025.** TypeSafe `choice` (Jev) must be reachable from the C# sim:
      a .NET port, an HTTP endpoint, or a small sidecar process. Settle before Phase 3.

## Planned work

- [ ] **Dev-025 C# sim and Godot client** (`docs/done/Dev-025.md`): port to a deterministic C# simulation
      and a Godot 4 .NET client with a 3D orthographic pixel-art camera, in seven stages proven by
      goldens from Python. Approved 25/09/2026; all seven stages (0-6) are done: `server/`, `web/`
      and `launcher/` are archived to `legacy-python-web-stack.zip` and removed, docs are retargeted
      to the C#/Godot stack, and Dev-014/016/021/023/024 are deprecated (below). The 26/09/2026
      Release benchmark passes the 1,000-Extra x10 gate at 64.864 ms average over 100 measured
      ticks; the Python server's own pre-cutover baseline, measured once immediately before deletion
      (`server/scripts/bench_extras.py`, archived with it), averaged 5,645.461 ms/tick at the same
      1,000 Extras — the C# sim is ~87× faster per tick. Still open: the manual parity walk and
      Windows export (below).
- [ ] **Infra03 CI on GitHub Actions**: run the checks on push now that the remote exists. The
      number was earmarked as Infra02 by `Infra01`; Infra02 became the agent context diet. No doc yet.

**Deprecated at cut-over** (`docs/done/Dev-025.md` [Sec. 3.7]; the files themselves are removed from `docs/`,
git keeps their history): Dev-014 (AI furniture as a primitive spec), Dev-016 (context menu
polish), Dev-021 (wall lines and heights), Dev-023 (server kernel refactor), Dev-024 (baked AO).
None needs a follow-up Dev for its *rendering* idea — real 3D geometry, real-time SSAO/shadows and
the already-built radial/context menu (Dev-025 stages 4-5) absorbed all of them. One surviving idea
does not: Dev-021 also motivated a future `build` op (players/NPCs constructing walls), which is a
gameplay change Dev-025 explicitly excluded from its port-only scope and still needs its own doc.

## Deferred work

- [ ] **Input replay.** The event log stores outcomes, not the 20 Hz inputs, so a run cannot
      yet be replayed exactly (`utils/VISION.md` [Sec. 11]).
- [ ] **Actors pass through each other.** Extras and Niko collide with terrain, never with bodies,
      so they can overlap on a tile. Body collision, and knowing who is in the way, is a later
      refinement (recorded by `done/Dev-018.md`).
- [ ] **Events to the client.** Only one event reaches the Godot client: the `HostActivityNotice`
      when one of Niko's activities finishes (`done/Dev-007.md`). Everything else stays sim-side
      until there is something to narrate (witnesses).
- [ ] **Activity progress is lost on interruption.** Walking away from a half-dug hole throws
      the progress away; partial progress that survives is a later refinement
      (`done/Dev-007.md` [Sec. 1]).

## Manual checks not run

Every manual acceptance that targeted the Phaser/React web client or `EtherBound.exe` launcher
(Dev-003, Dev-006, Dev-008 through Dev-012's GUI/BitCanvas/save-migration items, Dev-015, Dev-018's
GUI acceptance, Dev-019, Fix03 through Fix11, Fix13 through Fix16, Infra02's launcher-port example)
is dropped: that client and launcher no longer exist, replaced by Dev-025's Godot client. Their
docs stay in `docs/done/` as a record of what was built and verified at the time; nothing here
carries the check forward. Dev-025 itself tracks what the new client still needs approved
(`docs/done/Dev-025.md` [Sec. 6]): the manual parity walk in Godot and the clean-folder Windows export.

- [ ] **Dev-025 parity walk and Windows export** (`docs/done/Dev-025.md` [Sec. 6] acceptance 7, 9): manual,
      because both are visual/hands-on and were not run in this change — the seed-7 spawn, the lab
      bays, building cutaway and stubs, dig/push/throw/break a wall, both menus, `NEW`, `MAP` in a
      live Godot session; and a Windows export from a clean folder (new game, save, quit, reopen).

The Dev-005 GUI acceptance passed; details are recorded in `docs/done/Fix02.md`.

---

## TL;DR

Six design questions (abilities, city authoring, carry-over, event log retention, NPC
construction, recipes and supply); the op list is settled by `done/Dev-007.md` and the Matter
primitive by `done/Dev-012.md`. Dev-025 (all seven stages done) replaced the Python/FastAPI server
and Phaser/React client with a deterministic C# sim (~87× faster per tick at 1,000 Extras) and a
Godot 4 client; `legacy-python-web-stack.zip` keeps the old source, and every manual acceptance
that targeted it is dropped rather than carried forward. Dev-014/016/021/023/024 are deprecated: their
rendering ideas were absorbed by the port itself, except a future `build` op for wall construction.
Two deferred items remain (input replay, events to the client) plus lost activity progress on
interruption and actor pass-through. Dev-025's own manual gates (parity walk, Windows export) are
open above; Dev-007 was fully accepted on 22/09/2026.
