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

## Planned work

- [ ] **Dev-014 AI furniture** (`Dev-014.md`): an LLM proposes furniture as a validated primitive
      spec that BitCanvas renders; lands after Dev-012. The `gamesync.js` syntax error was fixed
      independently by Fix10.
- [ ] **Dev-016 context menu** (`Dev-016.md`): grouped and named entries, hover target, keyboard
      opening and navigation, ARIA roles, layout and contrast fixes, live refresh. Awaiting approval.

## Deferred work

- [ ] **Input replay.** The event log stores outcomes, not the 20 Hz inputs, so a run cannot
      yet be replayed exactly (`utils/VISION.md` [Sec. 11]).
- [ ] **Events to the client.** Only one event reaches the WebSocket: the `activity` notice when
      one of Niko's activities finishes (`done/Dev-007.md`). Everything else stays server-side
      until there is something to narrate (witnesses).
- [ ] **Activity progress is lost on interruption.** Walking away from a half-dug hole throws
      the progress away; partial progress that survives is a later refinement
      (`done/Dev-007.md` [Sec. 1]).

## Manual checks not run

The `check:visual` seed-7 spawn baselines (`web/tests/visual/spawn.spec.ts`, x1/x2/x4) now cover
the spawn view of the Dev-006, Dev-008, Fix05 and Fix09 checks below; the user still approves the
three images (`Infra01.md` [Sec. 6] 7).

- [ ] **Dev-017 radial menu:** press `V` at spawn (shows `Wait`/`Inspect`) and while carrying a
      shovel; confirm the ring stays centred on Niko while walking, hover and ←/→/digits move the
      hub detail, Enter runs the focused op, a dimmed slot shakes the hub with its reason, `V`/`Esc`
      close it, and `V` typed in the input line does nothing. See `docs/Dev-017.md` [Sec. 4]
      acceptance 3–4.
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
- [ ] **Dev-015 lab map:** DEBUG → MAP → LAB, `feature = relief`, `amplitude = 20`, REGENERATE;
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

The Dev-005 GUI acceptance passed; details are recorded in `docs/done/Fix02.md`.

---

## TL;DR

Six design questions (abilities, city authoring, carry-over, event log retention, NPC
construction, recipes and supply); the op
list is settled by `done/Dev-007.md` and the Matter primitive by `done/Dev-012.md`. Fix11's Dev
Blog migration landed; Fix10's manual Send to game acceptance remains open. Dev-013 is implemented;
its live physics/animation acceptance remains open. Two deferred items
(input replay, events to the client) plus lost activity progress. Manual acceptances remain for
Dev-003 browser, Dev-006 new game, Fix03 logging, Fix04 logging/movement, and Dev-008/Fix05/Fix06/Fix07
isometric rendering and network, plus Fix08 reconnect, Fix09 side textures and building base,
Dev-009 BitCanvas filesystem/game GUI acceptance, the Dev-010 asphalt road in the live game, the
Dev-011 roofing and brick walls in the live game, and the Dev-012 rendering, CARRY/menu, BitCanvas
furniture, seed-0 layout and save-migration checks, the Dev-013 physics and animation acceptance,
and the Fix11 Dev Blog database's Notion-UI read-through; Dev-007 was fully accepted on 22/09/2026.
Fix05–Fix09 automated validation passed; their GUI visual/performance checklists remain open.
