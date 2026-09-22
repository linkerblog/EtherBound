# EtherBound: Vision

[Vision] [Architecture] [Planning]

The living vision of EtherBound, the remake of NikoStory. It records every design decision
taken during the brainstorm and the order in which the game gets built. No code exists yet;
nothing is implemented until this document is approved.

---

## 1. Pitch

An endless, top-down life sandbox. *Project Zomboid without zombies + RimWorld*, with an LLM
narrative layer on top. You play Niko Unit Zero, a bioengineered combat unit from another dimension
and the only Ether bearer, in a city of hybrids, some of whom develop abilities.

You can do practically anything: deliver pizzas, sell in an alley, dig a hole and live in it, fly
off and live in the forest, go viral online. **None of it is programmed as a feature.** It comes
out of how the world is built, and the world reacts in ways that are correct but unexpected.

## 2. Pillars

1. **Systemic, not scripted.** Few generic primitives combine into behavior nobody wrote. When an
   idea needs special code, either a primitive is missing or the idea does not belong.
2. **The answer is in real life.** Every open design question is settled by asking how it works
   in reality.
3. **The world reacts to what it knows**, not to what happened. No witnesses means no consequence.
4. **Everyone acts through the same door.** Player, Agents and extras use one action API.
5. **LLMs propose, code decides.** No model ever writes state directly.
6. **Endless and replayable.** Seed plus player choices give a different city history every run.

## 3. Fixed decisions

| Topic | Decision |
|---|---|
| Backend | Python, FastAPI, WebSocket for world streaming |
| Frontend | Phaser 3 for the world + HTML overlay for text, scenes and panels |
| Type contract | Pydantic models → OpenAPI → generated TypeScript types |
| Persistence | SQLite with real migrations (SQLAlchemy + Alembic). No "reset to change schema" |
| Camera | Top-down 3/4, LimeZu assets (reused from NikoStory) |
| Scale | 1 tile = 1 m, chunked |
| Terrain | Fine heightmap surface (hills, slopes) + building floors + excavable underground |
| Clock | 1 real s = 1 game min by default, configurable. Pause, x1/x3/x10. Autopause in scenes |
| Distance | Walking only at start, accepting time distortion. No transport until core systems are consolidated |
| Rolls | Carried over from NikoStory: d20 + stat + skill×2 against 0/10/14/18/22; natural 1 disaster, natural 20 critical, partial success with a cost |
| Controls | WASD movement, right-click context menu, Enter opens free text |
| LLM | OpenRouter (strategy, prose) + TypeSafe `choice` a.k.a. Jev (gates, tactics, interpretation) |
| Protagonist | Always Niko. Cannot die, only be incapacitated. Infinite progression |
| Ether | Only Niko has it |
| Adult content | Explicitly allowed [Sec. 12] |
| Game length | Endless |

## 4. Architecture

```text
  Decision sources            Action API             World engine              Event bus
 ┌──────────────────┐     ┌────────────────┐     ┌──────────────────┐     ┌────────────────────┐
 │ Player (WASD/UI) │     │ verb + target  │     │ validate         │     │ Storyteller        │
 │ Agent (LLM/Jev)  │ ──► │ + modifiers    │ ──► │ resolve (dice)   │ ──► │ NPC minds          │
 │ Extra (rules)    │     │ (one API)      │     │ physics, time    │     │ Knowledge/rumors   │
 └──────────────────┘     └────────────────┘     │ commit to state  │     │ Economy, law, orgs │
          ▲                                      └──────────────────┘     │ Prose, UI, storage │
          └──────────────────────── perceive / react ◄────────────────────└────────────────────┘
```

- **World engine** is the only writer of state. It validates preconditions, resolves rolls and
  physics, advances time and emits events.
- **Systems never call each other.** They subscribe to events and emit new ones. A new system is a
  new subscriber; if it needs to change the core, stop and rethink.
- **Clock:** the server owns a 1 Hz logic tick (one game minute). Phaser interpolates between
  snapshots. WASD movement uses client prediction with server correction.
