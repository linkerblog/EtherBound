# Infra01: Repo safety, one check, enforced conventions and visual baselines

[Tooling] [Process] [Tests] [Docs]

The first `InfraNN` doc: work that changes *how* EtherBound is built and verified, not what the
game does. It introduces the doc type itself, `docs/utils/COMMITS.md` (written 23/09/2026) and the
checks that turn today's conventions into failures instead of reminders.

**Precondition:** `Dev-012`, `Fix10` and `Fix11` are committed, the working tree is clean and the
launcher is stopped. This doc re-checks out every file [Sec. 3.1], folds in `check:bitcanvas` from
Fix10 and extends the Dev Blog rule that Fix11 writes into `AGENTS.md`.

## 0. Findings (measured 23/09/2026)

| # | Finding |
|---|---|
| F1 | `git remote -v` is empty: the whole history exists on one disk |
| F2 | `core.autocrlf=true`; all 160 text blobs are LF (`git ls-files --eol`). Every `git add` warns "LF will be replaced by CRLF" |
| F3 | `VERSION.md` says `v0.6.7`, `HEAD` is `v0.7.0`. The launcher banner shows `v0.6.7` (`launcher/src/Program.cs:80`) |
| F4 | `npm run check:web` runs neither `npm test` (9 web test files, never run by a root script) nor the schema export, and skips the `git diff --exit-code schema.d.ts` that `CONTEXT.md` "Commands" documents |
| F5 | No aggregate check, no git hooks. "Run the checks" depends on memory |
| F6 | Only `rng.py` imports `random`; nothing reads the wall clock; only `engine/` and `app.py` import `etherbound.db`. The rules hold today, unenforced |
| F7 | The TOML loaders already validate (`engine/ops/catalog.py`, `world/materials.py`, `world/objects.py`) and tests load them. No new validation is needed |
| F8 | `server/tests/test_dev002.py` is named after a plan that is deleted when the phase closes |
| F9 | `PENDING.md` holds 12 manual GUI checks; renderer docs Fix05–Fix09 closed with their GUI acceptance open |
| F10 | `VISION.md` [Sec. 15] leaves "Isometric renderer (`Dev-008`)" unchecked; `Dev-008` is done |
| F11 | `CONTEXT.md` is 24 KB and loads every session; module rows run up to ~15 lines |

## 1. Decisions to approve

| Topic | Decision | Why |
|---|---|---|
| Doc type | `InfraNN.md` (two digits, not tied to the dev version). A doc that changes any `server.*` or `web.*` module is a `Dev`; one that touches only `tooling`, `launcher`, tests or process docs is an `Infra`. Same lifecycle and format as `Dev`/`Fix` | "Ops" and "Tool" are game terms; "Infra" appears nowhere in the repo |
| Commits | `docs/utils/COMMITS.md` is the only commit format. One commit per doc; every touched module +0.0.1 per commit; the overall version follows COMMITS [Sec. 2] and lands in `VERSION.md` in the same commit | Fixes F3 at its source and makes bumps checkable |
| Line endings | `.gitattributes`: `* text=auto eol=lf`, with `*.png`, `*.exe`, `*.db` as `binary` | F2. Blobs are already LF, so nothing is rewritten |
| Checks | `npm run check` runs every check. Hooks: `pre-commit` runs `check:fast`, `commit-msg` validates versions, `pre-push` runs `check` | F4, F5 |
| Hooks | Versioned in `.githooks/`, enabled once per clone with `npm run setup` (`core.hooksPath`) | No dependency; plain `sh`, which Git for Windows runs |
| Validator | `scripts/check-versions.mjs`, Node, no dependencies [Sec. 3.3] | Root tooling is already npm |
| Lint rules | Ruff `TID251` bans `random` and wall-clock calls in `server/src` outside `rng.py`; `test_architecture.py` limits DB imports [Sec. 3.4] | F6: `AGENTS.md` rules become failures |
| Visual baselines | Playwright screenshots of the running game at a fixed seed [Sec. 3.6]. Approved images replace GUI checklists where they reach | F9 |
| Notion | Fix11's Dev Blog rule (D5) also covers `InfraNN` docs that change code, with a new Kind `Infra`. The Work Report entry is the commit's tags and TL;DR under its `HH:MM` heading | Fix11 already narrows the Dev Blog; the Work Report then costs no extra writing |
| Manual checks | A manual check still open when its phase closes is automated or dropped, with the reason in `PENDING.md` | F9 |
| Plan size | A `Dev`/`Fix`/`Infra` doc aims at ~200 lines. Each acceptance item names its test file, its visual shot, or says "manual" and why | Dev-012 and Dev-014 exceed 400 lines |

## 2. Out of scope

- Server log levels and rotation, headless simulation runner, `DEBUG` named scenarios and
  teleport, the `MapScene.ts`/`world.py` split: they change game modules, so each is a `Dev`.
- CI on GitHub Actions and generating a catalog doc from the TOML files: `Infra02`, once the
  remote exists.
