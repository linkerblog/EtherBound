# EtherBound: Vision

[Vision] [Architecture] [Planning]

The living vision of EtherBound, the remake of NikoStory. It records every design decision
taken during the brainstorm and the order in which the game gets built. Every plan is checked
against it. A section ending in "Full text" keeps its body in `MINDS.md` or `ROADMAP.md`.

---

## 1. Pitch

An endless, isometric life sandbox. *Project Zomboid without zombies + RimWorld*, with an LLM
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

Stack changed 25/09/2026 (`Dev-025`): crowds, physics, shaders and a long world need native
speed and a depth buffer.

| Topic | Decision |
|---|---|
| Players | Singleplayer only. No network layer between simulation and client |
| Simulation | C# (.NET) library: world engine, systems, persistence. Deterministic, no engine types, on its own thread |
| Client | Godot 4 (.NET) in the same process: draws snapshots, sends commands, never simulates. Godot UI for text, scenes and panels |
| Type contract | The sim's C# types are the only definition; the client links the sim assembly, so nothing is generated |
| Persistence | SQLite with numbered migrations run by the sim. No "reset to change schema" |
| Camera | Isometric 2:1, fixed (no rotation). Real 3D geometry, orthographic camera (yaw 45°, pitch 30°) rendered to a low-res buffer and scaled by whole factors: pixel art with depth, light, shadows and ambient occlusion. A 1 m tile is a 64×32 px diamond; 0.5 m of height is 16 px. Own pixel art: BitCanvas (`BitCanvas/`) generates seeded textures and furniture shapes; a shape may be proposed by an LLM as a validated primitive spec [Sec. 13]; the LimeZu packs remain a reference base |
| Scale | 1 tile = 1 m, chunked |
| Terrain | Fine heightmap surface (hills, slopes) + building floors + excavable underground |
| Clock | 1 real s = 1 game min by default, configurable. Pause, x1/x3/x10. Autopause in scenes |
| Distance | Walking only at start, accepting time distortion. No transport until core systems are consolidated |
| Rolls | Carried over from NikoStory: d20 + stat + skill×2 against 0/10/14/18/22; natural 1 disaster, natural 20 critical, partial success with a cost |
| Controls | WASD movement, right-click context menu, `V` radial menu, Enter opens free text |
| LLM | OpenRouter (strategy, prose) + TypeSafe `choice` a.k.a. Jev (gates, tactics, interpretation) |
| Protagonist | Always Niko. Cannot die, only be incapacitated. Infinite progression |
| Ether | Only Niko has it |
| Adult content | Explicitly allowed [Sec. 12] |
| Game length | Endless |

## 4. Architecture

```text
  Decision sources            Action API             World engine              Event bus
 ┌──────────────────┐     ┌────────────────┐     ┌──────────────────┐     ┌────────────────────┐
 │ Player (WASD/UI) │     │ op + target    │     │ validate         │     │ Storyteller        │
 │ Agent (LLM/Jev)  │ ──► │ + modifiers    │ ──► │ resolve (dice)   │ ──► │ NPC minds          │
 │ Extra (rules)    │     │ (one API)      │     │ physics, time    │     │ Knowledge/rumors   │
 └──────────────────┘     └────────────────┘     │ commit to state  │     │ Economy, law, orgs │
          ▲                                      └──────────────────┘     │ Prose, UI, storage │
          └──────────────────────── perceive / react ◄────────────────────└────────────────────┘
```

- **World engine** is the only writer of state. It validates preconditions, resolves rolls and
  physics, advances time and emits events.
- **Systems never call each other.** They subscribe to events and request changes through the
  engine; only the engine enqueues events. A new system is a new subscriber; if it needs to change
  the core, stop and rethink.
- **Clock:** the sim owns a 1 Hz logic tick (one game minute). The client interpolates between
  snapshots; WASD is a `move` command the sim validates like any action, with no prediction.
- **Async LLM:** the world does not wait for a model. An Agent keeps executing its current plan
  while the LLM thinks; the answer is re-validated on arrival. Calls in flight during a pause are
  applied on resume, never mid-scene.

## 5. World model

Phase 1 fixes the spatial units used by the world engine: one metre tiles are grouped in 32×32
metre chunks, surface elevation is an integer number of half-metres, and `z` is an absolute three
metre band (`floor(h / 6)`). Every walkable spot has a standing elevation. A body may step to a
neighbouring tile when the difference is at most 0.5 m, it has 2 m of headroom, and no wall blocks
the shared edge. The body occupies the open interval above its standing surface through `h + 4`;
the three half-metre cells `h + 1` through `h + 3` must be clear, so a slab exactly 2 m overhead
touches but does not intersect the body. Its centre stays at least 0.3 m from a blocked wall edge.
Stairs and ramps are therefore ordinary graded tiles. A rise of 1 m or 1.5 m to an orthogonal
neighbour takes the `climb` op; anything higher needs a ladder or another vertical link.

