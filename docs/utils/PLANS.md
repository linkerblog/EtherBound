# EtherBound: Plans

[Guide] [Process] [Planning]

How plan docs (`Dev-XYZ.md`, `FixNN.md`, `InfraNN.md`) live, what they contain and how a phase
closes. The naming rules are in `AGENTS.md`.

## 1. Lifecycle

A versioned doc in `docs/` is in progress; once its content is implemented it moves to
`docs/done/`. `docs/done/` holds the current phase's finished docs only: not maintained, not
consulted as a living reference.

## 2. Format

Development Docs must follow the next format:

- Version title
- Development tags (Bugfix, Refactor, Tooling, etc.) surrounded by []
- Todo list with checkmarks
- Concise writing
- At the end a TL;DR.
- Aim at ~200 lines. Each acceptance item names its test file, its visual shot, or says
  "manual" and why.
- Test files are named by feature, never by doc.
- A reference to the doc's own section is a bare `[Sec. N]`, or ``` `X.md` ([Sec. N]) ``` after a
  file name. ``` `X.md` [Sec. N] ``` cites section N of `X.md`, and `npm run check:docs` verifies it.

## 3. Closing a phase

**Closing a phase empties `docs/done/`.** Once `docs/PENDING.md` has nothing left for the phase:
move whatever in `docs/done/` is still true into `docs/utils/VISION.md`, `CONTEXT.md` or tests
(a "must not break" list becomes tests where it can); make sure no living doc points into
`docs/done/`; add a `Phase N Recap` entry (`Kind = Recap`) to the Dev Blog that links the
phase's entries; tag the closing commit `phase-N-end`; then delete the contents of `docs/done/`.
A manual check still open when its phase closes is automated or dropped, with the reason written
in `docs/PENDING.md`.
Git history is the archive, and nobody reads old plans from it unless the user asks.
