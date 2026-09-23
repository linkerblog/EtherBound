# Fix11: The Dev Blog becomes a database with one entry per plan

[Docs] [Process]

The user asked on 23/09/2026 what to do about the Notion Dev Blog, which already holds 34
entries. This doc turns the recommendation into a plan. No code changes.

## 1. Findings

Read from Notion on 23/09/2026 (`EtherBound / Dev Blog`, 34 child pages).

### F1: The list shows no date, version, plan or kind
The entries are plain child pages. To learn when an entry landed, which plan it belongs to or
which version shipped it, the reader has to open it.

### F2: The order does not follow the plans
Pages sit in creation order, so `Fix01` comes after `Dev-006` and `Dev-008` sits between the
Fix entries. Nothing groups them by phase.

### F3: One plan, several entries
- **Fix02** has three: *Fix 02: Fresh-Save Clock Event and FIFO Drain Semantics*,
  *Fix02 H3: Snapping Stale Actor Height on Load* and *Fix02: Dev-005 GUI Acceptance and Review
  Closure*.
- **Dev-001** has two about the work itself: *Phase 0 Server Implementation* and *Phase 0 Formal
  Closure and Validation*.

### F4: Process entries sit next to game work
Five entries record changes to rules, docs or workflow, not to the game or its tooling:
*Dev-001: Documentation Structure Update*, *Daily Work Report Consolidation Rule*,
*Time-Sectioned Daily Work Reports*, *Move Living Guides into docs/utils* and *Response Tone
Guidance in AGENTS.md*. Work Reports already covers them.

### F5: The rule creates the growth
[`AGENTS.md`](../AGENTS.md) ("Notion") says: "Every update task must also create a new page under
the `Dev Blog` subindex." Every task, including a rule edit or a follow-up on the same plan, adds
a page.

## 2. Decisions to approve

| # | Topic | Decision | Why |
|---|---|---|---|
| D1 | Structure | An inline database `Entries` inside the existing `Dev Blog` page. The page keeps its title, 📋 icon, URL and place in the EtherBound index | Properties and views fix F1 and F2. Readers and the index keep the same link |
| D2 | Migration | Move the 34 pages into the database. Do not recreate them | Moving keeps each page's URL, content, icon, comments and creation time |
| D3 | Merges | Fix02: 3 → 1. Dev-001 work: 2 → 1 (details in [Sec. 3.3]). Merged-away pages go to the Notion trash, not permanent deletion | One plan, one entry (F3). The trash can restore them for 30 days |
| D4 | Process entries | Keep them as rows with `Kind = Process`, hidden from the default view | Nothing is lost, and the blog reads as game work (F4) |
| D5 | Rule going forward | One entry per `Dev-XYZ` or `FixNN` that changes code, created when the doc moves to `docs/done/`. Later work on the same plan updates that entry. Rule, doc and workflow changes, and small edits without a plan, go only in the Work Report | Stops the growth at its source (F5). Matches the plan rule in `CLAUDE.md` |
| D6 | Phase recaps | When a phase closes, add one `Kind = Recap` entry titled `Phase N Recap` that links the phase's entries. The first one is written when Phase 1 closes | Mirrors how the repo empties `docs/done/` at phase close. The blog reads by chapter |

This fix changes no code, so under D5 it gets a Work Report and no Dev Blog entry.

## 3. What changes

### 3.1 Database schema (`Dev Blog` → `Entries`)

| Property | Type | Values |
|---|---|---|
| Name | title | The page title, kept as is (Fix02 renamed in [Sec. 3.3]) |
| Kind | select | `Dev`, `Fix`, `Change`, `Process`, `Recap` |
| Plan | multi-select | `Dev-001` … `Dev-011`, `Fix01` … `Fix09`. Empty when there is no plan |
| Phase | select | `Phase 0` … `Phase 4` (`docs/utils/VISION.md` [Sec. 15]) |
| Date | created time | Automatic. It survives the move |
| Version | text | Overall project version of the commit that shipped the work, e.g. `v0.7.0`. Empty when unknown |
| Modules | multi-select | Module names from `docs/utils/VERSION.md` (`server.engine`, `web.game`, `launcher`, `bitcanvas`, `tooling`…) |

`Change` is a legacy kind: code shipped without a plan before D5. New entries are only `Dev`,
`Fix` or `Recap`.

