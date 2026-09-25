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

- [ ] **Dev-025 C# sim and Godot client** (`Dev-025.md`): port to a deterministic C# simulation
      and a Godot 4 .NET client with a 3D orthographic pixel-art camera, in seven stages proven by
      goldens from Python. Dev-014/016/021/023/024 stay frozen and are deprecated at cut-over.
      Approved 25/09/2026; stage 0 (look spike) done and its screenshots approved.
- [ ] **Dev-014 AI furniture** (`Dev-014.md`): an LLM proposes furniture as a validated primitive
      spec that BitCanvas renders; lands after Dev-012. The `gamesync.js` syntax error was fixed
      independently by Fix10.
- [ ] **Dev-016 context menu** (`Dev-016.md`): grouped and named entries, hover target, keyboard
      opening and navigation, ARIA roles, layout and contrast fixes, live refresh. Awaiting approval.
- [ ] **Dev-021 wall lines and heights** (`Dev-021.md`): six full-length wall lines per tile
      (edges, midlines, diagonals) and 0.5 m / 1 m / full heights; lands after Dev-022. The `build`
      op that places them is the next doc. Fix13's mask geometry is in place; awaiting approval.
- [ ] **Dev-024 baked ambient occlusion** (`Dev-024.md`): per-tile edge/corner AO computed at
      chunk draw time and painted in the existing `Blitter` batches, plus face, wall-base and
      object contact darks, with a `DEBUG` toggle. Awaiting approval.
- [ ] **Infra03 CI on GitHub Actions**: run the checks on push now that the remote exists. The
      number was earmarked as Infra02 by `Infra01`; Infra02 became the agent context diet. No doc yet.

## Deferred work

- [ ] **Input replay.** The event log stores outcomes, not the 20 Hz inputs, so a run cannot
      yet be replayed exactly (`utils/VISION.md` [Sec. 11]).
- [ ] **Actors pass through each other.** Extras and Niko collide with terrain, never with bodies,
      so they can overlap on a tile. Body collision, and knowing who is in the way, is a later
      refinement (recorded by `done/Dev-018.md`).
- [ ] **Events to the client.** Only one event reaches the WebSocket: the `activity` notice when
      one of Niko's activities finishes (`done/Dev-007.md`). Everything else stays server-side
      until there is something to narrate (witnesses).
- [ ] **W/A/S/D cannot be typed in text fields.** `MapScene` adds its movement keys with Phaser's
      default capture, and Phaser's global keyboard manager calls `preventDefault` on them, so the
      input line drops them (`wasd hello` types ` hello`). Predates Dev-019, which found it; the
      likely fix is `addKey(code, false)`, since `readDirection` already ignores keys while a text
      field has focus. Needs its own small doc.
- [ ] **Activity progress is lost on interruption.** Walking away from a half-dug hole throws
      the progress away; partial progress that survives is a later refinement
      (`done/Dev-007.md` [Sec. 1]).

## Manual checks not run

The `check:visual` seed-7 spawn baselines (`web/tests/visual/spawn.spec.ts`, x1/x2/x4) now cover
the spawn view of the Dev-006, Dev-008, Fix05, Fix09 and Dev-018 checks below; the user still
approves the three images (`Infra01.md` [Sec. 6] 7). Dev-019 re-shot them at the framed 1240×652
canvas; that approval is tracked under Dev-019.

- [ ] **Dev-019 framed viewport and view tabs:** approve the re-shot x1/x2/x4 baselines (1240×652);
      in the GUI the frame reads as a frame, the active tab joins the viewport, the HUD panels do not
      overlap at 1280×720, and right-click / `V` anchor on the right tile at x1 and x4; in Edge,
      `Alt+1..3` and ←/→ on a focused tab work and `Tab` reaches the tabs before the HUD. Verified
      headlessly: layout box, tab hotkeys, `Esc`, the `W` gate and the right-click anchor at x2. See
      `done/Dev-019.md` [Sec. 5] acceptance 3, 5–6.

- [ ] **Fix13 wall faces and 1/4 m body:** approve the new `building-x1.png` and
      `building-x2.png` baselines, then at x1/x2/x4 verify that walls sit on the floor, the top reads
      as a solid 25 cm slab, front walls have no grey band, ends do not float, windows sit at 1–2 m,
      corners close, brick courses remain intact, and a front wall still cuts to a one-unit stub.
      The paused visual spec generated both baselines; GUI acceptance and user approval remain open.
      See `done/Fix13.md` [Sec. 5] acceptance 6.

- [ ] **Fix14/Fix16 body and slab occlusion:** in the user's lab save at x1, x2 and x4, stand at the
      third screenshot and spawn positions, then walk outside the north and west walls and past the
      corners. The spawn's confirmed slab coverage is cut and no slab still overlaps Niko; steps and
      terraces do not light the silhouette, and walking remains smooth. Manual because the user's
      save and frame-feel cannot be covered headlessly. See `done/Fix14.md` [Sec. 4] 3 and
      `done/Fix16.md` [Sec. 4] 3.