- **Async LLM:** the world does not wait for a model. An Agent keeps executing its current plan
  while the LLM thinks; the answer is re-validated on arrival. Calls in flight during a pause are
  applied on resume, never mid-scene.

## 5. World model

- **Grid:** 1 m tiles in chunks. Only existing levels are stored (sparse).
- **Surface:** heightmap in fine steps (about 0.5 m). Slopes cost movement time and energy, block
  line of sight, and give view from the top. Rendered as terraces with cliff edges, ramps and height
  shading.
- **Buildings:** sit on a levelled base; floors are about 3 m, relative to that base. A building on
  a slope can have a basement exposed on one side. Rendered one floor at a time with roof cutaway.
- **Underground:** discrete z-levels of material layers (soil, rock, pipes, water) that can be
  excavated. A dug hole is a space: it shelters, floods, collapses.
- **Vertical links:** stairs, lifts, fire escapes, sewers, ramps. Pathfinding is a per-level A*
  plus vertical edges. Flight is a movement mode over the same world.
- **Beyond the city:** forest, river, hills, simulated at lower detail.
- **Physics (tile-based, not a physics engine):** bodies have mass; a hit is an impulse; the body
  travels tile by tile; on collision, impact energy against material resistance decides whether the
  wall breaks (becomes rubble and an opening) or the body takes the damage. Phaser only animates the
  trajectory the server computed.

## 6. Primitives

Everything in the game is built from these eight:

1. **Matter with properties:** objects, terrain and buildings made of materials (flammable,
   diggable, edible, valuable, illegal, heavy...).
2. **Mutable 3D space:** surface, floors, underground. Can be dug, built, broken.
3. **Needs:** hunger, sleep, money, safety, social, shelter, temperature. People and organizations
   have them.
4. **Organizations as actors:** businesses, police, government, gangs, factions. They have goals,
   money, territory, reputation and delegate tasks.
5. **Tasks and contracts:** "do X, get Y". Jobs, errands, favors and crimes for hire are all this.
6. **Ownership and law:** who owns what, what is forbidden where. Laws are data [Sec. 9].
7. **Knowledge and channels:** perceive, tell, call, post. Internet is a channel with mass reach.
8. **Generic verbs:** 40 to 60 verbs shared by every actor.

**Litmus test for any new idea:** can it be done with data and properties on these primitives?

## 7. Actions and player interaction

- **Verbs × properties.** No authored "burn Marco's shop": there is `ignite`, and wood is
  flammable.
- **Right-click menu is generated**, never authored: the server returns the verbs applicable to the
  target given its properties, the situation and what Niko carries (a locked door offers `open`,
  `knock`, `force`, and `break` with enough strength).
- **Free text (Enter):** Jev maps it to verb + target + modifiers. If nothing fits, the action is
  narrative-only (no mechanical effect) or a generic attempt resolved by a roll with consequences
  from a fixed table. The LLM interprets intent; it never invents rules.
- **Scenes:** dialogue and CYOA moments autopause the world, show generated options plus free text.

## 8. Characters and minds

### Niko

- Fixed character; his traits (literal, calm, introverted, easily overwhelmed) weigh on prose and on
  what certain options cost him.
- **Incapacitation, not death.** What happens next depends on who finds him: hospital and a bill,
  arrest if wanted, kidnapping by a faction curious about Ether, or waking up robbed in an alley.
- **Infinite progression** on a logarithmic curve. Tension is kept by systemic reaction (the
  stronger and better known Niko is, the more attention he draws), never by level-scaled enemies.
  Heavy Ether use strains him. Reputation, money, property and influence also grow without cap.
- **Ether is unique.** Witnesses do not know what they saw; rumors, recordings and internet spread
  it; scientists, government and factions take interest on their own.

### NPC tiers

| Tier | Brain | Notes |
|---|---|---|
| Extra (Figurante) | Deterministic rules + utility | Assigned history, abilities matching traits. Token-free |
| Agent (Agente) | LLM near/relevant, Jev when far, rules when very far | Full tools: interaction, thoughts, reasoning |
| Relevance turn | Extra temporarily run as an Agent | Chosen by the storyteller |

