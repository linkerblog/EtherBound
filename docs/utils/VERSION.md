# Versions

Overall project version: `v5.2.1`

Every module and its current version. Modules start at `v0.0.0`; each modification bumps its
version by `0.0.1`, and the bump ships in the same change. The overall project version tracks the
version assigned to the latest commit.

`server.*`, `web.*` and `launcher` were retired at the Dev-025 cut-over (26/09/2026); their history
stays in git log and in `legacy-python-web-stack.zip`, not below.

| Module | Path | Version |
|---|---|---|
| bitcanvas | `BitCanvas/` | v0.0.8 |
| sim.rng | `sim/EtherBound.Sim/Rng/` | v0.0.1 |
| sim.world | `sim/EtherBound.Sim/World/` | v0.0.4 |
| sim.events | `sim/EtherBound.Sim/Events/`, `sim/EtherBound.Sim/Core/` | v0.0.4 |
| sim.db | `sim/EtherBound.Sim/Db/` | v0.0.5 |
| sim.engine | `sim/EtherBound.Sim/Engine/`, `sim/EtherBound.Sim/Clock/` | v0.0.6 |
| sim.minds | `sim/EtherBound.Sim/Minds/` | v0.0.3 |
| sim.host | `sim/EtherBound.Host/` | v0.0.6 |
| game.app | `game/app/`, `game/project.godot`, `game/EtherBound.Game.csproj`, `game/EtherBound.Game.sln`, `game/export_presets.cfg` | v0.0.11 |
| game.render | `game/spike/` | v0.0.10 |
| game.ui | `game/ui/` | v0.0.7 |
| tooling | `.gitattributes`, `.githooks/`, `scripts/`, `EtherBound.sln`, `sim/Directory.Build.props`, `sim/EtherBound.Sim/EtherBound.Sim.csproj`, `sim/EtherBound.Sim/BannedSymbols.txt`, `sim/EtherBound.Bench/`, `package.json`, `global.json`, `.gitignore` | v0.0.23 |