- **Grid:** 1 m tiles in chunks. Only existing levels are stored (sparse).
- **Surface:** heightmap in fine steps (about 0.5 m). Slopes cost movement time and energy, block
  line of sight, and give view from the top. Rendered as isometric terraces: every height step is a
  vertical face, so a 0.5 m step, a 1 to 1.5 m climb and a cliff read differently. Height shading
  stays.
- **Buildings:** sit on a levelled base; floors are about 3 m, relative to that base. A building on
  a slope can have a basement exposed on one side. Niko is roofed when a slab lies above him in his
  column or an unwalled neighbour, or when he is underground. While roofed, floors, walls and
  in-building ground more than 2 m above him are hidden. Otherwise, a building that stands between
  him and the camera hides its storeys above him in the same way. Whatever still covers him, he is
  drawn as a silhouette on top. Walls on his floor that stand in front of him are cut down to a
  stub.
- **Underground:** discrete z-levels of material layers (soil, rock, pipes, water) that can be
  excavated. A dug hole is a space: it shelters, floods, collapses. Digging from the surface
  lowers the heightmap and records a per-tile `dug` depth, so the strata stay anchored to the
  original ground instead of sinking with it.
- **Storage:** below the surface, untouched space is implicit solid material from per-chunk strata;
  only excavated voids and constructed levels are stored. Walls occupy tile edges, while doorways
  and windows are edge openings.
- **Vertical links:** stairs, lifts, fire escapes, sewers, ramps. A `climbable` cell connects
  standing surfaces in its column across otherwise impassable height gaps. Pathfinding is A* over
  standing spots with those vertical links and the same edge rules as movement. Flight is a
  movement mode over the same world.
- **Beyond the city:** forest, river, hills, simulated at lower detail.
- **Objects:** items, containers and furniture are instances of data-defined kinds made of
  registered materials. A solid object fills the half-metre cells above its resting surface, so it
  blocks, can be climbed and can be stacked; a surface object's top is a standing surface. Actors
  carry objects in two hands and in worn slots; carried mass slows them (`Dev-012`).
- **Physics (tile-based, not a physics engine):** bodies have mass; a hit is an impulse; the body
  travels tile by tile; on collision, impact energy against material resistance decides whether the
  wall breaks (becomes rubble and an opening) or the body takes the damage. The client only
  animates the trajectory the sim computed; Godot's physics engine is never used for game state.
  Decided on 23/09/2026 (`Dev-013`):
  - **Resolved at action time, not per tick.** The 1 Hz clock cannot simulate seconds; a push
    computes the whole path inside one `submit` and commits one event carrying it.
  - **SI units:** kg, m/s, joules. Material `resistance` becomes the energy half a metre of the
    material withstands, calibrated on real cases (a fist does not break brick, a sledgehammer
    takes several blows, a car at 50 km/h goes through).
  - **Data-defined strikes.** A held `Tool` may supply `strike_speed_m_s`; strike energy is derived
    from that speed and the tool's mass. Bare hands use the standardized effective mass and speed
    in the physics profile. Tool capabilities stay in object data, not per-tool code.
  - **Effort profile.** Shove, throw, bare-hand and friction constants are data in the physics
    profile (values in `CONTEXT.md`, Physics).
  - **Cumulative damage.** Integrity is remaining joules. An intact object starts at its material
    resistance times its height in half-metre cells (at least one cell); a wall edge spans six
    cells. Partial wall damage is stored sparsely against its canonical north/west edge. At zero an
    object becomes data-defined rubble and spills its contents onto the supported surface; a wall
    edge becomes an opening. `Object.integrity` stores object damage.
  - **Gravity.** Unsupported objects and bodies fall to the next supported surface in the same
    action resolution. A body falling more than about 3 m emits an impact with potential energy
    `mass × 9.81 × height`; objects emit an impact on landing. Health remains the body system's
    responsibility.
  - **Body data.** Every actor has a positive mass in kilograms; existing actors receive an 80 kg
    default until character data supplies an individual value.
  - **Replay.** The logged resolution event contains the complete deterministic path, impacts,
    damage and break outcomes. Chunk changes are replication signals, not physics facts.
  - **Physics does not know health.** It emits `impact {target, energy}`; turning that into
    injury belongs to the body system.
  - Out of scope: structural collapse of buildings, fluids and fire. Hole collapse comes later,
    from soil stability.

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
8. **Generic ops:** 40 to 60 ops shared by every actor [Sec. 7].