**Version** for a `Dev`/`Fix` row comes from the commit that moved the plan to `docs/done/`
(`git log --diff-filter=A --format=%s -- docs/done/<Plan>.md`). For other rows, use the version the
page states if it states one; otherwise leave it empty. **Modules** come from the plan's
"Modules and versions" section, or from the page text.

### 3.2 Views

| View | Layout | Filter | Sort |
|---|---|---|---|
| **Latest** (default) | List | Kind is not `Process` | Date, newest first |
| **By phase** | Board grouped by Phase | Kind is not `Process` | Date, oldest first |
| **All** | Table, every property shown | None | Date, oldest first |

### 3.3 Merges

| Survivor (keeps its URL) | Absorbs | New title |
|---|---|---|
| *Fix 02: Fresh-Save Clock Event and FIFO Drain Semantics* | *Fix02 H3: Snapping Stale Actor Height on Load*, *Fix02: Dev-005 GUI Acceptance and Review Closure* | `Fix02: Dev-005 Review — Clock Event, Drain Semantics, Stale Height and GUI Acceptance` |
| *Dev-001: Phase 0 Server Implementation* | *Dev-001: Phase 0 Formal Closure and Validation* | Unchanged |

For each merge:
1. Append each absorbed page's content to the survivor under an `##` heading with the absorbed
   page's title. Content is copied as is, not rewritten.
2. Search the workspace for links to the absorbed page's URL (Work Reports, Systems Index,
   Roadmap to v1) and point them to the survivor.
3. Move the absorbed page to the trash.

### 3.4 Row mapping

Listed in the page's current order. Phase 0 covers everything up to the Dev-001 closure.

| # | Entry | Kind | Plan | Phase |
|---|---|---|---|---|
| 1 | Dev-001: Phase 0 Server Implementation | Dev | Dev-001 | 0 |
| 2 | Dev-001: Documentation Structure Update | Process | Dev-001 | 0 |
| 3 | Terminal Launcher Styling and Version Display | Change | — | 0 |
| 4 | Launcher Cleanup Supervisor | Change | — | 0 |
| 5 | Single-Console Launcher Process Model | Change | — | 0 |
| 6 | Daily Work Report Consolidation Rule | Process | — | 0 |
| 7 | Time-Sectioned Daily Work Reports | Process | — | 0 |
| 8 | Dev-001: Phase 0 Formal Closure and Validation | → merged into #1 | | |
| 9 | Dev-002: Chunked World Model and Standing-Rule Movement | Dev | Dev-002 | 1 |
| 10 | Native Windows Launcher: Job Objects, Shell-Free Services… | Dev | Dev-003 | 1 |
| 11 | Client HUD: Position and Speed Telemetry Readout | Change | — | 1 |
| 12 | Integer Camera Zoom for the Phaser Client | Dev | Dev-004 | 1 |
| 13 | Launcher Version Display Sourced from Version.md | Change | — | 1 |
| 14 | Move Living Guides into docs/utils | Process | — | 1 |
| 15 | Dev-005: Persistent Event Bus and Generic Action Pipeline | Dev | Dev-005 | 1 |
| 16 | Fix 02: Fresh-Save Clock Event and FIFO Drain Semantics | Fix (survivor) | Fix02 | 1 |
| 17 | Compact Framed Launcher Banner with Global Version | Change | — | 1 |
| 18 | Response Tone Guidance in AGENTS.md | Process | — | 1 |
| 19 | Dev-006: Seeded New-Game Control and Snapshot-Safe World Reset | Dev | Dev-006 | 1 |
| 20 | Fix02 H3: Snapping Stale Actor Height on Load | → merged into #16 | | |
| 21 | Generated Op Menus, Timed Activities and Anchored Digging | Dev | Dev-007 | 1 |
| 22 | Timed Movement Prediction and Server Event Logging | Fix | Fix03 (+ Fix04 if the page covers it) | 1 |
| 23 | Framed Game Viewport and Debug Drawer | Change | — | 1 |
| 24 | Fix01: World Grid, Standing Rules, and Seeded Test-World Repair | Fix | Fix01 | 1 |
| 25 | Dev-008: Isometric Chunk Rendering, Roof Cutaway, and Grass Atlas | Dev | Dev-008 | 1 |
| 26 | Fix02: Dev-005 GUI Acceptance and Review Closure | → merged into #16 | | |
| 27–31 | Fix05 … Fix09 | Fix | Fix05 … Fix09 | 1 |
| 32–34 | Dev-009 … Dev-011 | Dev | Dev-009 … Dev-011 | 1 |

