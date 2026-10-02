# Pending

Open decisions and loose ends. When one is settled, record the decision where it belongs
(`utils/VISION.md` or the relevant `Dev-XYZ.md`) and delete it from here.

## Open design questions

- [ ] **Abilities.** How common they are among hybrids, and which ones exist.
- [ ] **City authoring.** `Dev-010` settled the terrain (procedural from the seed, streamed per chunk).
      Open: roads, parcels and buildings on top of it, and any structure that crosses chunk borders
      (a site-based stamping contract). Procedural, hand-made, or authored landmarks on the base.
- [ ] **Carry-over from NikoStory.** Whether Halverton, the authored NPCs and the prompts come
      along. The roll formula already does (`utils/VISION.md` [Sec. 3]).
- [ ] **Event and replay-log retention.** The `event` table and `input_journal` grow without limit.
       Movement inputs run-length encode adjacent identical commands, but clock ticks and changed
       inputs still accumulate. Decide on pruning/checkpoint compaction
      before the Phase 2 block with its ~20 Extras. It ties into memory compaction (`utils/VISION.md`
      [Sec. 8], Life cycle). Earlier deferred in `Dev-005.md`, section 8, in
      `docs/deprecated/Docs-Archive.zip`.
- [ ] **NPC construction.** Leaning (23/09/2026): the three questions go to three brains. *Why*
      build comes from utility (Extras, organizations) or the LLM (Agents). *What* to build: the
      LLM or a table writes a building program (use, rooms, floors, material, budget) and a
      seeded code generator turns it into tiles that obey `utils/VISION.md` [Sec. 5]; Jev may
      only pick among generated candidates, and no model places tiles. *How*: a plan of ops
      (`buy`, `take`, `put`, `dig`, `build`) posted as a task or contract, with bought and carried
      materials, ownership or a permit, and progress as an activity. The `build` op itself is
      implemented in phase 1 (archive entry `Dev-036.md`); this item is who plans it. Construction
      far from Niko advances per day at lower detail. The same generator could answer City authoring.
      This requires needs, organizations, tasks and ownership first; Phase 4.
- [ ] **Recipes and supply.** Leaning (23/09/2026): Zomboid-style items stay on the Matter
      primitive. `cook` and `craft` read recipes as data rows (inputs, tools, station, skill,
      minutes, outputs). Inputs, tools and stations are asked for by tag or component
      (`flour`, `tool.cut`, `heat.oven`), never by a named kind, so any object with the property
      serves. An organization's stock is a need: without flour the recipe fails, no pizzas means
      no delivery job, and the organization posts a supply task while local prices move. Open:
      stock far from Niko is kept as numbers and becomes physical objects only when relevant
      (LOD), and the first tag list is kept short. Lands with the Phase 2 pizzeria.

- [ ] **Needs: what an unmet need costs.** `Dev-011` gives Extras hunger, thirst and rest with no
      consequence for ignoring them (no starvation, no mood), because there is no health yet. Decide with
      vitals and the body system; until then a starving Extra just stays hungry.
- [ ] **Needs for Niko.** Niko's `needs` is `NULL` and the `STATUS` panel stays hidden. Decide with vitals
      whether he gets the same three levels, and what they do to him (he cannot die, only be incapacitated).
