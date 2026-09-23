# Fix10: BitCanvas stops at startup because `gamesync.js` does not parse

[Bugfix] [Tooling]

Found while writing `Dev-014` [Sec. 0]. The user opened `BitCanvas/index.html` from disk and
confirmed the broken state on 23/09/2026.

## 1. Findings

### F1: `send()` redeclares its own parameter
`BitCanvas/gamesync.js:137` declares `async function send(folderName, variantsCanvas, sidesCanvas, names)`
and line 167 declares `const folderName = folder;`. Redeclaring a parameter with `const` is a
`SyntaxError`, so the whole script is rejected. `folder` does not exist either. `git blame` puts the
line in the `v0.7.0` commit.

Measured in headless Chrome from `file://` on 23/09/2026:

```
Uncaught SyntaxError: Identifier 'folderName' has already been declared   gamesync.js:167
Uncaught ReferenceError: BitCanvasGameSync is not defined                app.js:1786
```

Effects:
- **`app.js` stops at line 1786.** `setMaterialTab('terrain')`, `updateMaterialControls()`,
  `storedFolderName()`, the slider progress and `paintTexture()` never run. On open, the Grass
  preview is empty, the palette swatches are grey and the sliders have no progress bar.
- **Texture mode throws on every control update.** `app.js:1468` reads `BitCanvasGameSync.supported`.
- **Furniture mode works.** At line 1468, `isFurniture ||` short-circuits before that read, so the
  page looks alive once a furniture piece is clicked.
- **Send to game cannot work.**

### F2: No check parses the BitCanvas scripts
`check:server`, `check:web` and `check:launcher` never touch `BitCanvas/`, a plain HTML/JS module
with no build. A syntax error there passes every documented check.

## 2. Decisions to approve

| Topic | Decision | Why |
|---|---|---|
| The fix | Delete `gamesync.js:167`. `folderName` is the parameter that `app.js:1737` already passes (`sheets.folder`) | The line is the whole bug. The remaining uses at 170, 172 and 181 read the parameter as intended |
| The check | New root script `check:bitcanvas` that runs `node --check` on every `BitCanvas/*.js`. It joins the check commands in `CONTEXT.md` | Catches this class of error in under a second, with no new dependency |
| Scope | No behaviour change beyond the startup that now completes. Rendering code is untouched | A fix, not a feature. `Dev-012` and `Dev-014` build on this state |

## 3. What changes

### 3.1 `bitcanvas`
- `gamesync.js`: delete line 167 (`const folderName = folder;`).

### 3.2 `tooling`
- `package.json`: `"check:bitcanvas": "node --check BitCanvas/pixelart.js && node --check BitCanvas/gamesync.js && node --check BitCanvas/app.js"`.
  A new script in `BitCanvas/` is added to this list in the same change that creates it.

### 3.3 Docs
- `CONTEXT.md`: `npm run check:bitcanvas` in "Checks", and the `bitcanvas` pitfall's version line.
- `docs/Dev-012.md` [Sec. 9]: `bitcanvas` becomes v0.0.4 → v0.0.5.
- `docs/Dev-014.md`: [Sec. 0], [Sec. 7], [Sec. 11] and the TL;DR point to this doc instead of
  carrying the fix.
- `docs/PENDING.md`: listed under "Open fixes". Once this lands, it moves to the "Manual checks
  not run" list if [Sec. 6] step 4 is still open.

## 4. Modules and versions

| Module | Version |
|---|---|
| bitcanvas | v0.0.3 → v0.0.4 |
| tooling | v0.0.9 → v0.0.10 |

The user sets the overall project version at commit.

## 5. What must not break

- BitCanvas opens from `file://` with no build and no server.
- Every sheet and furniture sprite renders byte-identically, since no rendering code changes.
- Furniture mode keeps hiding Send to game. Browsers without `showDirectoryPicker` hide it too.
- Downloads keep working for every export.

## 6. Checks and acceptance

1. `npm run check:bitcanvas` passes. Before the fix it fails on `gamesync.js`.
2. Headless Chrome, `file://`: the console has no errors. The screenshot shows the Grass preview
   painted on open, coloured palette swatches and slider progress.
3. Headless Chrome: switching all 7 terrain materials and all 8 furniture pieces logs no errors.
4. **Manual (user, Chromium):** Send to game into a scratch folder named `sprites` holding a
   `grass` directory writes `grass_x4.png` and `grass_side_x4.png`, and the folder name shows
   after a reload. Then `git restore src/sprites` if the real folder was used.

## 7. Todo

### Decisions
- [ ] Approve [Sec. 2]

### Code
- [ ] Delete `gamesync.js:167` [Sec. 3.1]
- [ ] `check:bitcanvas` script [Sec. 3.2]

### Checks
- [ ] [Sec. 6] steps 1–3
- [ ] [Sec. 6] step 4 (user)

### Closing
- [ ] `CONTEXT.md`, `Dev-012.md`, `Dev-014.md`, `PENDING.md` [Sec. 3.3]; `docs/utils/VERSION.md` [Sec. 4]
- [ ] Notion: Work Report for the date
- [ ] Move this doc to `docs/done/`

---

## TL;DR

- `gamesync.js:167` redeclares the `folderName` parameter, so the script does not parse.
  `app.js` then stops at startup: an empty preview and grey swatches on open, errors in texture
  mode and a dead Send to game. Furniture mode hides it.
- Fix: delete that line. Add `npm run check:bitcanvas` (`node --check` on every BitCanvas script)
  so no check misses this again.
- `bitcanvas` v0.0.4, `tooling` v0.0.10. `Dev-012`'s BitCanvas bump moves to v0.0.5.