- [ ] **Fix15 front-wall stubs:** in the user's lab save at x1 and x2, walk along all four outer
      walls, around each corner and through the west doorway. No section on the far side of a wall
      disappears; the wall directly in front still drops to a stub, and the interior cut is unchanged.
      Manual because this is a visual check in the user's save. See `done/Fix15.md` [Sec. 4] acceptance 4.

- [ ] **Dev-018 basic Extras:** in the running game at seed 7, six green bodies stand near the spawn
      and, unpaused, walk and wait near their homes without overlapping the tiles they walk; `Inspect`
      on one reads `<name>. Standing.`/`Walking.`/`Waiting.`; `Push`/`Hit` name it. Its paused
      baselines were superseded by Dev-019's re-shoot. See `done/Dev-018.md` [Sec. 6]
      acceptance 11–12.

- [ ] **Fix10 BitCanvas Send to game:** in Chromium, send into a scratch `sprites` folder with a
      `grass` directory; confirm `grass_x4.png` and `grass_side_x4.png` are written and the folder
      name survives reload. See `done/Fix10.md` [Sec. 6] step 4.
- [ ] **Dev-003 launcher:** `O` with the browser closed opens the game, and quitting the
      launcher leaves the browser open (`done/Dev-003.md` [Sec. 9]).
- [ ] **Dev-006 new game:** manual GUI acceptance 1–10, including same-seed regeneration and
      reconnect redraw (`done/Dev-006.md` [Sec. 7]). The automated web build and server checks pass.
- [ ] **Fix04 movement and logging:** confirm logged movement lines and step alignment below
      20 fps in Chromium DevTools (`done/Fix04.md` [Sec. 6]).
- [ ] **Dev-008 isometric renderer:** manual visual, picking, movement, zoom and performance
      acceptance 1–12 (`done/Dev-008.md` [Sec. 7]).
- [ ] **Fix05 renderer:** GUI acceptance 1–9 for VOID floors, terrain overlap and x1 redraw/performance
      behavior (`done/Fix05.md` [Sec. 7]); automated checks pass, GUI profiling was not run here.
- [ ] **Fix06 renderer review:** GUI acceptance 1–9 for ledge faces, structure cutaway, silhouette and
      grass/planks/stone textures (`done/Fix06.md` [Sec. 7]); automated checks pass.
- [ ] **Fix07 renderer/network review:** GUI acceptance 1–6 for textured sheets loading through Vite
      and the world surviving reloads (`done/Fix07.md` [Sec. 7]); automated checks pass.
- [ ] **Fix08 reconnect:** a tab loaded with the API server down recovers to `LINKED` once the server
      is up, without a reload (`done/Fix08.md` [Sec. 6]); verified in a headless browser and by
      automated tests.
- [ ] **Fix09 side textures and building base:** GUI acceptance 1–8 for the sheared side sheets
      (grass lips, slab edges, seams, light, other materials, fallback) and the closed black gaps
      along the building's east and south base (`done/Fix09.md` [Sec. 7]); automated tests pass.
      F2 step 0 was confirmed headlessly, with no GUI available: at seed 1895070486 the interior
      column's solid top is 6 and its S/E fill faces run from the surrounding terrain (h 1–3) up
      to 6 along x 159|160 and y 159|160.
      On 23/09/2026 the user's screenshot at x1 (Niko at `X 137.84 · Y 148.77 · Z 2 (6.0 m)`)
      confirmed 1 (soil sides with the grass lip on the top unit only), 2 (stone slab edges match
       their tops; the light grey bands are gone) and 6 for the south-west base (no black gaps;
       the east side was out of frame). Still open: 3, 4, 5, 7, 8, the east side of 6, seed 7 and x4.
- [ ] **Dev-009 BitCanvas:** GUI acceptance for real directory picking, persisted permission, writes
      to a scratch folder, and game/Vite reload remains unverified; `file://` rendering works in
      headless Chromium. See `done/Dev-009.md` [Sec. 5].
- [ ] **Dev-010 asphalt:** in the running game, the road at x 120–123 is textured at about its old
      flat tone, road edges with a height step show the asphalt side sheet, and the test building's
      concrete floors show concrete, not cobblestone. See `done/Dev-010.md` [Sec. 4] step 5.
- [ ] **Dev-011 roofing and brick:** in the running game on the roof (z 4), the slab is textured at
      about its old tone and the brick walls show running-bond courses stepping 2:1 along each edge,
      a header course on the top unit, mortar lines continuous from tile to tile and unit to unit;
      windows still show the glass band, and walking next to a front wall still cuts it to a
      one-unit stub. See `done/Dev-011.md` [Sec. 4] step 6.
- [ ] **Dev-012 rendering and cutaway:** a table, chest, shelf and barrel drawn in the right slots,
      a bottle on the table and apples in piles, an occluded chest not drawn, and no frame-time
      regression. See `done/Dev-012.md` [Sec. 7.2].
- [ ] **Dev-012 menu, CARRY and feed:** `Take Bottle ×3`, `Put Bottle into Chest`, `L`/`R`/`BACK`
      rows, `LOAD` amber above 10 kg, live load updates and an `act` confirmation line. See
      `done/Dev-012.md` [Sec. 7.3].
