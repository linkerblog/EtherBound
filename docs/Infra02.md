# Infra02: Agent context diet — smaller boot docs, read on demand

[Docs] [Process] [Tooling]

Every agent session and every subagent starts cold by reading `CLAUDE.md`, `AGENTS.md`,
`docs/utils/VISION.md` and `CONTEXT.md` in full: ~68k characters (~17k tokens) before it opens a
single source file. Most of that is area-specific (launcher pitfalls, the storyteller, Notion
rules) or stale. This doc moves text rather than compressing it: nothing is paraphrased
into shorthand, and every removed line either moves to an on-demand doc or is listed as stale
[Sec. 0]. The code is not touched.

**Precondition:** Fix12 is committed and the working tree is clean. No other doc is mid-edit on
`AGENTS.md`, `CONTEXT.md` or `docs/utils/VISION.md`.

## 0. Findings (measured 24/09/2026 on the working tree at `33b86c7`)

| # | Finding |
|---|---|
| F1 | Boot set: `CLAUDE.md` 1.4k chars, `AGENTS.md` 7.5k, `VISION.md` 23.9k, `CONTEXT.md` 34.7k. Read in full at every cold start |
| F2 | `CONTEXT.md` "Measured pitfalls" is 12.9k chars (37% of the file), grouped by nothing: launcher, renderer, BitCanvas and physics pitfalls sit side by side |
| F3 | `CONTEXT.md` opens with a 1.4k paragraph listing which archived `Dev` docs specified what. It is history, and it points into `docs/done/` |
| F4 | `CONTEXT.md` records drifting status: the "Fix12 module versions" bullet, the "Current automated validation" test counts and the versions inside the BitCanvas bullet. `VERSION.md` and the checks are the truth |
| F5 | `CONTEXT.md` "Not yet present" lists tile physics (done in `Dev-013`) and an op list that omits `push`, `pull`, `drag`, `throw`, `hit`, `break` |
| F6 | `VISION.md` [Sec. 8], [Sec. 9], [Sec. 10], [Sec. 13] (minds, society, storyteller, LLM: 6.8k) and [Sec. 15], [Sec. 16] (roadmap: 1.9k) matter only when planning those areas |
| F7 | `VISION.md` still opens with "No code exists yet; nothing is implemented until this document is approved" |
| F8 | `AGENTS.md` "Conventions" is 5.8k; the Notion rule alone is ~2.3k and concerns only the agent that writes reports. "Closing a phase" and the plan format concern only the planner |
| F9 | ~90 references of the form `` `VISION.md` [Sec. N] `` live in active docs, `CONTEXT.md`, `README.md` and `PENDING.md`. Renumbering would break them; nothing checks them today |
| F10 | `Infra01` [Sec. 2] earmarked `Infra02` for CI. No doc was written; CI becomes `Infra03` |

## 1. Decisions to approve

| Topic | Decision | Why |
|---|---|---|
| Method | Move, never compress. Prose stays in normal English; no abbreviations, no telegraphic style | Compressed prose saves little, often tokenizes worse and is misread |
| Pitfalls | All pitfalls move to a new living guide `docs/utils/PITFALLS.md`, one section per area [Sec. 3.2]. `CONTEXT.md` keeps an index table: area → section → paths that trigger it | F2. An agent reads only the section for the area it touches |
| Vision split | Bodies of [Sec. 8], [Sec. 9], [Sec. 10], [Sec. 13] move to `docs/utils/MINDS.md`; [Sec. 15], [Sec. 16] to `docs/utils/ROADMAP.md`. Both keep the **same section numbers**. `VISION.md` keeps every heading with a 2–4 line summary and a "Full text:" pointer | F6 without breaking F9: an old reference still lands on the right heading |
| Agents rules | The Notion rule moves to `docs/utils/NOTION.md`; "Closing a phase" and the plan format move to `docs/utils/PLANS.md`. `AGENTS.md` keeps a one-line pointer for each | F8 |
| Reading map | A table at the top of `AGENTS.md` [Sec. 3.1] says who reads what. **Every agent:** `AGENTS.md`, `CONTEXT.md`. **Planner:** plus `VISION.md`, `PLANS.md`, `PENDING.md`. **Implementer:** plus the plan, the `VISION`/`MINDS`/`ROADMAP` sections it cites and the `PITFALLS` sections for the areas it touches. **Committer:** `COMMITS.md`, `VERSION.md`. **Reporter:** `NOTION.md` | The implementer no longer reads the whole vision; the plan already cites what applies |
| Status in docs | `CONTEXT.md` never records versions, test counts or "verified once" notes. Versions live in `VERSION.md`, and the checks decide what passes | F4. Those lines go stale every commit |
| Budgets | Warning-only character budgets in `scripts/check-sizes.mjs`: `CLAUDE.md` 2k, `AGENTS.md` 5k, `CONTEXT.md` 24k, `VISION.md` 17k; plan docs in `docs/` over 220 lines | Same style as the source-size warning; it keeps the diet from eroding |
| Reference check | New `scripts/check-docs.mjs` **fails** on a `` `X.md` [Sec. N] `` reference whose target file or heading does not exist | F9. Makes the split safe now and every later move |

