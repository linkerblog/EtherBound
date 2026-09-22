# EtherBound — UI style guide

The visual system of the HTML overlay that sits on top of the Phaser canvas. Adapted from the
LiraMind retro-terminal guide.

**Aesthetic: Niko's HUD.** Niko is a bioengineered combat unit; the interface is his internal
display: monospaced, near-black panels, neon accents, no rounded corners. The terminal look is
diegetic, not decoration, which is why glitch effects are reserved for moments that happen *to
Niko* (Ether, overload) instead of being sprinkled for style.

---

## 1. Layers

| Layer | What | Rules |
|---|---|---|
| World | Phaser canvas, LimeZu pixel art | No CSS effects over it. No text drawn in Phaser |
| HUD | Clock, speeds, feed, meters, status pills, input line | Anchored to the edges, never covers the centre of the screen |
| Panels | Character, relationships, inventory, phone | Right-side drawer, one open at a time |
| Scene | Prose, options, free text during autopause | Bottom third; the world stays visible and dimmed behind it |
| Menu | Right-click context menu | At the cursor, above everything |

```css
.world  { position: fixed; inset: 0; z-index: 0; }
.hud    { position: fixed; inset: 0; z-index: 10; pointer-events: none; }
.hud > * { pointer-events: auto; }
.drawer { z-index: 20; }
.scene  { z-index: 30; }
.menu   { z-index: 40; }
```

The HUD container lets clicks through to the canvas; only its children catch them.

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

### Game semantics

| Meaning | Colour |
|---|---|
| Running clock, success, legal, owned by Niko | `--green` |
| Information, selection, witnessed fact | `--cyan` |
| Ether, Ether strain | `--magenta` |
| Paused, warning, rumor (unconfirmed), low need | `--yellow` |
| Danger, harm, illegal, wanted, critical need | `--red` |
| Unavailable, unknown, dimmed | `--line-hi` (+ `opacity: .55–.65`) |

A verb that is possible but illegal where Niko stands is shown in `--red`, not hidden: the world
lets him do it and tells him it matters.

---

## 4. Reset and base

```css
* { box-sizing: border-box; border-radius: 0 !important; }
html, body { height: 100%; }
body {
  margin: 0; background: var(--bg); color: var(--text);
  font: 13px/1.55 var(--mono); overflow: hidden;
}
::selection { background: var(--cyan); color: var(--bg); }
::-webkit-scrollbar { width: 8px; height: 8px; }
::-webkit-scrollbar-track { background: var(--panel); }
::-webkit-scrollbar-thumb { background: var(--line-hi); }

canvas { image-rendering: pixelated; }
```

Pixel art: the camera zooms in integer steps only (x1 to x4; wheel and `+`/`-`/`0`), never
fractional, so tiles stay crisp.

---

## 5. HUD panels

HUD panels are translucent so the world reads through them.

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
┌──────────────────────────────────────────────────────────┐
│ [clock + speeds]                         [status pills]  │
│                                                          │
│                        world                     drawer ▸│
│                                                          │
│ [event feed]                                  [meters]   │
│ > input line                                             │
└──────────────────────────────────────────────────────────┘
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

---

## 8. Context menu (right-click)

The menu is built from the verbs the server returns. The client never adds or removes verbs; it
only renders them.

```css
.menu {
  position: fixed; min-width: 180px; background: var(--panel);
  border: 1px solid var(--line-hi); border-left: 2px solid var(--cyan);
  padding: 4px 0; box-shadow: 0 6px 24px rgba(0,0,0,.6);
}
.menu .target { padding: 2px 12px 6px; color: var(--dim); font-size: 11px; border-bottom: 1px solid var(--line); }
.menu .verb { display: flex; gap: 8px; padding: 3px 12px; cursor: pointer; }
.menu .verb::before { content: "›"; color: var(--dim); }
.menu .verb:hover, .menu .verb.focus { background: var(--cyan); color: var(--bg); }
.menu .verb:hover::before, .menu .verb.focus::before { color: var(--bg); }
.menu .verb.illegal { color: var(--red); }
.menu .verb.ether { color: var(--magenta); }
.menu .verb.off { color: var(--line-hi); cursor: not-allowed; }
.menu .verb .why { margin-left: auto; color: var(--dim); font-size: 10px; }
```

- Arrow keys move `.focus`, Enter picks, Esc closes.
- A verb that exists but cannot be done now (`.off`) shows why in `.why` ("locked", "too heavy").

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

While the input has focus, WASD types instead of moving.

---

## 10. Scene (autopause)

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
.feed .item .time  { color: var(--dim); font-size: 10px; margin-right: 6px; }
.feed .item.enter  { animation: glitch-in .42s steps(4) both; }
.feed .item.old    { opacity: .45; transition: opacity 2s; }
```

The difference between `.seen` and `.rumor` matters: the feed shows Niko's knowledge, not the
truth.

---

## 12. Meters

ASCII bars for needs, health and Ether strain.

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

---

## 13. Status pills

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

## 15. Diegetic effects

The CRT and glitch effects from LiraMind are kept, but they are **events, not decoration**.

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

- Desktop first; minimum supported viewport 1280×720. No phone layout.
- Every HUD action has a keyboard path (hotkeys for speeds, options, menu navigation).
- Colour is never the only signal: illegal verbs, rumors and Ether also carry a marker or style.

---

## TL;DR

The overlay is Niko's HUD: LiraMind's retro-terminal palette and components, translucent panels
anchored to the edges over pixel art that stays untouched. Monospace for interface, IBM Plex Serif
for prose. Magenta means Ether and nothing else. Menus come from the server, the feed shows what
Niko knows (seen vs rumor), and glitch/CRT effects fire only when something happens to Niko.
