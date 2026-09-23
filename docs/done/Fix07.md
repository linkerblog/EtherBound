# Fix07: Fix06 review (textures blocked by Vite, black world after reload)

[Bugfix] [Rendering] [Net] [Tooling]

Review of Fix06 as it stands in the working tree (uncommitted, overall `v0.6.2`,
`docs/done/Fix06.md`). The user reported on 22/09/2026:
- the textures stopped showing;
- after reloading the browser the world goes black, while Niko can apparently still move.

## 1. Findings

### F1: Vite refuses to serve the sprite sheets
`web/src/game/terrainSprites.ts` resolves the sheets with
`new URL("../../../src/sprites/...", import.meta.url)`. The sheets live in the repository's root
`src/sprites/`, outside Vite's root (`web/`). `logs/web.log` shows every request refused:

```text
The request id "C:\...\EtherBound\src\sprites\grass\grass_x4.png" is outside of Vite serving allow list.
```

(the same for `planks_x4.png` and `stone_x4.png`). The Dev-008 grass sheet worked because it was a
static `import`, and Vite lets files imported in the module graph through its allow list. A
`new URL(…, import.meta.url)` asset does not get that exception. Fix06 [Sec. 3.6] asked for the
existing import style, and the implementation changed it.

Phaser fails the three loads without a visible error. `createTerrainAtlas` then gets its
`__MISSING` texture, warns about the wrong size, and falls back to flat `top`. That is why every
surface is flat colour.

### F2: The chunks sent on connect are dropped before the scene listens
`App.tsx` calls `client.connect()` and only then `createGame(...)`. `MapScene` registers
`onChunk`/`onSnapshot` in `create()`, which runs only after `preload()` finishes loading the sheets.
The server sends the snapshot and the chunks around Niko right after the socket opens. It records
them as known for that connection (`server/src/etherbound/net/ws.py`, `_known_chunks`) and never
sends them again. `WebSocketClient` dispatches chunks only to the listeners present at that
moment, so the world is lost for good.

`onState` does replay the last world state to a late listener. That is why the position, the
HUD and movement still work. New chunks show up only when Niko walks into chunks the server has
not sent yet.

The race was already there. It now loses every time on reload because:
- the server and the socket are warm, so the chunks arrive in milliseconds;
- `preload` now waits for three requests, which fail.

A slow first launch happens to hide it.

## 2. Decisions to approve

| Topic | Decision | Why |
|---|---|---|
| Sprite loading | Static `import` of each sheet in a Vite-only module (`terrainSheets.ts`). The pure table in `terrainSprites.ts` keeps only key → file name, so node tests can read it. No `new URL` for assets | This is the pattern already proven in this repo. No Vite config change and no copying of the art out of `src/sprites/` |
| Loud failures | `preload` logs every `loaderror` with the key and the URL through `console.error` | F1 was silent in the browser. The next broken asset should say so in DevTools |
| Chunk replay | `WebSocketClient` keeps the latest revision of every chunk it has received. `onChunk` replays the cache to a new listener, the same way `onState` replays the world. A snapshot clears the cache before its listeners run | The client must hold every chunk the server believes it holds. The cache is exactly that set, whatever the scene's timing (reload, slow assets, StrictMode double effects) |
| Scope | Web only (`web.game`, `web.net`). The server, protocol and schema stay untouched | The server's "sent once" contract is correct. The client has to keep its side of it |

## 3. What changes

### 3.1 Sprites (`web.game`)
- `terrainSprites.ts` (pure, used by tests):
  - `TERRAIN_SPRITE_FILES: Record<string, string>` =
    `{ grass: "grass/grass_x4.png", wood_floor: "floor/planks_x4.png", concrete: "floor/stone_x4.png" }`,
    with paths relative to the root `src/sprites/`.
  - `shadeColor` and `spriteTint` stay as they are. `TERRAIN_SPRITES` is removed.
- `terrainSheets.ts` (new, imported by `MapScene` only):
  - One static import per sheet (`import grass from "../../../src/sprites/grass/grass_x4.png";`).
  - It exports `TERRAIN_SHEETS: Record<string, string>`, key → the imported URL, with the same keys
    as `TERRAIN_SPRITE_FILES`.
- `MapScene.preload`:
  - Loads `TERRAIN_SHEETS`.
  - Adds `this.load.on("loaderror", (file) => console.error(\`terrain sheet failed: ${file.key} ${file.url}\`))`.

### 3.2 Chunk cache (`web.net`)
- `WebSocketClient` gets `private readonly chunks = new Map<string, WorldChunk>()`, keyed
  `"cx,cy"`.
- `receive`:
  - `snapshot`: clear `chunks` **before** calling the snapshot listeners. The order stays
    snapshot → state, as today.
  - `chunk`: store it only if its revision is newer than the cached one, then dispatch it to the
    listeners as today. `ChunkStore.set` still drops stale revisions on the scene side.
