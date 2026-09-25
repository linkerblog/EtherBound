# AGENTS.md

## Reading map

Read the rows for your role. `VISION.md` holds the pillars, the architecture, the primitives
and the build order; several rules below look arbitrary until you read why they are there.

| Role | Reads |
|---|---|
| Every agent | `AGENTS.md`, `CONTEXT.md` |
| Planner | plus `docs/utils/VISION.md`, `docs/utils/PLANS.md`, `docs/PENDING.md` |
| Implementer | plus the plan, the `VISION`/`MINDS`/`ROADMAP` sections it cites and the `docs/utils/PITFALLS.md` sections for the areas it touches (index in `CONTEXT.md`) |
| Committer | `docs/utils/COMMITS.md`, `docs/utils/VERSION.md` |
| Reporter | `docs/utils/NOTION.md` |

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
  in `docs/utils/` (`VISION`, `MINDS`, `ROADMAP`, `VERSION`, `COMMITS`, `STYLEGUIDE`, `PITFALLS`,
  `NOTION`, `PLANS`); versioned work docs use `Dev-XYZ.md` (`Dev-001`, `Dev-002`, etc., according to
  the development version), reviews use `FixNN.md`, and work that changes only tooling, the launcher,
  tests or process docs uses `InfraNN.md`. A doc that changes any `server.*` or `web.*` module is a
  `Dev`; one that does not is an `Infra`. Before creating any
  `.md` file, check whether it already exists and update or reuse it instead of creating a duplicate.
- **Plans.** Lifecycle (`docs/` → `docs/done/`), format and closing a phase: `docs/utils/PLANS.md`.
- **Vision changes go to `docs/utils/VISION.md` first.** If a decision changes, update the doc, then the code.
- **Versions.** `docs/utils/VERSION.md` lists every module and its version. Every module starts at
  `v0.0.0`; on every modification, bump the affected module's version by `0.0.1` and update
  `docs/utils/VERSION.md` in the same change. A change that touches several modules bumps each of them.
  Its overall project version must match the version assigned to the latest commit. Every commit
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
- **Notion.** Every system update and completed task is reflected in the EtherBound Notion page;
  the rules are in `docs/utils/NOTION.md`.
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