Expected result: implementer boot ~29k chars (~7.5k tokens, −55%); planner boot ~45k chars
(~11k tokens, −35%). Area docs are read only when needed.

## 2. Out of scope

- Minifying code, stripping comments or shortening identifiers: they cost the agent more than they save.
- Rewriting `Dev-014.md` (407 lines) or any other active plan: the budget warning flags it,
  and its author trims it.
- `PENDING.md` "Manual checks not run" (~9k chars): it is emptied when the phase closes, as `AGENTS.md` already says.
- Blocking `docs/done/` in `.claude/settings.json`: living docs still cite `done/` sections until
  the phase closes, and a deny rule would stop agents from following those citations.
- CI on GitHub Actions: `Infra03`, gets a line in `PENDING.md` [Sec. 3.5].

## 3. What changes

### 3.1 `AGENTS.md` and `CLAUDE.md`
- `AGENTS.md`: new "Reading map" table at the top [Sec. 1]. "The minimum you must not break" is unchanged.
- "Conventions": the Notion bullet becomes one line pointing to `docs/utils/NOTION.md`. "Closing
  a phase" and "Development Docs must follow…" become one line pointing to `docs/utils/PLANS.md`.
  The doc-naming rules (`Dev`, `Fix`, `Infra`, `UPPER_CASE.md` guides) stay, since any agent can
  create a file. The living-guide list adds `PITFALLS.md`, `MINDS.md`, `ROADMAP.md`, `NOTION.md`, `PLANS.md`.
- `CLAUDE.md`: the first paragraph points to the reading map ("Planner" row) instead of listing
  three files "in full". The plan rule is unchanged.

### 3.2 `CONTEXT.md` and `docs/utils/PITFALLS.md`
- Intro: replace the archived-docs paragraph (F3) with two lines: what the file is, and that
  design lives in `VISION.md`.
- "Measured pitfalls" → `PITFALLS.md`, moved verbatim into these sections:

| Sec. | Area | Read when touching |
|---|---|---|
| 1 | Launcher and Windows processes | `launcher/`, server start/stop, the database file |
| 2 | World, generation and grid | `server/src/etherbound/world/`, generator versions |
| 3 | Movement, net and client sync | `server/.../net/`, `web/src/net/`, movement code |
| 4 | Objects and physics | `engine/ops/`, object kinds, `grid.py`/`ChunkStore` volumes |
| 5 | Renderer, atlas and assets | `web/src/game/`, `web/src/world/`, `src/sprites/` |
| 6 | BitCanvas | `BitCanvas/` |
| 7 | Tests and tooling | `scripts/`, Playwright, schema generation |

- `CONTEXT.md` keeps this table as "Pitfalls index" in place of the old section.
- Delete the status bullets of F4. Rewrite "Not yet present" (F5) against `engine/ops.toml` and the
  registered handlers, and list only what the code lacks.
- "Modules", "Data model", "Contracts" and "Commands" are unchanged.

### 3.3 `VISION.md`, `MINDS.md`, `ROADMAP.md`
- `MINDS.md` holds [Sec. 8], [Sec. 9], [Sec. 10], [Sec. 13] verbatim with their original numbers,
  and `ROADMAP.md` holds [Sec. 15] and [Sec. 16]. Each file opens with one line saying it is part of the vision and
  follows the same "vision first" rule.
- In `VISION.md`, each moved section keeps its heading, gets a 2–4 line summary written from its
  own first paragraph and ends with `Full text: docs/utils/MINDS.md [Sec. N]` (or `ROADMAP.md`).
- Replace the F7 sentence with one line about what the vision is today. The TL;DR is unchanged.
- References elsewhere stay as they are (F9); `check-docs.mjs` confirms they still resolve [Sec. 3.4].
  Future references to moved content should name the new file.

### 3.4 Scripts (`tooling`)
- `scripts/check-sizes.mjs`: add the budgets of [Sec. 1] as a second warning block
  (`check-sizes: docs over budget (warning only)`), measured in characters for the boot docs and
  in lines for `docs/*.md` plans (`PENDING.md` excluded).
- `scripts/check-docs.mjs` (Node, no dependencies): scan `*.md` in the root, `docs/` and
  `docs/utils/` (not `docs/done/`). For each `` `path.md` [Sec. N…] `` resolve the file by path or unique
  basename among living docs, then require a heading `## N.` or `### N.M` matching the
  number (the text after a comma, as in `[Sec. 1, Scope]`, is ignored). References into `done/`
  are skipped: they vanish at phase close. The error names the file, line and missing target.