- `onChunk(listener)`: after adding the listener, call it once for each cached chunk.
- `disconnect()` clears the cache. A new connection starts from an empty server-side
  `_known_chunks`, so it gets every chunk again.
- `MapScene` does not change for this. Its `onChunk` handler already creates the layers and marks
  the chunks dirty.

### 3.3 Tests (`web/tests/`)
- `terrain-sprites.test.ts`:
  - Switch to `TERRAIN_SPRITE_FILES`. Its keys are materials in `materials.toml`, as today.
  - Every file exists under the root `src/sprites/`, and its PNG `IHDR` (bytes 16–23) reads 256×32.
  - `terrainSheets.ts`, read as text, has one `import … from "../../../src/sprites/<file>"` per
    table entry and contains no `new URL(`. This is the guard against F1 coming back.
- `client-chunks.test.ts` (new), driving `receive` through a fake socket or a test hook:
  1. Chunks received before any listener are replayed to a listener added later.
  2. A snapshot clears the cache, so a listener added after it receives nothing old.
  3. An older revision does not replace a newer cached one.
  4. `disconnect()` clears the cache.

### 3.4 Docs
- `CONTEXT.md`:
  - `web.net` row: the client caches chunks and replays them to late listeners.
  - Measured pitfalls:
    - Assets outside `web/` must be static imports. A `new URL(…, import.meta.url)` is refused
      by Vite's allow list.
    - The server sends each chunk once per connection, so the client must never drop one.
- `docs/PENDING.md`: this doc's entry, replaced when done.

## 4. Traps

- **Do not move the art into `web/`** to dodge F1. The user keeps the sprites in `src/sprites/`.
- **Do not "fix" F2 by delaying `connect()`** until the scene is ready. Every later listener, and
  every remount, would have the same hole. The cache closes it for good.
- **Clear on snapshot before the listeners run.** Otherwise `MapScene`'s snapshot handler clears
  its store, and the next replay would bring back chunks from the previous world.
- **tsx cannot import `.png`.** Keep `terrainSheets.ts` out of every test's import graph.

## 5. Modules and versions

| Module | Version |
|---|---|
| web.game | v0.1.5 → v0.1.6 |
| web.net | v0.0.8 → v0.0.9 |

The overall project version is suggested to move to `v0.6.3`.

## 6. What must not break

- Everything in Fix06 [Sec. 6].
- Chunk revisions, neighbour redraws, and clearing on snapshot and on `NEW` game.
- The snapshot → state listener order (`CONTEXT.md`, `web.net`).

## 7. Checks and acceptance

Checks: `cd web && npm test && npm run build`, generated types unchanged, and the server checks.

Manual, with `EtherBound.exe`, seed 7:
1. `logs/web.log` has no "outside of Vite serving allow list" line, and DevTools shows the three
   sheets loaded (200).
2. Grass, the stone floors (concrete) and the plank floors (wood) are textured, as in Fix06 [Sec. 7] 9.
3. **Reload** (F5) five times: the world appears every time, textured.
4. **Hard reload** (Ctrl+F5) and a reload with the server already warm: the same.
5. **`NEW` game** with another seed: the old world does not come back, and the new one draws.
6. Walking into new chunks after a reload still streams them.

Automated checks passed: 43 web tests, the production build, unchanged generated types, and the
server checks (79 tests, lint, format, pyright). The interactive GUI acceptance 1–6 was not run here
and remains open in `docs/PENDING.md`.

## 8. Todo

### Decisions
- [x] Approve [Sec. 2] (approved by the request to execute Fix07)

### Code
- [x] `TERRAIN_SPRITE_FILES`, `terrainSheets.ts`, `loaderror` logging [Sec. 3.1]
- [x] Chunk cache and replay in `WebSocketClient` [Sec. 3.2]

### Checks
- [x] Tests [Sec. 3.3]; web build, generated types, server checks
- [ ] Manual acceptance 1–6 [Sec. 7]

### Closing
- [x] `CONTEXT.md`, `PENDING.md` [Sec. 3.4]; `docs/utils/VERSION.md` [Sec. 5]
- [x] Notion: Work Report for the date and a Dev Blog page
- [x] Move this doc to `docs/done/`

---

## TL;DR

- **No textures:** the sheets are loaded with `new URL(...)`, and Vite refuses to serve files from
  the root `src/sprites/` that way (`web.log` shows it). Go back to static imports in a Vite-only
  module, and log load errors loudly.
- **Black world after reload:** the socket connects before the Phaser scene listens. The server
  sends each chunk only once, so they are lost, while movement survives because the state is
  replayed.
- The fix: the WebSocket client caches chunks and replays them to late listeners, the same way it
  already replays the state.

Web only: `web.game` v0.1.6, `web.net` v0.0.9.
