# EtherBound — UI style guide

The visual system of Niko's HUD. Adapted from the LiraMind retro-terminal guide; originally an
HTML/CSS overlay over the Phaser canvas, now a Godot `Control`/`CanvasLayer` theme over the 3D
viewport (`game/ui/`, Dev-025 cut-over 26/09/2026). The design below — palette, fonts, layout,
menu and HUD behaviour, effects — did not change at cut-over; only the implementation medium did.
Read the CSS/HTML/JS snippets as the precise design reference (colours, spacing, states, keyboard
rules), not as code that runs: `game/ui/GameHud.cs`, `ActionMenuOverlay.cs` and `GeneratorPanel.cs`
are the real implementation, in Godot `Theme` overrides and `StyleBoxFlat`. A section marked **not
yet built** describes a target for a feature that has no Godot implementation yet (same as it had
no web implementation before it either); its CSS still communicates the intended look precisely.

**Aesthetic: Niko's HUD.** Niko is a bioengineered combat unit; the interface is his internal
display: monospaced, near-black panels, neon accents, no rounded corners. The terminal look is
diegetic, not decoration, which is why glitch effects are reserved for moments that happen *to
Niko* (Ether, overload) instead of being sprinkled for style.

BitCanvas (`BitCanvas/`) follows this guide too, except [Sec. 1] and [Sec. 15], which belong to the
game's world and to Niko.

---

## 1. Layers

