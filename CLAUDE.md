# CLAUDE.md

Start with [`AGENTS.md`](AGENTS.md) and [`docs/VISION.md`](docs/VISION.md), plus
[`CONTEXT.md`](CONTEXT.md) once it exists. They still apply in full.

## The plan is written before it is executed

When work needs a plan, **the only deliverable of that turn is the `.md` that describes it.**
Write the document, then stop and wait for approval. Do not touch code, do not run migrations,
do not "start with the easy part" while the plan is still on the table.

- The plan goes in `docs/` as `Dev-XYZ.md` (`Dev-001`, `Dev-002`, etc., according to the
  development version), following the naming rules in `AGENTS.md`.
- It states what changes, which modules are affected, which version each one lands on,
  which primitives or systems it touches, and what must not break.
- Only after the document is approved does implementation begin, and it follows the
  document. If reality turns out to be different, update the `.md` first, then keep going.
- A plan that contradicts `docs/VISION.md` updates the vision first, with approval.
- Once the content is implemented, the doc moves to `docs/done/`.

This applies to anything with more than one moving part. A one-line fix, a typo or an
explicitly requested edit does not need a plan; when in doubt, write the `.md`.