- [ ] **Dev-012 BitCanvas furniture:** the chest and barrel are about 1 m tall with tops at 32 px, and
      the pixel-style `table`, `chair`, `chest`, `shelf` and `barrel` sprites are exported to
      `src/sprites/object/`. See `done/Dev-012.md` [Sec. 7.4].
- [ ] **Dev-012 seed-0 furniture positions:** confirm the generated objects against the recorded
      coordinates in `done/Dev-012.md` [Sec. 8].
- [ ] **Dev-012 save migration:** point the server at a real `data/etherbound.db` and confirm
      `0005_object` upgrades it in place. See `done/Dev-012.md` [Sec. 10].
- [ ] **Dev-013 physics:** in the running game, push and throw objects into an obstacle, break a
      wall over repeated hits and watch an object fall into the pit; the animation must match the
      committed final positions at x1–x4. See `done/Dev-013.md` [Sec. 4] step 7.
- [ ] **Fix11 Dev Blog database:** open it in Notion and confirm it reads well, newest first, with the
      properties visible. See `done/Fix11.md` [Sec. 6] step 7.
- [ ] **Dev-015 lab map:** the `DEBUG` tab → MAP → LAB, `feature = relief`, `amplitude = 20`, REGENERATE;
      Niko appears north of the feature bay and walks into the hills; change `scale` and regenerate;
      then walk bays 1–9 and confirm each matches `done/Dev-015.md` [Sec. 2]. See
      `done/Dev-015.md` [Sec. 5] acceptance 8.

- [ ] **Fix03 walking:** acceptance 1 (no jump at start or stop) was confirmed by the user on
      22/09/2026 ("walking feels smooth"), and 5 (releasing the keys after `DIG` does not
      cancel it) in the Dev-007 check. 3 was confirmed too: smooth at 4 m/s on grass and
      4.44 m/s on asphalt (4 / 0.9, the server's `walk_cost`). 2 was confirmed too: after stopping, the HUD read
      `X 125.46 · Y 144.40 · Z 0 (1.0 m)`, exactly the server position. 4 passed as well: paused, Niko can neither walk
      nor act, and x10 leaves walking at real-time speed (the clock speed only scales game
       minutes). Still open in `docs/done/Fix03.md` [Sec. 6]: 6 (confirming event lines in `server.log`).

- [ ] **Infra02 reading map:** give a cold subagent only the reading map in `AGENTS.md` and check
      that it picks `PITFALLS.md` [Sec. 1] for "change the launcher's port handling" and [Sec. 4]
      for "add a new object kind". See `done/Infra02.md` [Sec. 6] acceptance 6.

The Dev-005 GUI acceptance passed; details are recorded in `docs/done/Fix02.md`.

---

## TL;DR

Six design questions (abilities, city authoring, carry-over, event log retention, NPC
construction, recipes and supply); the op
list is settled by `done/Dev-007.md` and the Matter primitive by `done/Dev-012.md`. Fix11's Dev
Blog migration landed; Fix10's manual Send to game acceptance remains open. Dev-013 is implemented;
its live physics/animation acceptance remains open. Dev-018 is implemented: six seeded Extras walk
and wait near the spawn; its GUI acceptance and the approval of the re-shot paused baselines remain
open, and actor-vs-actor collision stays deferred. Dev-019 is implemented: a framed viewport with
`GAME`/`DEBUG`/`LLM` tabs; its baseline approval and GUI/Edge checks remain open. Dev-020's walls
are corrected by Fix13: the faces hang from their draw height and the render-only thickness is
1/4 m with a parallelogram strip, end faces and a corner post. Fix13's baseline approval and
x1–x4 GUI acceptance remain open. Fix14 selects structure cutaways from body-height probes every
frame with a one-tile release; Fix16 tests drawn slab faces against an independent pixel oracle and
probes the full sprite. The Fix14/Fix16 lab GUI check remains open. Fix15 limits front-wall stubs to
unobstructed sightlines; its lab GUI acceptance remains open. Two deferred items (input replay,
events to the client) plus lost
activity progress, actor pass-through and W/A/S/D
swallowed in text fields. Manual
acceptances remain for
Dev-003 browser, Dev-006 new game, Fix03 logging, Fix04 logging/movement, and Dev-008/Fix05/Fix06/Fix07
isometric rendering and network, plus Fix08 reconnect, Fix09 side textures and building base,
Dev-009 BitCanvas filesystem/game GUI acceptance, the Dev-010 asphalt road in the live game, the
Dev-011 roofing and brick walls in the live game, the Dev-012 rendering, CARRY/menu, BitCanvas
furniture, seed-0 layout and save-migration checks, the Dev-013 physics and animation acceptance,
the Dev-018 Extras acceptance, and the Fix11 Dev Blog database's Notion-UI read-through; Dev-007 was
fully accepted on 22/09/2026.
Fix05–Fix09 automated validation passed; their GUI visual/performance checklists remain open.
Infra02 moved agent boot text into on-demand guides; its cold-subagent reading-map check remains open.