- A relevance turn must leave **facts and traits** behind that the deterministic layer understands,
  not just prose. An Extra can be promoted to Agent permanently.
- LOD is by **relevance**, not raw distance: distance + relationship with Niko + active plot
  involving him.

### Three brains

| Brain | Role |
|---|---|
| LLM | **Strategy:** forms goals and plans |
| Jev | **Tactics:** picks the next step among legal actions |
| Code | **Routine** and deterministic failures |

**Plan loop:** plan → execute → on failure, classify:

| Failure | Example | Resolved by |
|---|---|---|
| Deterministic | Locked door, blocked path, shop closed | Code: alternate route, wait, knock, find key |
| Broken precondition | Target is no longer there | Jev: alternate step within the plan |
| Semantic surprise | Target shows up with armed friends | LLM: rethink the goal (queued if not available) |

- A plan is a list of verbs from the shared vocabulary with a goal, never prose.
- Anti-loop: after N failures on the same step, drop or change the goal.
- Replanning is triggered (plan done, semantic failure, relevant event, long periodic refresh),
  never every tick.

### Traits → utility

- Responses are generic and tagged (violent, risky, covert, planned, legal, social, protective...).
- Each trait only shifts weights per tag. Context adds weight (relationship, relative strength,
  witnesses, abilities on both sides). Pick with some randomness among the top options.
- Combinations are never written; they emerge. Aggressive + Fearless attacks now; Smart +
  Aggressive ambushes unseen; Fearful + Smart moves the sister away.
- Reactions need **knowledge**: X reacts only once X learns of the event, and the reaction becomes a
  persistent goal that survives days of routine.

### Relationships