- [ ] **Perception.** `WorldEngine.Percepts` (`Dev-011`) lists food and water within 12 m with no line of
      sight and no memory, the one approximation of "react only to what you perceived". The witnesses and
      knowledge Dev replaces its body; the callers (the Extras' utility step) must not change.
- [ ] **Needs: feed lines and supply.** Seeing another actor eat is perception, not `Dev-011`; shops,
      stock and prices are "Recipes and supply". Extras in a streaming world live on a starting kit.

- [ ] **Crowd tick cost has regressed since the cut-over.** `Bench -- --extras 1000` gives ~3.7 s per tick at
      the committed `v5.2.0` and today, against 64.9 ms recorded at Dev-025. 100 Extras take 57 ms, 200 take
      160 ms and 1,000 take 3,700 ms, so it is superlinear: every `move` builds a session whose
      `Session.Actors()` clones all actor rows. Find the commit between Dev-025 and `v5.2.0` that did it, and
      avoid the all-actor clone in `MoveOp`/`OthersOn` (a lookup by tile). Not a `Dev-011` regression: measured
      identical with and without needs.
- [ ] **Needs: worst-case crowd cost is +12 %.** With every Extra needy and no source (the steady state of the
      streaming world) a 200-Extra tick costs ~179 ms against ~160 without needs. The scan minute is a `Menu` read plus
      a 12 m tile scan per Extra; cache which chunks hold drinkable ground, or scan less often, to get under 10 %.
- [ ] **Re-run the live Jev bench.** `eat` and `sleep` now appear in Niko's menu (`sleep` before `wait`), changing the
      candidate numbers; `Llm.Bench -- --list` shows every expected label is still a candidate, but accuracy was last
      measured before Dev-011.
- [ ] **Dev-011 manual check.** Watch a `test` world for one game day at x10 (Extras eat, drink at the pond, sleep
      at their anchors) and capture the `DevConsole` NPC table with the new `HUN`/`THI`/`RST` columns
      (`--shots`). The sim half is automated in `NeedsReplayRules.cs`; this checks the look and the pace.

## Planned work

- [ ] **Construction system, phase 3: diagonal slots.** Phases 1 and 2 implement edge/floor
      construction and `H`/`V` interior halves (archive entries `Dev-036.md` and `Dev-037.md`),
      with region-aware movement/A* and rendering. Add `D1`/`D2` region geometry and diagonal wall
      picking/build/break, render their quads and review the shader in `game.render`. `slot_mask`,
      `slot_mat` and region-bearing A* already exist; generalize the eight-sector connectivity without
      regressing H/V. The phase plan is in section 11 of archive entry `Dev-036.md`.
- [ ] **Infra03 CI on GitHub Actions**: run the checks on push now that the remote exists. The
      number was earmarked as Infra02 by archive entry `Infra01.md`; Infra02 became the agent
      context diet. No plan exists yet.
- [ ] **Dev-004 manual checks (HUD layout, build panel, dev console).** Captures at the design size
      exist (`--shots DIR --shots-size 2560x1440`, run against an isolated `--database`):
      `spawn-x2.png`, `build-walls.png`, `build-floors.png`, `build-selected.png`,
      `dev-console.png`, `dev-console-npcs.png`, `llm-placeholder.png`. Open: compare `HUD / Base`
      (11:53) against `spawn-x2.png` for the 2 px match and confirm the derived `Header` 64, footer
      80 and status column 296, chosen so the frame sums to 2560×1440; see the selected `BuildSlot`
      and a real submit on a surface with `build_cost` (the default `test` world has none, so at
      seed 7 the only slot is a disabled `Asphalt wall`); press `D`, `W`, `A`, `S` with the console
      closed; and confirm that opening the build panel or the console does not stop movement.
      `HudLayoutTests.cs` covers the frame arithmetic instead of a screenshot.
- [ ] **Split `GameHud.cs`.** Dev-005 left it over 600 lines (`check:sizes` warns); split it if it
      grows again.
- [ ] **Dev-004: Figma side.** Update the Figma frames with the panels from D3 and export the
      `EtherBound/Colors` tokens. The implementing session had no Figma access, so the palette comes
      from the tokens already documented in `utils/STYLEGUIDE.md` [Sec. 3] and the derived widths
      above stay unconfirmed. Do not draw HP/EP until the sim exposes vitals: the `STATUS` panel
      stays hidden.

**Deprecated at cut-over** (archive entry `Dev-025.md`, section 3.7): Dev-014 (AI furniture as a
primitive spec), Dev-016 (context menu polish), Dev-021 (wall lines and heights), Dev-023 (server
kernel refactor), Dev-024 (baked AO). Their documents are preserved in `docs/deprecated/Docs-Archive.zip`.
None needs a follow-up Dev for its *rendering* idea — real 3D geometry, real-time SSAO/shadows and
the already-built radial/context menu (Dev-025 stages 4-5) absorbed all of them. One surviving idea
does not: Dev-021 also motivated a future `build` op (players/NPCs constructing walls), which is a
gameplay change Dev-025 explicitly excluded from its port-only scope. It is tracked in construction
phases 1 and 2 (archive entries `Dev-036.md` and `Dev-037.md`); diagonal phase 3 remains above.

- [ ] **Minimap memory.** `Dev-010` ships a local window with no memory. Open: an explored-chunk set
      (fog, a persisted table and an engine-written event) and a full `M` map on the same sampler.
- [ ] **Float32 precision far from the origin.** `Dev-010` placed Niko 20 km out in a throwaway save and a still
      `--shots` capture shows no gap or z-fighting, but motion jitter was not checked (a still cannot show it).
      Walk there windowed before trusting it; a floating origin in `game.render` is the fix if it jitters.
- [ ] **Sprite sheets for sand, clay, rock and water.** The endless terrain uses them, and they draw as flat
      material colour (the mesher's fallback) until BitCanvas makes sheets for them (`Sheets` in `WorldClient`).
- [ ] **Dev-010 Figma side.** Add the minimap rect to the HUD frames in `EtherBound-Design`.

## Deferred work

- [ ] **Host first frame remains late.** `Dev-010` did not worsen it: the windowless host's first frame (Release, warm,
       `Bench -- chunkgen`) is 48-51 ms for `infinite` (world setup 15 ms, snapshot 28-31 ms, of which the whole first
       minimap window is 17 ms) against 60-72 ms for `test`. Keep the accepted 3.37 s warm-launch baseline. The new
       catalog preloader produced 44.1, 67.9 and 77.9 ms from `WorldClient._Ready` to first host frame
       in isolated Debug runs (median 67.9 ms); a warm Release run measured 107.4 ms. First-frame
       actor updates are now 6.1–8.5 ms and meet the 20 ms target. Do not send a blank/loading frame
       to game the metric: publishing requires the restored world. The <=50 ms host target still
       needs a safe world-restore optimization.
- [ ] **Chunk crossing budget is not reliable yet.** On the sim side an endless crossing is cheap (Release: 5 new chunks
       read in the frame 2.8 ms with no prefetch, a chunk 1.1 ms on average and 2.2 ms at p95, the minimap's 9 new chunks
       about 2.7 ms); the client's mesh rebuild was not re-measured with `infinite`, so the numbers below still stand. The serial/parallel geometry parity test passes,
       projection buffers and surviving mesh-band nodes are reused, and Extras use batched rendering.
       After moving array packing to worker-built buffers, two current Release traces rebuilding 15
       chunks measured 42.3 and 42.6 ms. Both remain above the 40 ms budget; earlier measurements
       were from the pre-packing path and are not comparable. Further improvement must keep Godot
       resource/node creation on the main thread and must not hide content.
- [ ] **Nsight Systems 2019 Vulkan layer is blocked by permissions.** Its manifest is absent at
       `C:\Program Files\NVIDIA Corporation\Nsight Systems 2019.5.2\Target-Windows\x86_64\VkLayers\VkLayer_nsight-sys_windows.json`, but its value remains in
       `HKLM\SOFTWARE\Khronos\Vulkan\ExplicitLayers`. Removing only that value returned Access Denied;
       cleanup requires an elevated Windows session. No other Vulkan layer was changed.

---

## TL;DR

Six design questions (abilities, city authoring, carry-over, event log retention, NPC
construction, recipes and supply; the Jev runtime was settled in `Dev-007`); the op list is settled in archive entry `Dev-007.md`
and the Matter primitive in `Dev-012.md`. Dev-025's seven stages replaced the Python/FastAPI server
and Phaser/React client with deterministic C# and Godot (~87× faster per tick at 1,000 Extras);
`legacy-python-web-stack.zip` preserves the old source. The manual parity/export and walking-feel
checks were verified and closed on 29/09/2026. Dev-004 rebuilt the HUD as the Figma layout and Dev-005 polished it
(closed 01/10/2026); the Figma-side update and Dev-004's own visual checks stay open above. Replay, actor collision, resumable
work, event delivery
and blocked-edge movement are implemented; first-frame actor updates are within budget. Dev-014/016/
021/023/024 are deprecated: their rendering ideas were absorbed by the port, except the future `build`
op, implemented in phase 1 (archive entry `Dev-036.md`). The remaining first-host-frame, worst-case
chunk budget and elevated Nsight cleanup are listed above. Dev-007 was fully accepted on 22/09/2026.
