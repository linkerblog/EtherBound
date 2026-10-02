# EtherBound: Roadmap

[Vision] [Planning]

Part of the vision (`docs/utils/VISION.md`), with its section numbers kept. The same rule applies:
changes go here first, then the code.

## 15. Roadmap

### Phase 0: Skeleton
- [x] Create the repo, `AGENTS.md`, `CONTEXT.md`, `docs/utils/VERSION.md`
- [x] FastAPI app with WebSocket, 1 Hz clock, pause and speeds
- [x] SQLite + Alembic, first migration
- [x] Phaser client: top-down map, WASD with prediction, right-click menu stub, HTML overlay
- [x] OpenAPI → TypeScript type generation in the build

### Phase 1: Core (no LLM)
- [x] World model: chunks, heightmap, floors, underground levels, materials
- [ ] The eight primitives as data models
- [x] Op vocabulary and action API; generated context menus
- [x] Event bus and action pipeline
- [ ] Witnesses, knowledge, rumor propagation
- [x] Tile physics: impulse, knockback, breakable walls
- [ ] Seeded RNG streams and the decision log
- [x] Isometric renderer with placeholder art (`Dev-008`)
- [x] Port to a C# sim and a Godot 3D orthographic client, same behaviour (`Dev-025`)

### Phase 1.5: Playable loop
Pulled forward from Phase 3 on purpose (`Dev-007`, `Dev-008`): the player types what Niko does and reads what
happened, on the ops Phase 1 already has. No NPC mind is driven by a model yet.
- [x] Jev runtime: free text becomes one of the engine's own generated menu entries (`Dev-007`)
- [x] OpenRouter client, role config, spend cap and a narrator that only knows what Niko perceived (`Dev-008`)
- [x] **Acceptance:** the Jev bench and the narrator bench pass their written rules with a live key (01/10/2026)

### Phase 1.6: Endless terrain and minimap
Pulled forward from Phase 4 ("wilderness") before the vertical slice (`Dev-010`): the terrain streams in
from the seed with no edge, and the HUD shows a minimap around Niko. Roads, buildings and the city stay in
Phase 4 and in "City authoring" (`docs/PENDING.md`).
- [x] Streaming generator `infinite`: pure per-chunk terrain, lazy load, eviction, persist only modified chunks
- [x] Minimap: a local colour window around Niko from the same sampler
- [x] **Acceptance:** the same chunk is byte-identical whatever the visit order, and Niko can walk any direction without hitting an edge (`EndlessTerrainRules.cs`, `EndlessTraversalRules.cs`)

### Phase 2: Vertical slice
One block: a pizzeria, an alley, a park, about 20 Extras.
- [ ] Needs and utility without traits: hunger, thirst, rest, `eat`/`drink`/`sleep`, a utility step (`Dev-011`)
- [ ] Trait weights, organizations posting tasks
- [ ] **Acceptance:** a delivery job appears without being programmed; Niko can take it
- [ ] **Acceptance:** selling in the alley works and reacts to traffic, law and witnesses
- [ ] **Acceptance:** Niko can dig a hole in the park and enter it; someone may notice
- [ ] **Acceptance:** a hit seen by a witness turns into a rumor and a reaction days later

### Phase 3: Minds
- [ ] Jev integration: storyteller gate, tactics (free text shipped in Phase 1.5)
- [ ] Agents with LLM plans, relevance LOD, failure taxonomy, anti-loop
- [ ] Relevance turns and permanent promotion
- [ ] Scenes with autopause, options and prose

### Phase 4: Scale
- [ ] Full city (the endless wilderness is Phase 1.6)
- [ ] Transport (bus, metro, taxi, owned car), only once core systems are consolidated
- [ ] Internet channel
- [ ] Abilities distribution, Ether and its strain
- [ ] Factions, public opinion, laws
- [ ] Life cycle, memory compaction, city history

## 16. Open questions

Tracked in [`PENDING.md`](../PENDING.md).