- The event log retention question (`PENDING.md`) and the "gray-box until Phase 2" question.

## 3. What changes

### 3.1 Repo
- **User:** create a private GitHub repository and push `main` (`git remote add origin ...`,
  `git push -u origin main`).
- `.gitattributes` as in [Sec. 1]. Then `git add --renormalize .` (expected: no changes) and
  re-check out the tree on the clean state (`git rm -r --cached -q . && git reset --hard`).

### 3.2 Scripts and hooks (`package.json`)

| Script | Runs |
|---|---|
| `setup` | `git config core.hooksPath .githooks` |
| `check:server` | unchanged |
| `check:web` | `cd server && uv run etherbound-schema && cd ../web && npm run gen:types && git diff --exit-code src/net/schema.d.ts && npm test && npm run build` |
| `check:bitcanvas` | from Fix10 |
| `check:launcher` | unchanged |
| `check:versions` | `node scripts/check-versions.mjs` |
| `check` | `check:versions`, `check:server`, `check:web`, `check:bitcanvas`, `check:launcher`, in that order |
| `check:fast` | `node scripts/check-versions.mjs --staged`, `ruff check`, `ruff format --check`, `check:bitcanvas` |
| `check:visual` | `cd web && npx playwright test` |

Hooks: `.githooks/pre-commit` → `npm run check:fast`; `.githooks/commit-msg` →
`node scripts/check-versions.mjs --message "$1"`; `.githooks/pre-push` → `npm run check`.

### 3.3 `scripts/check-versions.mjs`
- **Always:** `VERSION.md` has the overall line in the exact format `Program.cs:80` parses, and
  every module row has a version and paths that exist.
- **`--staged`:** each staged path maps to the module whose path is its longest prefix. Every
  mapped module's staged version is exactly its `HEAD` version + 0.0.1; a module bumped without
  a staged path fails too. Unmapped paths (`docs/`, `server/tests/`, `web/tests/`, `src/sprites/`,
  root `.md`) need no bump. The error names the module and both versions.
- **`--message <file>`:** the first line is `vX.Y.Z`, equals the staged overall version, and is
  exactly one level above the previous version (COMMITS [Sec. 2]).
- `VERSION.md` Path column becomes full repo-relative paths, directories ending in `/`
  (`server/src/etherbound/app.py`, `server/src/etherbound/config.py`,
  `server/src/etherbound/routes/`...). `tooling` also owns `.gitattributes`, `.githooks/`,
  `scripts/`, `server/pyproject.toml`, `server/uv.lock`, `web/package.json`,
  `web/package-lock.json`, `web/vite.config.ts`, `web/playwright.config.ts`.

### 3.4 Lint and architecture (`server/`)
- `pyproject.toml`: add `"TID"` to `select`; `[tool.ruff.lint.flake8-tidy-imports.banned-api]`
  bans `random` ("use `RNGStreams.stream(system)`"), `time.time`, `time.time_ns`,
  `datetime.datetime.now`, `datetime.datetime.utcnow`, `datetime.date.today`.
  `per-file-ignores`: `src/etherbound/rng.py` and `tests/**` → `TID251`.
- `tests/test_architecture.py`: an AST scan of `src/etherbound/**/*.py`. Importing
  `etherbound.db` (any submodule) or `sqlalchemy.orm` is allowed only under `engine/`, `db/` and
  in `app.py`.

### 3.5 Test names
- `test_dev002.py` → `test_world_rules.py`. Its menu and chunk-push tests move to `test_api.py`,
  its Phase 0 migration test to `test_migration.py`. No test body changes.

### 3.6 Visual baselines (`web/`)
- `@playwright/test` as a dev dependency, run on the installed Edge (`channel: "msedge"`), so no
  browser download. Headless, `--use-angle=swiftshader`, viewport 1280×720, scale 1,
  `maxDiffPixelRatio: 0.002`.
- `webServer` starts the server with the launcher's command (`launcher/src/Services.cs:32`) and
  `ETHERBOUND_DATABASE_URL` pointing at a temp file, never `data/etherbound.db`, plus Vite.
  Port 8000 is fixed by the Vite proxy, so `reuseExistingServer: false` refuses to run beside a
  live launcher.
- `tests/visual/spawn.spec.ts`: `POST /api/game/new {"seed": 7}`, a fresh browser context, wait
  until no `chunk` WebSocket frame arrives for 1 s, screenshot the canvas only (the HUD clock
  moves) at x1, x2 and x4 (`+` key).
- Baselines are committed next to the spec. `--update-snapshots` only in a doc that says why, and
  the user approves the new images.
- Required before closing any doc that touches `web.game`, `web.world` or sprite sheets. Not part
  of `check`: it needs free ports and a GPU-less renderer run.

