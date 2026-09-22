# Pending

Open decisions and loose ends. When one is settled, record the decision where it belongs
(`VISION.md` or the relevant `Dev-XYZ.md`) and delete it from here.

## Open design questions

- [ ] **Verb list.** The exact vocabulary of 40 to 60 generic verbs. First draft due in Phase 1.
- [ ] **Abilities.** How common they are among hybrids, and which ones exist.
- [ ] **City authoring.** Procedural from seed, hand-made in Tiled, or authored landmarks on a
      procedural base.
- [ ] **Carry-over from NikoStory.** Whether Halverton, the authored NPCs and the prompts come
      along. The roll formula already does (`VISION.md` [Sec. 3]).

## Loose ends

- [ ] **Notion.** `Dev-018` and `Dev-019` from NikoStory still hang under the EtherBound page;
      delete them.
- [ ] **NikoStory working tree.** 11 modified files uncommitted in the old repo (`schema.ts`,
      `movement.ts`, `grid.ts`, `Dev-018.md`, ...). Commit them as the closing state or discard
      them.
- [ ] **Silenced server logs.** `server/alembic/env.py` [line 10] calls `fileConfig(...)`
      without `disable_existing_loggers=False`, so every start disables uvicorn's loggers once
      the migrations run: request errors and shutdown never reach `logs\server.log`. Found while
      building `Dev-003`; a server fix.

---

## TL;DR

Four design questions (verbs, abilities, city authoring, carry-over) and three loose ends
(Notion pages, NikoStory's uncommitted changes, the silenced server logs).
