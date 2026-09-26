# Versions

Overall project version: `v2.5.0`

Every module and its current version. Modules start at `v0.0.0`; each modification bumps its
version by `0.0.1`, and the bump ships in the same change. The overall project version tracks the
version assigned to the latest commit. The launcher banner and console title display this overall
version from `docs/utils/VERSION.md`, not the launcher's module version; keep both displays in sync
whenever the overall version changes.

| Module | Path | Version |
|---|---|---|
| server.app | `server/src/etherbound/app.py`, `server/src/etherbound/config.py`, `server/src/etherbound/routes/` | v0.0.11 |
| server.clock | `server/src/etherbound/clock.py` | v0.0.1 |
| server.engine | `server/src/etherbound/engine/` | v0.0.15 |
| server.events | `server/src/etherbound/events/` | v0.0.7 |
| server.minds | `server/src/etherbound/minds/` | v0.0.2 |
| server.net | `server/src/etherbound/net/` | v0.0.11 |
| server.db | `server/src/etherbound/db/`, `server/alembic/` | v0.0.9 |
| server.rng | `server/src/etherbound/rng.py` | v0.0.2 |
| server.world | `server/src/etherbound/world/` | v0.0.9 |
| web.world | `web/src/world/` | v0.0.12 |
| web.game | `web/src/game/` | v0.1.19 |
| web.net | `web/src/net/` | v0.0.15 |
| web.ui | `web/src/ui/` | v0.0.13 |
| launcher | `launcher/` | v0.0.4 |
| bitcanvas | `BitCanvas/` | v0.0.6 |
| sim.rng | `sim/EtherBound.Sim/Rng/` | v0.0.1 |
| sim.world | `sim/EtherBound.Sim/World/` | v0.0.1 |
| sim.events | `sim/EtherBound.Sim/Events/`, `sim/EtherBound.Sim/Core/` | v0.0.1 |
| sim.db | `sim/EtherBound.Sim/Db/` | v0.0.1 |
| sim.engine | `sim/EtherBound.Sim/Engine/`, `sim/EtherBound.Sim/Clock/` | v0.0.1 |
| sim.minds | `sim/EtherBound.Sim/Minds/` | v0.0.1 |
| sim.host | `sim/EtherBound.Host/` | v0.0.1 |
| game.app | `game/project.godot`, `game/EtherBound.Game.csproj` | v0.0.1 |
| game.render | `game/spike/` | v0.0.1 |
| tooling | `.gitattributes`, `.githooks/`, `scripts/`, `server/scripts/`, `EtherBound.sln`, `sim/Directory.Build.props`, `sim/EtherBound.Sim/EtherBound.Sim.csproj`, `sim/EtherBound.Sim/BannedSymbols.txt`, `sim/EtherBound.Bench/`, `package.json`, `global.json`, `.gitignore`, `.env.example`, `server/pyproject.toml`, `server/uv.lock`, `web/package.json`, `web/package-lock.json`, `web/vite.config.ts`, `web/playwright.config.ts` | v0.0.16 |