Result: 31 rows; 26 in **Latest**.

For #22, check `docs/done/Fix03.md` and `docs/done/Fix04.md` against the page text: it describes
timed 50 ms steps and event logging, which may span both.

### 3.5 `AGENTS.md`

In "Conventions" → **Notion**, replace:

> Every update task must also create a new page under the `Dev Blog` subindex. Dev Blog
> pages must use a technical, descriptive title and an icon that matches the entry's topic.

with:

> The `Dev Blog` subindex is a database with one entry per `Dev-XYZ` or `FixNN` that changes
> code, created when the doc moves to `docs/done/`; later work on the same doc updates its entry
> instead of adding a new one. Entries use a technical, descriptive title and an icon that
> matches the topic, and fill Kind, Plan, Phase and Modules; Version is filled once the release
> commit exists. Changes to docs, rules or workflow, and small edits without a plan, go only in
> the Work Report.

In **Closing a phase empties `docs/done/`**, before "tag the closing commit", add:

> add a `Phase N Recap` entry (`Kind = Recap`) to the Dev Blog that links the phase's entries;

Nothing else in `AGENTS.md` changes.

### 3.6 Docs
- `docs/PENDING.md`: listed under "Open fixes"; removed once this lands.

## 4. Modules and versions

No module changes. `AGENTS.md`, `docs/` and Notion do not belong to any module in
`docs/utils/VERSION.md`. The user sets the overall project version at commit.

## 5. What must not break

- The `Dev Blog` page keeps its title, 📋 icon, URL and position in the EtherBound index, with the
  separator and subindex order unchanged.
- Every surviving entry keeps its URL, icon and content. Merges only append content.
- Nothing is permanently deleted. Absorbed pages go to the trash.
- No link in Work Reports, the Systems Index or Roadmap to v1 points to a trashed page.
- Work Reports and the Systems Index keep their structure. The only edits there are repointed links.

## 6. Checks and acceptance

1. Fetch `Dev Blog`: it holds the `Entries` database and no loose child pages.
2. The database has 31 rows. Every row has Kind, Phase and Date. Every `Dev`/`Fix` row has Plan.
3. **Latest** shows 26 rows, with *Dev-011* first. **By phase** shows Phase 0 and Phase 1 columns.
4. The Fix02 survivor holds the three original texts under their own headings. The Dev-001
   survivor holds both texts.
5. A workspace search for each trashed page's URL finds no links.
6. The `AGENTS.md` diff is limited to the two passages in [Sec. 3.5].
7. **Manual (user):** open the Dev Blog in Notion and confirm it reads well, newest first, with the
   properties visible.

## 7. Todo

### Decisions
- [ ] Approve [Sec. 2] (D1–D6)

### Notion
- [ ] Create the `Entries` database and its properties inside `Dev Blog` [Sec. 3.1]
- [ ] Move the 34 pages into it [Sec. 3.4]. If the tool cannot move pages into a database, stop
      and update this doc; do not recreate pages
- [ ] Fill Kind, Plan, Phase, Version and Modules [Sec. 3.1], [Sec. 3.4]
- [ ] Merge Fix02 and Dev-001, repoint links, then trash the absorbed pages [Sec. 3.3]
- [ ] Create the three views [Sec. 3.2]

### Repo
- [ ] `AGENTS.md` [Sec. 3.5]

### Checks
- [ ] [Sec. 6] steps 1–6
- [ ] [Sec. 6] step 7 (user)

### Closing
- [ ] `docs/PENDING.md` [Sec. 3.6]
- [ ] Notion: Work Report for the date (no Dev Blog entry, per D5)
- [ ] Move this doc to `docs/done/`

---

## TL;DR

- The Dev Blog has 34 loose pages with no date, plan or kind. Fix02 is split across three pages,
  and five entries are process notes. The `AGENTS.md` rule adds a page for every task.
- Turn the Dev Blog into a Notion database (Kind, Plan, Phase, Date, Version, Modules) with the
  views Latest, By phase and All. Move the pages in without recreating them. Merge Fix02 3 → 1 and
  Dev-001 2 → 1. Process rows stay, hidden from Latest.
- New rule: one entry per `Dev`/`Fix` plan that changes code, updated rather than duplicated.
  Process changes go only to Work Reports. A `Phase N Recap` entry is added when a phase closes.
- No code and no module versions change.