### 3.7 Docs
- `AGENTS.md`: `InfraNN.md` in "Docs" with the scope rule [Sec. 1]; commits follow
  `docs/utils/COMMITS.md`; "Versions" points to COMMITS [Sec. 2] and the validator; "Before
  calling something done" becomes `npm run check` (plus `check:visual` for rendering); test files
  are named by feature, never by doc; manual-check expiry in "Closing a phase"; plan size and
  acceptance naming in "Development Docs"; in the Notion rule written by Fix11, "`Dev-XYZ` or
  `FixNN`" becomes "`Dev-XYZ`, `FixNN` or `InfraNN`", and Work Report entries come from the commit
  message; the determinism bullet notes Ruff enforces it.
- Notion: add `Infra` to the Dev Blog `Kind` select (Fix11 [Sec. 3.1]).
- `CLAUDE.md`: the plan rule names `Dev-XYZ.md`, `FixNN.md` and `InfraNN.md`.
- `CONTEXT.md`: "Commands" lists `setup`, `check`, `check:fast`, `check:visual`. Module rows
  shrink to one or two lines; what is a contract moves to "Contracts", what the code shows is cut.
- `VISION.md` [Sec. 15]: check "Isometric renderer (`Dev-008`)".
- `PENDING.md`: drop the Infra01 line; mark each manual check the spawn baselines cover.

## 4. Modules and versions

| Module | Version |
|---|---|
| tooling | v0.0.10 (after Fix10) → v0.0.11 |

No `server.*`, `web.*`, `launcher` or `bitcanvas` source changes. The overall version is set at
commit by COMMITS [Sec. 2] (a Z bump is expected).

## 5. What must not break

- `EtherBound.exe` still reads the overall line of `VERSION.md`; its format is unchanged.
- Every existing check still passes; no file under `server/src/` or `web/src/` changes.
- The savegame `data/etherbound.db` is never opened by the visual run.
- BitCanvas still opens from `file://` with no build.
- A clone without `npm run setup` still works; it only lacks the hooks.

## 6. Acceptance

1. `git ls-files --eol` shows `i/lf w/lf` for every text file; `git add` prints no CRLF warning.
2. `npm run check` passes and runs the 9 web test files. A local field rename in a Pydantic
   message model makes it fail on `schema.d.ts`; revert.
3. Validator, with scratch commits: an `engine/` edit without a bump fails naming `server.engine`;
   with +0.0.1 it passes; +0.0.2 fails; a message version that differs from `VERSION.md` fails;
   `v0.7.0` → `v0.9.0` fails.
4. A scratch `import random` in `engine/world.py` fails `ruff check` with the `RNGStreams`
   message; revert.
5. `test_architecture.py` passes; a scratch `from etherbound.db.models import Actor` in
   `world/grid.py` fails it; revert.
6. `npm run check:visual` passes twice in a row on unchanged code.
7. **Manual (user):** approve the three baseline images.
8. `git push` runs `npm run check` through `pre-push`.
9. No `test_dev*.py` remains; `AGENTS.md` and `CLAUDE.md` name `InfraNN.md` and `COMMITS.md`.

## 7. Todo

### Decisions
- [ ] Approve [Sec. 1]

### Repo
- [ ] Remote and first push (user) [Sec. 3.1]
- [ ] `.gitattributes` and re-checkout [Sec. 3.1]

### Checks
- [ ] Scripts, hooks and `setup` [Sec. 3.2]
- [ ] `check-versions.mjs` and `VERSION.md` paths [Sec. 3.3]
- [ ] Ruff `TID251` and `test_architecture.py` [Sec. 3.4]
- [ ] Rename `test_dev002.py` [Sec. 3.5]
- [ ] Playwright harness and baselines [Sec. 3.6]

### Acceptance
- [ ] [Sec. 6] 1–6, 8–9
- [ ] [Sec. 6] 7 (user)

### Closing
- [ ] `AGENTS.md`, `CLAUDE.md`, `CONTEXT.md`, `VISION.md`, `PENDING.md` [Sec. 3.7]
- [ ] `VERSION.md` [Sec. 4]; commit per `COMMITS.md`
- [ ] Notion: `Infra` kind, Dev Blog entry (Kind `Infra`), Work Report for the date
- [ ] Move this doc to `docs/done/`

---

## TL;DR

- New doc type `InfraNN.md` for tooling and process; commits follow `docs/utils/COMMITS.md`, one
  per doc, with the overall version written to `VERSION.md` in the same commit.
- Private remote, `.gitattributes` (LF), one `npm run check` that finally runs the web tests and
  the schema diff, and hooks: `check:fast` on commit, version check on the message, full check on push.
- `check-versions.mjs` enforces module bumps and the semver step; Ruff bans `random` and the wall
  clock outside `rng.py`; an architecture test keeps the DB inside the engine.
- Playwright baselines of seed 7 at spawn (x1, x2, x4) start replacing GUI checklists.
- Fix11's Dev Blog rule extends to Infra docs; Work Reports come from the commit message. Manual
  checks expire at phase close; plans aim at ~200 lines. `tooling` v0.0.11.