**Litmus test for any new idea:** can it be done with data and properties on these primitives?

## 7. Actions and player interaction

An **op** (operation) is one entry of the shared vocabulary (`dig`, `take`, `talk`). An **action**
is one submitted use of an op: the op, its target and its modifiers. Actors never act outside the
ops. The vocabulary is data (`server/src/etherbound/engine/ops.toml`); a code handler gives an op
its behaviour, and an op without one is never offered. An op that takes game time runs as an
**activity** on the actor, advanced by clock ticks and interrupted by the actor's next action.

- **Ops × properties.** No authored "burn Marco's shop": there is `ignite`, and wood is
  flammable.
- **Right-click menu is generated**, never authored: the server returns the ops applicable to the
  target given its properties, the situation and what Niko carries (a locked door offers `open`,
  `knock`, `force`, and `break` with enough strength).
- **A `V` radial menu** shows the same generated entries for Niko's own tile, arranged around him,
  so the player reaches every action with one key and without aiming.
- **Free text (Enter):** Jev maps it to op + target + modifiers. If nothing fits, the action is
  narrative-only (no mechanical effect) or a generic attempt resolved by a roll with consequences
  from a fixed table. The LLM interprets intent; it never invents rules.
- **Scenes:** dialogue and CYOA moments autopause the world, show generated options plus free text.

## 8. Characters and minds

Niko is fixed, cannot die (only be incapacitated) and progresses without cap; Ether is his alone.
NPCs are Extras (rules and utility), Agents (LLM near, Jev far, rules very far) or Extras on a
relevance turn. Three brains split the work (LLM strategy, Jev tactics, code routine); traits shift
utility weights per tag, reactions need knowledge, and relationships are multi-axis and asymmetric.

Full text: `docs/utils/MINDS.md` [Sec. 8]

## 9. Emergent society

Witnessed events spread as knowledge and rumors through the social graph. Prices, jobs, the
internet channel, factions and laws emerge from data and utility: organizations post jobs, factions
form from shared situations, and laws are data rows proposed under public pressure.

Full text: `docs/utils/MINDS.md` [Sec. 9]

## 10. Storyteller

Code filters which events are possible, Jev picks the one that fits (or none), the world engine
commits it as a fact, and the full LLM only writes prose or wakes an Agent. The storyteller also
runs long arcs and grants relevance turns.

Full text: `docs/utils/MINDS.md` [Sec. 10]

## 11. Determinism and replay

- Seeded RNG, one stream per system, so the same seed and inputs give the same city.
- LLM and Jev are not deterministic: **every model decision is logged as an event.** A run can be
  replayed exactly, which is also the main debugging tool ("why did this law pass?").

## 12. Content policy

Adult and sexual content is explicitly allowed and not limited by prose filters. Two rules are
enforced by the engine as op preconditions, not only in prompts:

- **Adults only.** The life cycle creates children; they are excluded from every sexual op by
  code.
- **Consent from every party**, evaluated from traits, relationship and state. Niko only consents if
  the player chooses so.

## 13. LLM layer

OpenRouter serves Agent strategy and prose; Jev serves gates, tactics and free-text
interpretation. NikoStory's lessons carry over, the token budget caps concurrent LLM Agents, and at
authoring time only, an LLM may propose furniture specs that BitCanvas validates and renders.

Full text: `docs/utils/MINDS.md` [Sec. 13]

## 14. What must not break

- The world engine is the only writer of state.
- One action API for player, Agents and Extras.
- Systems talk only through the event bus.
- Reactions come from knowledge, never from omniscience.
- Menus and jobs are generated, never authored.
- Schema changes ship as migrations.
- Every model decision is logged.

## 15. Roadmap

Phase 0 skeleton, Phase 1 core without LLM, Phase 2 a one-block vertical slice whose acceptances
must produce unprogrammed behavior, Phase 3 minds, Phase 4 scale.

Full text: `docs/utils/ROADMAP.md` [Sec. 15]

## 16. Open questions

Tracked in `docs/PENDING.md`.

---

## TL;DR

EtherBound is an endless isometric life sandbox (Zomboid + RimWorld + LLM) where Niko, the only
Ether bearer, lives in a city of hybrids. Singleplayer C# sim + Godot, 3D ortho pixel art, SQLite
with migrations, 1 s = 1 min with pause. Eight primitives and one shared op API make jobs, factions, laws and consequences
emerge instead of being scripted. Extras run on rules, Agents on LLM (strategy) + Jev (tactics),
the storyteller proposes and the world engine alone commits. Build order: skeleton → core without
LLM → one-block vertical slice that must produce unprogrammed behavior → minds → scale.