- `package.json`: `check:docs` runs it; `check` and `check:fast` run `check:docs` after `check:versions`.
- `CONTEXT.md` "Commands" lists `check:docs`.

### 3.5 `docs/utils/NOTION.md`, `docs/utils/PLANS.md`, `PENDING.md`, `README.md`
- `NOTION.md`: the Notion bullet of `AGENTS.md` verbatim, split into "Systems Index",
  "Dev Blog" and "Work Reports" sub-headings. No rule changes.
- `PLANS.md`: "Closing a phase", the plan format list and the lifecycle sentences of `docs/`
  (in progress → `docs/done/`), verbatim.
- `PENDING.md` "Planned work": add `Infra03` CI on GitHub Actions (the remote exists now).
- `README.md` "Documentation": list the new guides.

## 4. Modules and versions

| Module | Version |
|---|---|
| tooling | v0.0.12 → v0.0.13 (`scripts/check-sizes.mjs`, `scripts/check-docs.mjs`, `package.json`) |

No `server.*`, `web.*`, `launcher` or `bitcanvas` file changes. The overall version is set at
commit by `COMMITS.md` [Sec. 2] (a Z bump is expected).

## 5. What must not break

- No rule is lost. Every line removed from `AGENTS.md`, `CONTEXT.md` or `VISION.md` either
  appears verbatim in a new guide or is one of F3, F4, F5, F7.
- Every existing `` `X.md` [Sec. N] `` reference still resolves (`check:docs`).
- `AGENTS.md`, `CLAUDE.md` and `CONTEXT.md` remain the root entry points, and an agent that only
  reads `AGENTS.md` (Kilo Code) still finds the Notion, plan and pitfall rules through pointers.
- `EtherBound.exe` still reads the overall line of `VERSION.md`; its format is unchanged.
- `npm run check` still passes; the size warnings do not fail it.

## 6. Acceptance

1. `npm run check:docs` passes. A scratch reference to `` `VISION.md` [Sec. 99] `` makes it fail,
   naming the file and line; revert.
2. `npm run check:sizes` prints no doc-budget warning for the four boot docs, and warns for `Dev-014.md`.
3. `wc -c` on the boot docs: `AGENTS.md` ≤ 5k, `CONTEXT.md` ≤ 24k, `VISION.md` ≤ 17k.
4. `npm run check` passes.
5. **Manual (planner):** `git diff --word-diff` over the three edited docs shows only removals
   that reappear in a new guide or are F3/F4/F5/F7. A text move can only be checked by reading.
6. **Manual (user):** a cold subagent given only the reading map picks the right `PITFALLS`
   section for "change the launcher's port handling" and for "add a new object kind".

## 7. Todo

### Decisions
- [ ] Approve [Sec. 1]

### Docs
- [ ] Reading map and pointers in `AGENTS.md`; `CLAUDE.md` first paragraph [Sec. 3.1]
- [ ] `PITFALLS.md`, pitfalls index, stale bullets, "Not yet present" [Sec. 3.2]
- [ ] `MINDS.md`, `ROADMAP.md`, `VISION.md` summaries [Sec. 3.3]
- [ ] `NOTION.md`, `PLANS.md`, `PENDING.md`, `README.md` [Sec. 3.5]

### Tooling
- [ ] Doc budgets in `check-sizes.mjs` [Sec. 3.4]
- [ ] `check-docs.mjs` and `package.json` scripts [Sec. 3.4]

### Acceptance
- [ ] [Sec. 6] 1–5
- [ ] [Sec. 6] 6 (user)

### Closing
- [ ] `VERSION.md` [Sec. 4]; commit per `COMMITS.md`
- [ ] Notion: Dev Blog entry (Kind `Infra`), Work Report for the date (per `NOTION.md`)
- [ ] Move this doc to `docs/done/`

---

## TL;DR

- Boot docs cost ~17k tokens per cold start; the target is ~7.5k for implementers and ~11k for
  the planner, by moving text, not compressing it.
- Pitfalls go to `PITFALLS.md` by area. Minds, society, storyteller and LLM go to `MINDS.md`, and the
  roadmap to `ROADMAP.md`, with the same section numbers so old references still work.
  Notion rules go to `NOTION.md`, plan and phase rules to `PLANS.md`.
- A reading map in `AGENTS.md` says who reads what; `CONTEXT.md` stops recording versions and test counts.
- `check-docs.mjs` fails on broken `[Sec. N]` references; `check-sizes.mjs` warns on doc budgets.
  `tooling` v0.0.13. CI moves to `Infra03`.