- Multi-axis (affection, trust, respect, fear, attraction) and asymmetric.
- Code + traits give the expected effect of an event; random noise varies each interaction; Jev
  only interprets ambiguous cases (mostly the player's free text).

### Life cycle

- NPCs age, die, have children, migrate in and out.
- Old memories compact into facts ("Z hit my sister years ago" → grudge against Z).
- A city history log of major events feeds the storyteller and the Agents; local legends emerge.

## 9. Emergent society

- **Knowledge and rumors:** every event has witnesses (perception, line of sight, height, light,
  noise). Knowledge spreads through the social graph, distorts, reaches people who were not there.
- **Economy:** macro inflation is a slow controlled drift (about 3% a year, events can shift it);
  micro prices come from local supply and demand. Each merchant has a stable personal margin from
  seed and traits (Greedy: high margin plus small per-sale noise), bargained against Niko's rapport.
  Money sinks (rent, taxes, spoilage, repairs) keep it from piling up.
- **Jobs:** organizations with unmet demand post paid tasks. Anyone can take them. If the business
  dies, so does the job.
- **Internet:** a channel reached through devices that need power and connection. Listings, job
  posts, messages, social media (rumors with huge reach), online shopping with delivery.
- **Factions:** a periodic deterministic pass looks for connected NPCs in similar situations
  (shared need, grievance, ability, neighborhood, class). Density + catalyst (a leader trait or a
  shared event, gated by Jev) forms a faction. Goal comes from the shared situation; modus operandi
  from aggregated member traits. Factions are organizations; they grow, split, merge, dissolve.
  War appears when opposing goals compete for the same territory or resource.
- **Laws:** there are no hybrid laws at start. A law is a data row
  `{verb/tag, context, applies to, penalty}`. Incidents move public opinion; pressure past a
  threshold makes politicians (with traits) propose laws; utility + Jev decide; police enforce;
  people react. Laws can be repealed. Different seeds end with different legal systems.

## 10. Storyteller

```text
Trigger (time / event / pacing)
  → candidates: code filters what is POSSIBLE (preconditions, cooldowns, tension budget)
  → Jev: which one FITS now, or none (+ confidence as threshold)
  → world engine commits it as a fact
  → full LLM only for prose or to wake an Agent
```

It also runs long arcs (a gang growing, a neighborhood gentrifying) and grants relevance turns.

## 11. Determinism and replay

- Seeded RNG, one stream per system, so the same seed and inputs give the same city.
- LLM and Jev are not deterministic: **every model decision is logged as an event.** A run can be
  replayed exactly, which is also the main debugging tool ("why did this law pass?").

## 12. Content policy

Adult and sexual content is explicitly allowed and not limited by prose filters. Two rules are
enforced by the engine as verb preconditions, not only in prompts:

- **Adults only.** The life cycle creates children; they are excluded from every sexual verb by
  code.
- **Consent from every party**, evaluated from traits, relationship and state. Niko only consents if
  the player chooses so.

## 13. LLM layer

- OpenRouter for Agent strategy and prose; TypeSafe `choice` (Jev) for gates, tactics, free-text
  interpretation and relationship ambiguity.
- Carry over NikoStory's measured lessons: do not pass `reasoning` to a non-reasoning model, keep
  prose models out of state, benchmark before switching a model into the loop.
- Token budget is a design input: cap concurrent LLM Agents.

## 14. What must not break

- The world engine is the only writer of state.
- One action API for player, Agents and Extras.
- Systems talk only through the event bus.
- Reactions come from knowledge, never from omniscience.
- Menus and jobs are generated, never authored.
- Schema changes ship as migrations.
- Every model decision is logged.

## 15. Roadmap

### Phase 0: Skeleton
- [x] Create the repo, `AGENTS.md`, `CONTEXT.md`, `docs/Version.md`
- [x] FastAPI app with WebSocket, 1 Hz clock, pause and speeds
- [x] SQLite + Alembic, first migration
- [x] Phaser client: top-down map, WASD with prediction, right-click menu stub, HTML overlay
- [x] OpenAPI → TypeScript type generation in the build

### Phase 1: Core (no LLM)
- [ ] World model: chunks, heightmap, floors, underground levels, materials
- [ ] The eight primitives as data models
- [ ] Verb vocabulary and action API; generated context menus
- [ ] Event bus, witnesses, knowledge, rumor propagation
- [ ] Tile physics: impulse, knockback, breakable walls
- [ ] Seeded RNG streams and the decision log

### Phase 2: Vertical slice
One block: a pizzeria, an alley, a park, about 20 Extras.
- [ ] Needs, utility with trait weights, organizations posting tasks
- [ ] **Acceptance:** a delivery job appears without being programmed; Niko can take it
- [ ] **Acceptance:** selling in the alley works and reacts to traffic, law and witnesses
- [ ] **Acceptance:** Niko can dig a hole in the park and enter it; someone may notice
- [ ] **Acceptance:** a hit seen by a witness turns into a rumor and a reaction days later

### Phase 3: Minds
- [ ] Jev integration: free text, storyteller gate, tactics
- [ ] Agents with LLM plans, relevance LOD, failure taxonomy, anti-loop
- [ ] Relevance turns and permanent promotion
- [ ] Scenes with autopause, options and prose

### Phase 4: Scale
- [ ] Full city, wilderness
- [ ] Transport (bus, metro, taxi, owned car), only once core systems are consolidated
- [ ] Internet channel
- [ ] Abilities distribution, Ether and its strain
- [ ] Factions, public opinion, laws
- [ ] Life cycle, memory compaction, city history

## 16. Open questions

Tracked in [`PENDING.md`](PENDING.md).

---

## TL;DR

EtherBound is an endless top-down life sandbox (Zomboid + RimWorld + LLM) where Niko, the only
Ether bearer, lives in a city of hybrids. FastAPI + Phaser, SQLite with migrations, 1 s = 1 min
with pause. Eight primitives and one shared verb API make jobs, factions, laws and consequences
emerge instead of being scripted. Extras run on rules, Agents on LLM (strategy) + Jev (tactics),
the storyteller proposes and the world engine alone commits. Build order: skeleton → core without
LLM → one-block vertical slice that must produce unprogrammed behavior → minds → scale.
