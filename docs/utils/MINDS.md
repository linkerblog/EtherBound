# EtherBound: Minds and society

[Vision] [Minds] [Society]

Part of the vision (`docs/utils/VISION.md`), with its section numbers kept. The same rule applies:
changes go here first, then the code.

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
- The first Extras are six seeded people placed by `world.population` within 15 m of the spawn; their
  deterministic brain (`server.minds`) holds one goal at a time, published as `mind = {anchor, goal}`
  and fulfilled only through the action API, so a restart replays it.
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

- A plan is a list of ops from the shared vocabulary with a goal, never prose.
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
  `{op/tag, context, applies to, penalty}`. Incidents move public opinion; pressure past a
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

## 13. LLM layer

- OpenRouter for Agent strategy and prose; TypeSafe `choice` (Jev) for gates, tactics, free-text
  interpretation and relationship ambiguity.
- Carry over NikoStory's measured lessons: do not pass `reasoning` to a non-reasoning model, keep
  prose models out of state, benchmark before switching a model into the loop.
- Token budget is a design input: cap concurrent LLM Agents.
- **Art authoring.** At authoring time, never during play, an LLM may propose a furniture spec
  (primitives, roles, patterns, seeded slots) through BitCanvas. Code validates it, and BitCanvas
  renders it with the game's projection, light and ramps. The game only loads the resulting PNG.
  The recipe saved next to the sprite (prompt, model, spec, render settings) is its record; it
  decides nothing in the world, so it is not a logged event. Image models (diffusion) are not part
  of the pipeline.
