# Versions

Every module and its current version. Modules start at `v0.0.0`; each modification bumps its
version by `0.0.1`, and the bump ships in the same change.

| Module | Path | Version |
|---|---|---|
| server.app | `server/src/etherbound/app.py`, `config.py`, `routes/` | v0.0.1 |
| server.clock | `server/src/etherbound/clock.py` | v0.0.1 |
| server.engine | `server/src/etherbound/engine/` | v0.0.1 |
| server.net | `server/src/etherbound/net/` | v0.0.1 |
| server.db | `server/src/etherbound/db/`, `server/alembic/` | v0.0.1 |
| server.rng | `server/src/etherbound/rng.py` | v0.0.1 |
| server.testmap | `server/src/etherbound/testmap.py` | not versioned (Phase 1 deletes it) |
| web.game | `web/src/game/` | v0.0.2 |
| web.net | `web/src/net/` | v0.0.1 |
| web.ui | `web/src/ui/` | v0.0.2 |
| tooling | `scripts/`, `start.bat`, `cleanup.bat`, root config | v0.0.6 |
