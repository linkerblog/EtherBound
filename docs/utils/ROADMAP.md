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

Tracked in [`PENDING.md`](../PENDING.md).