| Layer | What | Rules |
|---|---|---|
| Frame | View tabs (`GAME`, `DEBUG`, `LLM`) | Top-left of the window; no content |
| World | The 3D viewport (`PixelView`'s low-res `SubViewport`, isometric pixel art) | No effects over it from the HUD. No text drawn in the 3D world |
| HUD | Clock, speeds, feed, carry, activity, input line | Anchored to the window edges, never covers the centre of the screen |
| View panel | `DEBUG` and `LLM` views | Their own `Control`, visible only for that tab; the world keeps rendering behind it |
| Panels | Character, relationships, inventory, phone — **not yet built** | Right-side drawer, one open at a time |
| Scene | Prose, options, free text during autopause — **not yet built** | Bottom third; the world stays visible and dimmed behind it |
| Menu | Right-click list and the `V` radial menu | At the cursor / on Niko, above everything |

In Godot, the world lives in a `SubViewport` drawn by a `TextureRect` (`PixelView`); the HUD is a
`CanvasLayer` (`Layer = 2`, above the default `0`) so it always composites over that texture without
being part of its pixel grid. `GameHud`'s own root `Control` holds the `GAME`/`DEBUG`/`LLM` view
containers (only one `Visible` at a time) and the always-on panels; `ActionMenuOverlay` is a further
child added last, so it draws on top of everything else in the same layer. There is no `display:
none` equivalent to worry about: a Godot `Control` with `Visible = false` skips both drawing and
input without collapsing any size the `SubViewport` depends on.

---

## 2. Fonts

```html
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;600;700&family=IBM+Plex+Serif:ital,wght@0,400;0,600;1,400&display=swap" rel="stylesheet">
```

```css
--mono: "JetBrains Mono", "Cascadia Code", Consolas, ui-monospace, monospace;
--prose: "IBM Plex Serif", Georgia, serif;
```

- `--mono` for everything that is interface: HUD, menus, buttons, meters, labels.
- `--prose` only for narrative text in scenes. Long prose in 13px monospace is tiring to read; the
  serif marks "this is the story" against "this is the system".

**Not yet matched in Godot.** `GameHud`/`ActionMenuOverlay` currently use the engine's default
theme font at the sizes above (10–12 px), not JetBrains Mono/IBM Plex Serif; loading those as
Godot `FontFile` resources under `game/assets/` and setting them as the HUD `Theme`'s default fonts
is open work, not a design change.

---

## 3. Palette

```css
:root {
  --bg: #07080a;       /* page background behind the canvas */
  --panel: #0c0d10;    /* panel background */
  --panel-a: rgba(12,13,16,.88); /* HUD panels over the world */
  --bar: #121318;      /* bars / section headers */
  --line: #22242b;     /* subtle borders/separators */
  --line-hi: #3a3d47;  /* highlighted borders */
  --text: #d6d8de;     /* main text */
  --dim: #6a6d78;      /* secondary text/metadata */
  --green: #5af78e;    /* success, primary accent, "live" */
  --cyan: #57c7ff;     /* info, secondary accent, selection */
  --magenta: #ff6ac1;  /* Ether. Nothing else uses it */
  --yellow: #f3f99d;   /* warning, paused */
  --red: #ff5c57;      /* danger, harm, illegal */
}
```

**Magenta is Ether.** In LiraMind it was the brand accent; here it belongs to the one thing only
Niko has. Text selection uses cyan instead.

**Matched in Godot**, hex for hex, as `Color` constants in `GameHud`/`ActionMenuOverlay`
(`TextColor`, `DimColor`, `GreenColor`, `CyanColor`, `YellowColor`, `RedColor`, and `#ff6ac1` for
Ether feed lines); panel backgrounds and borders use the same `--panel`/`--bar`/`--line`/`--line-hi`
values in `StyleBoxFlat`.

### Game semantics

| Meaning | Colour |
|---|---|
| Running clock, success, legal, owned by Niko | `--green` |
| Information, selection, witnessed fact | `--cyan` |
| Ether, Ether strain | `--magenta` |
| Paused, warning, rumor (unconfirmed), low need | `--yellow` |
| Danger, harm, illegal, wanted, critical need | `--red` |
| Unavailable, unknown, dimmed | `--line-hi` (+ `opacity: .55–.65`) |

An op that is possible but illegal where Niko stands is shown in `--red`, not hidden: the world
lets him do it and tells him it matters.

---

## 4. Reset and base

No CSS reset applies in Godot; every `StyleBoxFlat` in `game/ui/` sets its own square corners
(`CornerRadius* = 0`) directly, matching "no rounded corners" above. `Terrain.gdshader` and
`PixelView`'s nearest-filtered `SubViewport` are the equivalent of `image-rendering: pixelated`.

Pixel art: the camera zooms in integer steps only (x1 to x4; `1`/`2`/`4` keys today, still no
fractional step), so tiles stay crisp.

---

## 5. HUD panels

HUD panels are translucent so the world reads through them.

**Matched in Godot:** `GameHud.CreatePanel` builds this exact panel (90% alpha `--panel`, `--line`
border, a `▍ TITLE` heading row in `--dim` on a `--bar` strip) for `CLOCK`, `FEED`, `CARRY`, `ACT`,
`DEBUG` and `LLM`; `Layout()` anchors them to the window edges on every resize instead of a fixed
1240×652 frame, since the Godot window is resizable.

```css
.hud-panel {
  background: var(--panel-a); border: 1px solid var(--line);
  backdrop-filter: blur(2px); padding: 8px 12px;
}
h3 {
  margin: -8px -12px 8px; padding: 4px 12px; font-size: 11px; font-weight: 600;
  text-transform: uppercase; letter-spacing: 1px; color: var(--dim);
  background: var(--bar); border-bottom: 1px solid var(--line);
}
h3::before { content: "▍"; color: var(--cyan); margin-right: 6px; }
```

Placement:

```text
  ┌──────┬───────┬─────┐                     ETHERBOUND // LIVE SIMULATION
  │ GAME │ DEBUG │ LLM │
┌─┘      └───────┴─────┴───────────────────────────────────────┐
│ [clock + speeds]                              [status pills]  │
│                                                               │
│                        world (viewport)               drawer ▸│
│                                                               │
│ [event feed]                                       [carry]    │
│                                                    [meters]   │
│ > input line                                                  │
└───────────────────────────────────────────────────────────────┘
```

The frame is 20 px on every side plus the 28 px tab row on top, so at 1280×720 the viewport is
1240×652. HUD panels anchor to the viewport, not to the window.

The `CARRY` panel sits directly above the meters: one row per hand slot (`L`, `R`, or a single
`HANDS` row for a two-handed object) and a `BACK` row for a worn slot, each either the object and
its count or `—`. Its last row is `LOAD 12.4 kg`, shown in `--dim` up to the free 10 kg and in
`--yellow` above it.

```css
.carry { margin-bottom: 6px; }
.carry .row { display: grid; grid-template-columns: 42px 1fr; gap: 6px; font-size: 11px; }
.carry .slot { color: var(--dim); text-transform: uppercase; }
.carry .item .count { color: var(--dim); }
.carry .load { color: var(--dim); }
.carry .load.warn { color: var(--yellow); }
```

---

## 6. Clock and speed controls

```html
<div class="hud-panel clock">
  <span class="time">DAY 3 · 14:27</span>
  <button class="mini">II</button><button class="mini active">x1</button>
  <button class="mini">x3</button><button class="mini">x10</button>
</div>
```

```css
.clock .time { font-weight: 700; margin-right: 10px; }
.clock.paused .time { color: var(--yellow); animation: blink 1s steps(1) infinite; }
.clock.scene .time::after { content: " · SCENE"; color: var(--yellow); }
.mini.active { color: var(--green); border-color: var(--green); }
```

**Matched in Godot** (`GameHud.UpdateFrame`/`BuildGamePanels`): `DAY 1 · 04:05` in bold green,
turning yellow (not blinking yet) while paused; `II`/`RESUME`, `x1`/`x3`/`x10` and `NEW` mini
buttons. The `.scene` suffix has no equivalent yet (no scene system).

---

## 7. Buttons

Bracket buttons for real decisions (scene options, confirmations); `.mini` for toolbar controls.

```css
button {
  font: 600 12px var(--mono); cursor: pointer; border: 1px solid var(--line-hi);
  background: transparent; color: var(--text); padding: 4px 12px;
  text-transform: uppercase; letter-spacing: .5px;
}
button::before { content: "[ "; color: var(--dim); }
button::after { content: " ]"; color: var(--dim); }
button:hover { background: var(--text); color: var(--bg); border-color: var(--text); }
button:hover::before, button:hover::after { color: var(--bg); }

button.primary { border-color: var(--green); color: var(--green); }
button.primary:hover { background: var(--green); color: var(--bg); box-shadow: 0 0 14px rgba(90,247,142,.5); }
button.danger { border-color: var(--red); color: var(--red); }
button.danger:hover { background: var(--red); color: var(--bg); }
button.ether { border-color: var(--magenta); color: var(--magenta); }
button.ether:hover { background: var(--magenta); color: var(--bg); box-shadow: 0 0 14px rgba(255,106,193,.5); }
button:disabled { opacity: .35; cursor: not-allowed; }

.mini { border: 1px solid var(--line-hi); padding: 1px 7px; font-size: 10px; color: var(--dim); }
.mini::before, .mini::after { content: none; }
.mini:hover { background: var(--line-hi); color: var(--text); }
```

**Partly matched in Godot:** `ButtonStyle` in `game/ui/` sets the same border/hover-invert colours
per state (normal, hover, disabled), but the `[ bracket ]` pseudo-decoration around the label text
is not reproduced — a Godot `Button`'s text is drawn as given, so the brackets would need to be
part of the label string itself. `danger`/`ether` tinting is applied ad hoc (e.g. illegal/Ether
context-menu rows) rather than as a named button variant.

---

## 8. Context menu (right-click)

The menu is built from the ops the sim returns (`WorldEngine.Menu`, over the host channel). The
client never adds or removes ops; it only renders them. **Matched in Godot:**
`ActionMenuOverlay.ShowContext`/`ShowRadial` reproduce the panel border, the dim `TARGET` line,
per-row `illegal`/`ether` tinting and a disabled row's tooltip reason; arrow keys, number keys,
Enter/Space and Esc all work as below. The bracket-button `[ ]` note from [Sec. 7] applies here too.

```css
.menu {
  position: fixed; min-width: 180px; background: var(--panel);
  border: 1px solid var(--line-hi); border-left: 2px solid var(--cyan);
  padding: 4px 0; box-shadow: 0 6px 24px rgba(0,0,0,.6);
}
.menu .target { padding: 2px 12px 6px; color: var(--dim); font-size: 11px; border-bottom: 1px solid var(--line); }
.menu .op { display: flex; gap: 8px; padding: 3px 12px; cursor: pointer; }
.menu .op::before { content: "›"; color: var(--dim); }
.menu .op:hover, .menu .op.focus { background: var(--cyan); color: var(--bg); }
.menu .op:hover::before, .menu .op.focus::before { color: var(--bg); }
.menu .op.illegal { color: var(--red); }
.menu .op.ether { color: var(--magenta); }
.menu .op.off { color: var(--line-hi); cursor: not-allowed; }
.menu .op .sub { color: var(--dim); }
.menu .op:hover .sub, .menu .op.focus .sub { color: var(--bg); }
.menu .op .why { margin-left: auto; color: var(--dim); font-size: 10px; }
```

- Arrow keys move `.focus`, Enter picks, Esc closes.
- An op that exists but cannot be done now (`.off`) shows why in `.why` ("locked", "too heavy").
- `.sub` is the entry's `subject`, the object it acts on, right after the label
  (`Take Bottle ×3`, `Put Bottle into Chest`). The label alone is identical for every object.

### Radial menu (`V`)

The same server entries for Niko's tile and his open neighbours (`radius=1`), in two levels
(`done/Fix17.md`). Level 1 is one slot per verb on rings around him; level 2 shows that verb's targets,
each in the screen direction of its tile. The hub names the target and the focused option; the
client never hides an op (an unavailable slot is dim, never removed).

```css
.radial { position: fixed; z-index: 40; width: 0; height: 0; }
.radial-lines { position: absolute; left: 0; top: 0; transform: translate(-50%, -50%); overflow: visible; pointer-events: none; }
.radial-lines line { stroke: var(--line-hi); stroke-width: 1; }
.radial-hub { position: absolute; left: 0; top: 0; transform: translate(-50%, -50%); width: 172px; text-align: center; background: var(--panel); border: 1px solid var(--line-hi); border-left: 2px solid var(--cyan); padding: 8px 10px; }
.radial-slot { position: absolute; transform: translate(-50%, -50%); border: 1px solid var(--line-hi); background: var(--panel-a); color: var(--text); }
.radial-slot:hover, .radial-slot.focus { background: var(--cyan); color: var(--bg); border-color: var(--cyan); }
.radial-slot.off { color: var(--line-hi); border-color: var(--line); cursor: not-allowed; }
.radial-slot.illegal { color: var(--red); }
.radial-slot.ether { color: var(--magenta); }
.radial-slot.violent .radial-op::before { content: "! "; color: var(--yellow); }
.radial-slot.radial-tile { width: 132px; height: 34px; min-width: 0; max-width: none; justify-content: center; }
.radial-hub .radial-header { display: block; width: 100%; border: 0; border-bottom: 1px solid var(--line); background: none; color: var(--cyan); font-weight: 600; }
button.radial-header::before { content: "‹ "; color: var(--dim); }
.radial-hub .radial-row { display: block; width: 100%; margin: 0 0 4px; padding: 3px 6px; border: 1px solid var(--line-hi); background: var(--panel-a); }
.radial-hub .radial-row:hover, .radial-hub .radial-row.focus { background: var(--cyan); color: var(--bg); border-color: var(--cyan); }
```

- `V` toggles and closes from either level. The radial is anchored on Niko and follows him while
  open.
- **Level 1, verbs.** One slot per `op`, in catalog order; eight per ring, first at the top,
  clockwise, further verbs on a wider ring. A verb with one entry shows it whole (`OPEN` /
  `CHEST`) and runs at once; one with several opens level 2. A verb is `.off` only when every
  entry under it is. ←/↑ and →/↓ cycle, `1`–`9` focus by position, Enter/Space pick, Esc or a
  click on the hub closes.
- **Level 2, targets.** The hub shows the verb as `.radial-header` (click to go back), then one
  `.radial-row` per entry on Niko's own tile. Every other entry is a fixed-size `.radial-tile`
  slot in its tile's screen octant: up and down columns sit above and below the hub, the other
  six on two side lanes; a tile with several entries forms a column growing away from the hub.
  Lines reach only the first slot of each column. The label is the entry's subject, or the
  tile's surface (`GRASS`).
- **Level 2 keys.** ←/↑ and →/↓ cycle hub rows, then octants clockwise from up. The numpad is the
  3×3 around Niko: `8` up, `9` up-right, `6` right, `3` down-right, `2` down, `1` down-left, `4`
  left, `7` up-left, `5` the hub rows; the same digit again steps down that column. Enter/Space
  pick; `Esc`/`Backspace` go back to level 1.
- While a target (or a single-entry verb) has focus, its tile gets a 1 px cyan outline on the map.

---

## 9. Input line (Enter)

```html
<div class="hud-panel input"><span class="prompt">&gt;</span><input placeholder="say or try anything"></div>
```

```css
.input { display: flex; align-items: center; gap: 8px; }
.input .prompt { color: var(--green); font-weight: 700; }
input, textarea {
  flex: 1; background: transparent; color: var(--text); border: none;
  font: inherit; caret-color: var(--green);
}
input:focus, textarea:focus { outline: none; }
input::placeholder { color: var(--dim); }
.input:focus-within { border-color: var(--green); box-shadow: inset 0 0 12px rgba(90,247,142,.08); }
```

While the input has focus, WASD types instead of moving. **Matched in Godot:** `GameHud`'s
`LineEdit` uses the same green `>` prompt and `"say or try anything"` placeholder;
`WorldClient.SendMovement` checks `_gameHud.CanMoveWorld` (false while the input has focus) before
reading WASD, so typing never moves Niko.

---

## 10. Scene (autopause) — not yet built

No Godot implementation: this needs the LLM/narrative layer (`docs/PENDING.md`), which never
existed in the web client either.

```css
.scene {
  position: fixed; left: 50%; bottom: 24px; transform: translateX(-50%);
  width: min(760px, calc(100vw - 32px)); max-height: 45vh; overflow: auto;
  background: var(--panel); border: 1px solid var(--line-hi); border-top: 2px solid var(--yellow);
  padding: 14px 18px;
}
.scene-backdrop { position: fixed; inset: 0; background: rgba(0,0,0,.45); z-index: 29; }

.prose { font: 16px/1.65 var(--prose); color: var(--text); white-space: pre-wrap; }
.prose .thought { color: var(--dim); font-style: italic; }
.prose .speech .who { font: 600 11px var(--mono); color: var(--cyan); text-transform: uppercase; letter-spacing: 1px; margin-right: 6px; }

.options { display: flex; flex-direction: column; gap: 6px; margin-top: 12px; }
.options button { text-align: left; text-transform: none; }
.options button .key { color: var(--dim); margin-right: 6px; }
.options button .roll { float: right; color: var(--dim); font-weight: 400; }
```

- Niko's thoughts are the lines that start with `...`; the renderer wraps them in `.thought`.
- Each option shows its hotkey (`1`–`9`) and, when it involves a roll, the skill and risk in
  `.roll` ("OBSERVE · MEDIUM"). Options are mechanics; the UI shows that honestly.
- The free-text input [Sec. 9] sits under the options.

### "Narrator thinking" line

While the LLM prepares prose, show decoding noise instead of a spinner:

```css
.noise { color: var(--magenta); opacity: .8; letter-spacing: 1px; }
```

```js
const GLYPHS = "░▒▓█▀▄▌▐<>/\\|=+*#%&01ｱｲｳｴｵｶｷｸｹｺ";
const noise = (n) => Array.from({ length: n }, () => GLYPHS[(Math.random() * GLYPHS.length) | 0]).join("");
```

`Math.random` is allowed here: it is presentation, not simulation.

---

## 11. Event feed

What Niko perceives or hears, newest at the bottom, fading out after a while.

```css
.feed { width: 360px; max-height: 30vh; overflow: hidden; display: flex; flex-direction: column; justify-content: flex-end; }
.feed .item {
  padding: 4px 8px; border-left: 2px solid var(--line-hi); background: var(--panel-a);
  margin-top: 3px; font-size: 12px;
}
.feed .item.seen   { border-left-color: var(--cyan); }
.feed .item.rumor  { border-left-color: var(--yellow); font-style: italic; }
.feed .item.harm   { border-left-color: var(--red); }
.feed .item.ether  { border-left-color: var(--magenta); }
.feed .item.act    { border-left-color: var(--line-hi); }                  /* DIG · 24 MIN, DIG DONE */
.feed .item.warn   { border-left-color: var(--yellow); color: var(--yellow); } /* CAN'T DIG · reason */
.feed .item.fail   { border-left-color: var(--red); color: var(--red); }       /* DIG FAILED · reason */
.feed .item .time  { color: var(--dim); font-size: 10px; margin-right: 6px; }
.feed .item.enter  { animation: glitch-in .42s steps(4) both; }
.feed .item.old    { opacity: .45; transition: opacity 2s; }
```

The difference between `.seen` and `.rumor` matters: the feed shows Niko's knowledge, not the
truth. `.act`, `.warn` and `.fail` report Niko's own actions: a start with its length, a result, a
refusal with its reason, a failure with its reason. An interruption the player caused shows
nothing. The words carry the meaning (`CAN'T`, `FAILED`), never only the colour.

**Matched in Godot:** `GameHud.PushFeed(text, category)` colours `warn`/`fail`/`act`/`seen`/`ether`
exactly as above and caps the feed at 8 rows; `.rumor` has no source yet (no rumor system), and the
`glitch-in`/fade-to-`.old` entrance/exit animation [Sec. 15] is not built.

---

## 12. Meters — not yet built

ASCII bars for needs, health and Ether strain; no Godot implementation until needs/health exist.

```css
.meter { display: grid; grid-template-columns: 70px 1fr; gap: 6px; font-size: 11px; }
.meter .label { color: var(--dim); text-transform: uppercase; }
.meter .bar { color: var(--cyan); letter-spacing: -1px; white-space: nowrap; overflow: hidden; }
.meter .bar .off { color: var(--line-hi); }
.meter .bar.warn { color: var(--yellow); }
.meter .bar.bad { color: var(--red); }
.meter .bar.ether { color: var(--magenta); text-shadow: 0 0 6px rgba(255,106,193,.4); }
```

```js
function meter(pct, cells = 16) {
  const on = Math.round((pct / 100) * cells);
  return `${"█".repeat(on)}<span class="off">${"░".repeat(cells - on)}</span>`;
}
```

The `CARRY` panel [Sec. 5] is not a meter: `LOAD` is a plain kilogram figure that turns amber
above the free 10 kg, the same warning colour as a `.bar.warn`.

---

## 13. Status pills — not yet built

No `wanted`/faction system yet, so nothing produces a pill in Godot either.

```css
.pill {
  font-size: 11px; padding: 1px 8px; border: 1px solid var(--line-hi); color: var(--dim);
  text-transform: uppercase; letter-spacing: .5px; background: var(--panel-a);
}
.pill.wanted { color: var(--red); border-color: var(--red); }
.pill.wanted::before { content: "● "; animation: blink 1s steps(1) infinite; }
.pill.ether  { color: var(--magenta); border-color: var(--magenta); text-shadow: 0 0 6px rgba(255,106,193,.5); }
.pill.warn   { color: var(--yellow); border-color: var(--yellow); }
.pill.ok     { color: var(--green); border-color: var(--green); }
```

---

## 14. Drawer and tabs

**View tabs.** Folder tabs in the top-left, in the order `GAME`, `DEBUG`, `LLM`; the app always
starts on `GAME`. Each view owns the whole window: `GAME` is the world and its HUD, `DEBUG` holds
the map generator form (`MAP` only for now), `LLM` is a placeholder until the first in-game model.
Leaving `GAME` closes the menus and the `NEW` popover, releases the input line's focus and disables
the world's keyboard; switching views never pauses or changes the clock. **Matched in Godot:**
`GameHud.SwitchView`/`UpdateTabStyles` do exactly this (cyan active tab, dim inactive), though the
"active tab is one pixel taller" overlap trick is CSS-specific and not reproduced — Godot just
recolours the tab buttons.

```css
.view-tab { height: 24px; border: 1px solid var(--line); border-bottom: none; background: var(--bar); color: var(--dim); }
button.view-tab::before, button.view-tab::after { content: none; }
.view-tab.active { height: 29px; margin-bottom: -1px; border-color: var(--line-hi); background: var(--panel); color: var(--cyan); box-shadow: inset 0 1px 0 var(--cyan); }
```

Tab-style buttons need the `button.` prefix on their `::before`/`::after` reset, or the global
`button:not(.mini)::before` brackets win on specificity.

**Drawer — not yet built.** The debug drawer is gone (replaced by the `DEBUG` view); the drawer
stays the pattern for Niko's panels ([Sec. 1] "Panels"), none of which exist yet in either stack.

```css
.drawer {
  position: fixed; top: 56px; right: 0; bottom: 56px; width: 360px;
  background: var(--panel); border-left: 1px solid var(--line-hi); display: flex; flex-direction: column;
  transform: translateX(100%); transition: transform .15s steps(3);
}
.drawer.open { transform: none; }
.tabs { display: flex; gap: 2px; padding: 0 8px; background: var(--bar); border-bottom: 1px solid var(--line); }
.tab {
  border: none; border-bottom: 2px solid transparent; background: transparent; color: var(--dim);
  padding: 6px 12px; font-size: 11px; letter-spacing: 1px;
}
.tab::before, .tab::after { content: none; }
.tab:hover { background: transparent; color: var(--text); }
.tab.active { color: var(--cyan); border-bottom-color: var(--cyan); text-shadow: 0 0 8px rgba(87,199,255,.45); }
.list .row { padding: 6px 10px; border-bottom: 1px solid var(--line); border-left: 2px solid var(--line-hi); }
.list .row .meta { font-size: 11px; color: var(--dim); }
```

Relationship rows show each axis as a short meter [Sec. 12], not a single number.

---

## 15. Diegetic effects — not yet built

The CRT and glitch effects from LiraMind are kept, but they are **events, not decoration**. No
Godot implementation yet (would be a shader/`AnimationPlayer` pass over the HUD `CanvasLayer`, and
requires Ether/damage/overwhelm state that doesn't exist yet either).

| Effect | When |
|---|---|
| Scanlines on HUD panels | Always, subtle, only on `.hud-panel` and `.scene` |
| `glitch` on text | Niko uses Ether |
| `rgb-hit` | Niko takes damage |
| Full-screen scanlines + flicker | Niko is overwhelmed (his trait) or incapacitated |
| `glitch-in` | New feed item |

```css
.hud-panel, .scene { position: relative; }
.hud-panel::after, .scene::after {
  content: ""; position: absolute; inset: 0; pointer-events: none;
  background: repeating-linear-gradient(to bottom, rgba(255,255,255,.022) 0 1px, transparent 1px 3px);
}

body.overwhelmed::before {
  content: ""; position: fixed; inset: 0; pointer-events: none; z-index: 50;
  background:
    repeating-linear-gradient(to bottom, rgba(255,255,255,.04) 0 1px, transparent 1px 3px),
    radial-gradient(ellipse at center, transparent 45%, rgba(0,0,0,.7) 100%);
  animation: flicker 3s infinite steps(1);
}

.glitch { position: relative; }
.glitch::before, .glitch::after {
  content: attr(data-text); position: absolute; left: 0; top: 0; opacity: 0;
  pointer-events: none; white-space: nowrap;
}
.glitch.glitching { animation: jitter .34s steps(3) both; }
.glitch.glitching::before { opacity: .85; color: var(--cyan); animation: slice-a .34s steps(2) both; }
.glitch.glitching::after { opacity: .85; color: var(--magenta); animation: slice-b .34s steps(2) both; }

.rgb-hit { animation: rgb-hit .45s steps(4) both; }

@keyframes blink { 50% { opacity: 0; } }
@keyframes flicker { 0%,100% { opacity: 1 } 41% { opacity: .82 } 42% { opacity: 1 } 77% { opacity: .9 } 78% { opacity: 1 } }
@keyframes jitter {
  0% { transform: translate(0) } 33% { transform: translate(-2px, 1px) skewX(-6deg) }
  66% { transform: translate(2px, -1px) skewX(4deg) } 100% { transform: none }
}
@keyframes slice-a {
  0% { clip-path: inset(8% 0 62% 0); transform: translateX(-4px) }
  30% { clip-path: inset(52% 0 18% 0); transform: translateX(3px) }
  60% { clip-path: inset(78% 0 4% 0); transform: translateX(-2px) }
  100% { clip-path: inset(0 0 100% 0) }
}
@keyframes slice-b {
  0% { clip-path: inset(60% 0 12% 0); transform: translateX(4px) }
  30% { clip-path: inset(12% 0 66% 0); transform: translateX(-3px) }
  60% { clip-path: inset(34% 0 44% 0); transform: translateX(2px) }
  100% { clip-path: inset(0 0 100% 0) }
}
@keyframes rgb-hit {
  0% { text-shadow: 3px 0 var(--magenta), -3px 0 var(--cyan); filter: brightness(1.6); transform: skewX(-10deg) }
  50% { text-shadow: -2px 0 var(--magenta), 2px 0 var(--cyan); transform: skewX(6deg) }
  100% { text-shadow: none; filter: none; transform: none }
}
@keyframes glitch-in {
  0% { opacity: 0; transform: translateX(-8px) skewX(-10deg); text-shadow: 3px 0 var(--magenta), -3px 0 var(--cyan); filter: brightness(2); }
  35% { opacity: 1; transform: translateX(5px) skewX(8deg); clip-path: inset(0 0 45% 0); }
  65% { transform: translateX(-2px); clip-path: inset(35% 0 0 0); text-shadow: -2px 0 var(--magenta), 2px 0 var(--cyan); }
  100% { opacity: 1; transform: none; clip-path: inset(0); text-shadow: none; filter: none; }
}
```

```js
const REDUCED = matchMedia("(prefers-reduced-motion: reduce)").matches;
function retrigger(el, cls, ms) {
  if (!el || REDUCED) return;
  el.classList.remove(cls);
  void el.offsetWidth; // forces reflow so the animation can be re-triggered
  el.classList.add(cls);
  setTimeout(() => el.classList.remove(cls), ms);
}
const glitch = (el) => retrigger(el, "glitching", 340);
const hit = (el) => retrigger(el, "rgb-hit", 450);
```

---

## 16. Accessibility and targets

```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation: none !important; transition: none !important; }
}
```

- Desktop first; minimum supported window 1280×720. No phone layout.
- Every HUD action has a keyboard path (hotkeys for speeds, options, menu navigation).
- View tabs: `Alt+1`/`Alt+2`/`Alt+3` select `GAME`/`DEBUG`/`LLM` from anywhere, matched on the
  physical digit key (`GameHud._UnhandledInput`, matched in Godot). `Esc` returning `DEBUG`/`LLM`
  to `GAME` and ←/→ roving focus between tabs are not built; `Enter` and `V` already only act on
  `GAME` (`CanMoveWorld`/the `_gameHud.ActiveView` guard).
- Colour is never the only signal: illegal ops, rumors and Ether also carry a marker or style.

---

## 17. Confirmations — not yet built

Destructive actions open a confirmation popover anchored to the control that opened it, never
centred over the world. Use a `danger` confirm button and a `CANCEL` button. Open with focus inside;
Enter confirms and Esc cancels. Do not add a global hotkey for a destructive action. No Godot
implementation yet: nothing in the client currently needs one (`NEW` overwrites nothing; a save
opens in place).

---

## TL;DR

The overlay is Niko's HUD: LiraMind's retro-terminal palette and components, translucent panels
anchored to the edges over pixel art that stays untouched. Monospace for interface, IBM Plex Serif
for prose. Magenta means Ether and nothing else. Menus come from the sim, the feed shows what Niko
knows (seen vs rumor), destructive actions use anchored confirmations, and glitch/CRT effects fire
only when something happens to Niko. Since the Dev-025 cut-over the implementation is a Godot
`CanvasLayer`/`Control` theme (`game/ui/`), not CSS; the palette, HUD panels, context/radial menu,
input line, feed and view tabs are matched, while fonts, meters, pills, the drawer, scenes,
diegetic effects and confirmations remain open work, same as before the port.
