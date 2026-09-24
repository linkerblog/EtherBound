# AGENTS.md

**Read [`docs/utils/VISION.md`](docs/utils/VISION.md) before touching this repository.** It holds the pillars,
the architecture, the primitives and the build order. Several rules below look arbitrary until you
read why they are there.

Once code exists, `CONTEXT.md` in the root records the current technical context (modules, data
model, commands, measured pitfalls). Until then, `docs/utils/VISION.md` is the only reference.

## The minimum you must not break

- **The world engine is the only writer of state.** LLMs, Jev, the storyteller and every system
  propose; the engine validates, resolves and commits.
- **One action API.** Player, Agents and Extras act through the same ops. No actor gets a private
  shortcut, including the player.
- **Systems talk only through the event bus.** A new system is a new subscriber. If it needs to
  change the core, stop and write a doc first.
- **Reactions come from knowledge.** An NPC reacts only to what it perceived or was told, never to
  what happened out of its sight.
- **Generated, never authored.** Context menus, jobs, factions and laws emerge from primitives and
  data. A feature that needs special-case code means a primitive is missing: raise it, do not hack
  it in.
- **Every LLM and Jev decision is logged as an event**, so any run can be replayed with its seed.
- **Do not pass `reasoning` to a model that does not reason**: you switch it on and it becomes
  three times slower.
- **Adult content is allowed**, with two rules enforced as op preconditions in code: adults only
  (children are excluded from every sexual op) and consent from every party. Niko only consents
  if the player has chosen so.

## Conventions

- **Docs.** `AGENTS.md`, `CLAUDE.md` and `CONTEXT.md` are the root entry points. Every other `.md`
  goes in `docs/`, never in the root nor in code subdirectories. Living guides use `UPPER_CASE.md`
  (`docs/utils/VISION.md`, `docs/utils/VERSION.md`, `docs/utils/STYLEGUIDE.md`); versioned work docs use `Dev-XYZ.md` (`Dev-001`, `Dev-002`, etc., according to
  the development version), reviews use `FixNN.md`, and work that changes only tooling, the launcher,
  tests or process docs uses `InfraNN.md`. A doc that changes any `server.*` or `web.*` module is a
  `Dev`; one that does not is an `Infra`. A versioned doc in `docs/` is in progress; once
  its content is implemented it moves to `docs/done/`. `docs/done/` holds the current phase's
  finished docs only: not maintained, not consulted as a living reference. Before creating any
  `.md` file, check whether it already exists and update or reuse it instead of creating a duplicate.
- **Closing a phase empties `docs/done/`.** Once `docs/PENDING.md` has nothing left for the phase:
  move whatever in `docs/done/` is still true into `docs/utils/VISION.md`, `CONTEXT.md` or tests
  (a "must not break" list becomes tests where it can); make sure no living doc points into
  `docs/done/`; add a `Phase N Recap` entry (`Kind = Recap`) to the Dev Blog that links the
  phase's entries; tag the closing commit `phase-N-end`; then delete the contents of `docs/done/`.
  A manual check still open when its phase closes is automated or dropped, with the reason written
  in `docs/PENDING.md`.
  Git history is the archive, and nobody reads old plans from it unless the user asks.
- **Vision changes go to `docs/utils/VISION.md` first.** If a decision changes, update the doc, then the code.
- **Versions.** `docs/utils/VERSION.md` lists every module and its version. Every module starts at
  `v0.0.0`; on every modification, bump the affected module's version by `0.0.1` and update
  `docs/utils/VERSION.md` in the same change. A change that touches several modules bumps each of them.
  Its overall project version must match the version assigned to the latest commit. The launcher
  banner and console title display this overall version from `docs/utils/VERSION.md`, not the launcher's
  module version; keep both displays in sync whenever the overall version changes. Every commit
  follows `docs/utils/COMMITS.md`; the overall
  version and the commit bumps follow `docs/utils/COMMITS.md` [Sec. 2], enforced by
  `scripts/check-versions.mjs`.
- **English only.** Code, comments, prompts, identifiers, docs and player-facing text are English.
  Identifiers are ASCII (no accents, no `ñ`); player-facing text is not bound by that. Never mix two
  languages inside one file.
- **Response tone.** Keep responses cordial, affectionate and affirming, with the requested
  lovebombing warmth, while using clear, technically precise language.
- **Comments explain the *why*, not the *what*.** If the code is already self-explanatory, do not
  comment it.
- **Notion.** Every system update must also be reflected in the EtherBound Notion page
  (`EtherBound — Systems Index`), preserving the existing page format, table structure,
  separator before the subindexes, and corresponding icon. When a task is completed, add a concise
  report under its own `Work Reports` subindex; reports must not be mixed into another subindex.
  The `Dev Blog` subindex is a database with one entry per `Dev-XYZ`, `FixNN` or `InfraNN` that
  changes code, created when the doc moves to `docs/done/`; later work on the same doc updates its
  entry instead of adding a new one. Entries use a technical, descriptive title and an icon that
  matches the topic, and fill Kind, Plan, Phase and Modules; Version is filled once the release
  commit exists. Changes to docs, rules or workflow, and small edits without a plan, go only in
  the Work Report. Its entry is the commit's tags and TL;DR under its `HH:MM` heading. Work
  report entries must be pages under the `Work Reports` subindex, titled `Report DD/MM/YYYY`, with
  an icon that matches the report's topic. Work Reports are consolidated by date: before creating a
  report, search for that date and update the existing page. There must be only one Work Report page
  per date, containing all completed work for that day. Inside the daily page, separate updates by
  modification time using `HH:MM` headings, ordered chronologically.
- **Schema changes ship as migrations.** Every change to the database models comes with an Alembic
  migration. Never wipe the savegame to change the schema.
- **Type contract.** Pydantic models are the source of truth; the TypeScript types in the web client
  are generated from the OpenAPI schema, never written by hand.
- **Determinism.** Randomness goes through the seeded RNG stream of its system. No bare
  `random` calls, no wall-clock time inside the simulation. Ruff `TID251` fails a bare `random`,
  `time.time`, `time.time_ns` or `datetime.now` outside `rng.py`, and `test_architecture.py` keeps
  the database imports inside the engine.
- **Before calling something done:** run `npm run check` (after `npm run setup` once per clone for
  the git hooks). Add `npm run check:visual` when the renderer or sprite sheets changed. `Dev-001`
  fixes the exact history; `CONTEXT.md` keeps the commands afterwards.
- On Windows, stop the server before touching the database file: it keeps it locked.
- Kill any zombie server process.

- Development Docs must follow the next format:
  - Version title
  - Development tags (Bugfix, Refactor, Tooling, etc.) surrounded by []
  - Todo list with checkmarks
  - Concise writing
  - At the end a TL;DR.
  - Aim at ~200 lines. Each acceptance item names its test file, its visual shot, or says
    "manual" and why.
  - Test files are named by feature, never by doc.
