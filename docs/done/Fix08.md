# Fix08: The client never reconnects after a failed or dropped socket

[Bugfix] [Net]

Found while verifying Fix07 (`docs/done/Fix07.md`) against the running launcher. The chunk cache
works, but the user still saw a black world because the browser's socket was never open.

## 1. Findings

### F1: `WebSocketClient.connect()` opens a socket once and never retries
`connect()` creates the socket and only emits a connection state on `close`/`error`. Nothing
schedules another attempt. Two ordinary events leave the tab stuck:

- **Startup race.** The Vite dev server is ready seconds before the API server. Measured on
  22/09/2026: `web.log` shows Vite ready at 23:49:42 and a `/ws` proxy `ECONNREFUSED 127.0.0.1:8000`
  at 23:49:49; `server.log` shows the app ready at 23:49:50. A page (or a Vite-triggered reload of a
  pre-existing tab) that loads in that window fails the socket.
- **Hot-reload restart.** `EtherBound.exe` restarts the server on source saves; every restart drops
  the open sockets.

The evidence: the Fix06 run (23:31) accepted 8 `/ws` connections and logged `actor.moved` events;
the next run (23:49) accepted 0 and logged no movement, while the user pressed `NEW` three times
(HTTP keeps working without a socket). With Fix07's cache in place, a connected tab renders — a
headless check showed the textured world and a probe replayed all 25 chunks to a late listener — so
the black world was a dead socket, not a lost chunk.

## 2. Decision to approve

| Topic | Decision | Why |
|---|---|---|
| Reconnect | `WebSocketClient` retries a closed or failed socket with a 250 ms backoff that doubles to 4 s and resets on a successful `open`. `disconnect()` stops the loop | The client must survive the startup race and hot-reload restarts. Fix07's cache then replays the world on the new snapshot, so recovery needs no reload |
| Scope | Web only (`web.net`) | The server already sends a snapshot and its chunks on every new connection |

## 3. What changes

### 3.1 Reconnect (`web.net`)
- `client.ts`:
  - `connect()` stores the URL and calls a private `open()`; `disconnect()` sets `closed`, clears
    the timer, drops the URL, then closes the socket.
  - `open()` tags each handler with its socket and ignores events from a socket that is no longer
    current, resets the backoff on `open`, and on `close` emits `closed` then schedules a retry.
  - `scheduleReconnect()` / `clearReconnect()` own the single pending timer; the delay doubles from
    `RECONNECT_MIN_MS` (250 ms) to `RECONNECT_MAX_MS` (4 s).
- No change to the chunk cache, the snapshot clear, or the listener order.

### 3.2 Tests (`web/tests/`)
- `client-chunks.test.ts`: the fake socket can emit `close`; a dropped socket produces a new one,
  and `disconnect()` leaves no retry pending.

### 3.3 Docs
- `CONTEXT.md`: `web.net` row and a measured pitfall.
- `docs/PENDING.md`: this doc's entry, replaced when done.

## 4. Modules and versions

| Module | Version |
|---|---|
| web.net | v0.0.9 → v0.0.10 |

The overall project version stays `v0.6.3`: Fix07 and Fix08 ship in the same uncommitted batch.

## 5. What must not break

- Everything in Fix07 [Sec. 6]: chunk replay, clearing on snapshot and on `NEW`, the snapshot →
  state listener order.
- Unmount: `disconnect()` must leave no timer that would reopen a socket after teardown.

## 6. Checks and acceptance

Checks: `cd web && npm test && npm run build`, generated types unchanged, server checks.

Verified end to end in a headless browser: a tab loaded with the API server down showed `OFFLINE`,
and once the server came up the same tab became `LINKED` and received the position **without a
reload**. Automated: 45 web tests pass.

## 7. Todo

### Decisions
- [x] Approve [Sec. 2] (approved by the request to make the game visible again)

### Code
- [x] Reconnect in `WebSocketClient` [Sec. 3.1]

### Checks
- [x] Tests [Sec. 3.2]; web tests and build
- [x] Browser end-to-end: `OFFLINE` → `LINKED` without a reload [Sec. 6]

### Closing
- [x] `CONTEXT.md`, `PENDING.md` [Sec. 3.3]; `docs/utils/VERSION.md` [Sec. 4]
- [x] Notion: Work Report for the date and a Dev Blog page
- [x] Move this doc to `docs/done/`

---

## TL;DR

- The client opened one socket and never retried. A page that loaded before the API server was ready
  (Vite is ready ~7 s earlier) or a tab that survived a hot-reload restart stayed `OFFLINE` with a
  black world, even though Fix07's chunk cache was working.
- `WebSocketClient` now reconnects with a small backoff and stops on `disconnect()`.
- Verified in a headless browser: the tab recovered from `OFFLINE` to `LINKED` on its own.

Web only: `web.net` v0.0.10; the project stays `v0.6.3`.
