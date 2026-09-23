import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent as ReactKeyboardEvent, type ReactElement } from "react";
import { createGame, type ContextTarget, type Telemetry } from "../game/MapScene";
import type { ZoomLevel } from "../game/zoom";
import { requestNewGame, WebSocketClient } from "../net/client";
import type { ConnectionState, MenuEntry, MenuResponse, WorldState } from "../net/protocol";

type MenuState = { target: ContextTarget; targetLabel?: string; entries: MenuEntry[]; error?: string } | null;
type ResolvedMenuState = Exclude<MenuState, null>;
/** `seen` is what Niko perceives; `act`, `warn` and `fail` report his own actions. */
type FeedKind = "echo" | "seen" | "act" | "warn" | "fail";
type FeedItem = { text: string; kind: FeedKind; minute: number };

const PLAYER_ID = "niko";
const FEED_LENGTH = 6;

const initialWorld: WorldState = { gameMinute: 0, paused: false, speed: 1, actors: {} };

function formatGameTime(minutes: number): string {
  const day = Math.floor(minutes / 1440) + 1;
  const dayMinute = ((minutes % 1440) + 1440) % 1440;
  const hours = Math.floor(dayMinute / 60).toString().padStart(2, "0");
  const mins = Math.floor(dayMinute % 60).toString().padStart(2, "0");
  return `DAY ${day} · ${hours}:${mins}`;
}

function statusLabel(state: ConnectionState): string {
  return state === "open" ? "LINKED" : state === "connecting" ? "CONNECTING" : "OFFLINE";
}

function formatPosition({ x, y, z, h }: Telemetry): string {
  const altitude = h === undefined ? "" : ` (${(h / 2).toFixed(1)} m)`;
  return `X ${x.toFixed(2)} · Y ${y.toFixed(2)} · Z ${z}${altitude}`;
}

function meter(value: number, cells = 12): string {
  const filled = Math.round(Math.max(0, Math.min(100, value)) / 100 * cells);
  return "█".repeat(filled) + "░".repeat(cells - filled);
}

export function App(): ReactElement {
  const hostRef = useRef<HTMLDivElement>(null);
  const clientRef = useRef<WebSocketClient | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [world, setWorld] = useState(initialWorld);
  const [connection, setConnection] = useState<ConnectionState>("connecting");
  const [menu, setMenu] = useState<MenuState>(null);
  const [input, setInput] = useState("");
  const [feed, setFeed] = useState<FeedItem[]>([]);
  const [telemetry, setTelemetry] = useState<Telemetry | null>(null);
  const [zoom, setZoom] = useState<ZoomLevel | null>(null);
  const [newGameOpen, setNewGameOpen] = useState(false);
  const [debugOpen, setDebugOpen] = useState(false);
  const menuRequest = useRef(0);
  const gameMinute = useRef(0);
  // Results carry only a sequence, so remember which op each sent action was.
  const sentOps = useRef(new Map<number, string>());

  function pushFeed(text: string, kind: FeedKind): void {
    setFeed((current) => [...current.slice(-(FEED_LENGTH - 1)), { text, kind, minute: gameMinute.current }]);
  }

  useEffect(() => {
    const client = new WebSocketClient();
    clientRef.current = client;
    const removeState = client.onState((state) => {
      gameMinute.current = state.gameMinute;
      setWorld(state);
    });
    const removeConnection = client.onConnection(setConnection);
    const removeResult = client.onResult((result) => {
      const op = sentOps.current.get(result.sequence) ?? "ACTION";
      sentOps.current.delete(result.sequence);
      if (result.text) pushFeed(result.text, "act");
      if (!result.accepted) {
        pushFeed(`CAN'T ${op} · ${result.reason ?? "not now"}`, "warn");
        return;
      }
      setWorld((current) => {
        const actorKey = current.actors[PLAYER_ID] ? PLAYER_ID : current.actors.player ? "player" : null;
        if (!actorKey) return current;
        const actor = current.actors[actorKey]!;
        return {
          ...current,
          actors: {
            ...current.actors,
            [actorKey]: {
              ...actor,
              carried: result.carried ?? actor.carried ?? [],
              load_kg: result.load_kg ?? actor.load_kg ?? 0,
            },
          },
        };
      });
      if (result.activity) pushFeed(`${op} · ${result.activity.ends_minute - result.activity.started_minute} MIN`, "act");
    });
    const removeActivity = client.onActivity((activity) => {
      // An interruption is something the player just did; it needs no notice.
      if (activity.actor_id !== PLAYER_ID || activity.outcome === "interrupted") return;
      const op = activity.op.toUpperCase();
      if (activity.outcome === "completed") pushFeed(`${op} DONE`, "act");
      else pushFeed(`${op} FAILED · ${activity.reason ?? "unknown"}`, "fail");
    });
    client.connect();
    const removeAll = () => {
      removeState();
      removeConnection();
      removeResult();
      removeActivity();
      client.disconnect();
    };
    if (!hostRef.current) return removeAll;
    const game = createGame(hostRef.current, client, (target) => void openMenu(target), setTelemetry, setZoom);
    return () => {
      game.destroy(true);
      removeAll();
    };
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Enter" && !document.querySelector("#new-game-dialog") && document.activeElement !== inputRef.current) {
        event.preventDefault();
        inputRef.current?.focus();
      }
      if (event.key === "Escape") {
        setMenu(null);
        setDebugOpen(false);
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  async function openMenu(target: ContextTarget): Promise<void> {
    const token = ++menuRequest.current;
    setMenu({ target, entries: [] });
    const query = new URLSearchParams({ x: String(target.x), y: String(target.y), z: String(target.z) });
    try {
      const response = await fetch(`/api/menu?${query}`);
      if (!response.ok) throw new Error(`menu request ${response.status}`);
      const payload = await response.json() as MenuResponse;
      if (token !== menuRequest.current) return;
      setMenu({ target, targetLabel: payload.target, entries: payload.ops });
    } catch {
      if (token !== menuRequest.current) return;
      setMenu({ target, entries: [], error: "SERVER MENU UNAVAILABLE" });
    }
  }

  function pickEntry(entry: MenuEntry): void {
    const client = clientRef.current;
    if (!client || !entry.available) return;
    // The server built this action; sending it back unchanged is the whole contract.
    sentOps.current.set(client.sendAction(entry.action), entry.label.toUpperCase());
    setMenu(null);
  }

  function setClock(paused: boolean, speed = world.speed): void {
    clientRef.current?.sendClock(paused, speed);
    setWorld((current) => ({ ...current, paused, speed }));
  }

  function submitInput(event: FormEvent): void {
    event.preventDefault();
    const value = input.trim();
    if (!value) return;
    pushFeed(value, "echo");
    setInput("");
  }

  const activity = (world.actors[PLAYER_ID] ?? world.actors.player)?.activity;

  return <main className="shell" onClick={() => { setMenu(null); setNewGameOpen(false); }} onContextMenu={(event) => event.preventDefault()}>
    <div ref={hostRef} className="world" aria-label="EtherBound world" />
    <section className="hud" aria-label="Niko HUD">
      <div className={`hud-panel clock ${world.paused ? "paused" : ""}`} onClick={(event) => event.stopPropagation()}>
        <span className="time">{formatGameTime(world.gameMinute)}</span>
        <button className="mini" aria-label="Pause" onClick={() => setClock(!world.paused)}>II</button>
        {[1, 3, 10].map((speed) => <button key={speed} className={`mini ${!world.paused && world.speed === speed ? "active" : ""}`} onClick={() => setClock(false, speed)}>x{speed}</button>)}
        <span className="sep" aria-hidden="true" />
        <button className="mini" aria-haspopup="dialog" aria-expanded={newGameOpen} aria-controls="new-game-dialog" onClick={() => setNewGameOpen(true)}>NEW</button>
      </div>
      {newGameOpen && <NewGamePopover
        seed={world.seed ?? 0}
        onClose={() => setNewGameOpen(false)}
        onSuccess={(seed) => {
          pushFeed(`NEW GAME · SEED ${seed}`, "echo");
          setNewGameOpen(false);
        }}
      />}
      {telemetry && <div className="hud-panel telemetry" aria-label="Position, speed and zoom">
        <div className="readout"><span className="label">POS</span><span>{formatPosition(telemetry)}</span></div>
        <div className="readout"><span className="label">SPD</span><span>{telemetry.speed.toFixed(2)} m/s</span></div>
        {zoom !== null && <div className="readout"><span className="label">ZOOM</span><span>x{zoom}</span></div>}
        {activity && <div className="readout"><span className="label">ACT</span><span>{activity.op.toUpperCase()} · {Math.max(0, activity.ends_minute - world.gameMinute)} MIN</span></div>}
      </div>}
      <div className="hud-panel status" onClick={(event) => event.stopPropagation()}>
        <span className={`pill ${connection === "open" ? "ok" : "warn"}`}>{statusLabel(connection)}</span>
        <span className="pill ether">ETHER: 00%</span>
        <button className="mini" aria-expanded={debugOpen} aria-controls="debug-drawer" onClick={() => setDebugOpen((open) => !open)}>DEBUG</button>
      </div>
      <div className="hud-panel feed" aria-live="polite" onClick={(event) => event.stopPropagation()}>
        <div className="feed-label">▍FEED</div>
        {feed.map((item, index) => <div className={`feed-item ${item.kind}`} key={`${item.minute}-${index}-${item.text}`}><span className="time">{formatGameTime(item.minute)}</span>{item.text}</div>)}
      </div>
      <CarryPanel actor={world.actors[PLAYER_ID] ?? world.actors.player} />
      <div className="hud-panel meters" onClick={(event) => event.stopPropagation()}>
        <div className="meter"><span className="label">HEALTH</span><span>{meter(100)}</span></div>
        <div className="meter"><span className="label">ENERGY</span><span>{meter(100)}</span></div>
        <div className="meter ether-bar"><span className="label">ETHER</span><span>{meter(0)}</span></div>
      </div>
      <form className="hud-panel input-line" onSubmit={submitInput} onClick={(event) => event.stopPropagation()}>
        <span className="prompt">&gt;</span>
        <input ref={inputRef} value={input} onChange={(event) => setInput(event.target.value)} placeholder="say or try anything" aria-label="Free text action" />
      </form>
    </section>
    <DebugDrawer open={debugOpen} onClose={() => setDebugOpen(false)} />
    {menu && <ContextMenu state={menu} onPick={pickEntry} onClose={() => setMenu(null)} />}
  </main>;
}

function CarryPanel({ actor }: { actor: WorldState["actors"][string] | undefined }): ReactElement {
  const carried = actor?.carried ?? [];
  const both = carried.find((item) => item.slot === "both");
  const left = carried.find((item) => item.slot === "left");
  const right = carried.find((item) => item.slot === "right");
  const back = carried.find((item) => item.slot === "back");
  const itemText = (item: typeof carried[number] | undefined): string => item
    ? `${item.name}${item.quantity > 1 ? ` ×${item.quantity}` : ""}`
    : "—";
  return <div className="hud-panel carry" aria-label="Carried objects">
    <h3>CARRY</h3>
    {both
      ? <CarryRow slot="HANDS" item={itemText(both)} />
      : <><CarryRow slot="L" item={itemText(left)} /><CarryRow slot="R" item={itemText(right)} /></>}
    <CarryRow slot="BACK" item={itemText(back)} />
    <div className={`load ${((actor?.load_kg ?? 0) > 10) ? "warn" : ""}`}>LOAD {(actor?.load_kg ?? 0).toFixed(1)} kg</div>
  </div>;
}

function CarryRow({ slot, item }: { slot: string; item: string }): ReactElement {
  return <div className="row"><span className="slot">{slot}</span><span className="item">{item}</span></div>;
}

function DebugDrawer({ open, onClose }: { open: boolean; onClose: () => void }): ReactElement {
  return <aside id="debug-drawer" className={`drawer ${open ? "open" : ""}`} aria-label="Debug menu" aria-hidden={!open} onClick={(event) => event.stopPropagation()}>
    <div className="drawer-heading">
      <h2>DEBUG CONSOLE</h2>
      <button className="mini" aria-label="Close debug menu" onClick={onClose}>X</button>
    </div>
    <div className="tabs" role="tablist" aria-label="Debug sections">
      <button className="tab active" id="debug-tab" role="tab" aria-selected="true" aria-controls="debug-panel">DEBUG</button>
    </div>
    <div className="drawer-content" id="debug-panel" role="tabpanel" aria-labelledby="debug-tab">
      <div className="debug-placeholder">
        <span className="placeholder-mark" aria-hidden="true">[ -- ]</span>
        <h3>NO DEBUG TOOLS</h3>
        <p>Debug controls and diagnostics will appear here.</p>
        <span className="pill">PLACEHOLDER</span>
      </div>
    </div>
  </aside>;
}

function NewGamePopover({
  seed: initialSeed,
  onClose,
  onSuccess,
}: {
  seed: number;
  onClose: () => void;
  onSuccess: (seed: number) => void;
}): ReactElement {
  const [seedText, setSeedText] = useState(String(initialSeed));
  const [error, setError] = useState("");
  const [pending, setPending] = useState(false);
  const pendingRef = useRef(false);
  const seedRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    seedRef.current?.focus();
    seedRef.current?.select();
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    if (pendingRef.current) return;
    if (!/^\d+$/.test(seedText)) {
      setError("SEED MUST BE A WHOLE NUMBER FROM 0 TO 2147483647");
      return;
    }
    const seed = Number(seedText);
    if (!Number.isSafeInteger(seed) || seed < 0 || seed > 2147483647) {
      setError("SEED MUST BE A WHOLE NUMBER FROM 0 TO 2147483647");
      return;
    }

    pendingRef.current = true;
    setPending(true);
    setError("");
    try {
      await requestNewGame(seed);
      onSuccess(seed);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message.toUpperCase() : "NEW GAME REQUEST FAILED");
    } finally {
      pendingRef.current = false;
      setPending(false);
    }
  }

  function randomizeSeed(): void {
    const values = new Uint32Array(1);
    let seed: number;
    do {
      window.crypto.getRandomValues(values);
      seed = values[0]! & 0x7fffffff;
    } while (seed === Number(seedText));
    setSeedText(String(seed));
    setError("");
  }

  function handleKeyDown(event: ReactKeyboardEvent<HTMLFormElement>): void {
    if (event.key !== "Escape") return;
    event.preventDefault();
    event.stopPropagation();
    if (!pendingRef.current) onClose();
  }

  return <form id="new-game-dialog" role="dialog" aria-label="New game" aria-modal="false" className="hud-panel confirm" noValidate onSubmit={(event) => void submit(event)} onClick={(event) => event.stopPropagation()} onKeyDown={handleKeyDown}>
    <h3>NEW GAME</h3>
    <p>Wipes the city, Niko and the event log. This cannot be undone.</p>
    <div className="seed">
      <label className="label" htmlFor="new-game-seed">SEED</label>
      <input ref={seedRef} id="new-game-seed" type="number" min="0" max="2147483647" step="1" value={seedText} aria-invalid={Boolean(error)} aria-describedby={error ? "new-game-error" : undefined} onChange={(event) => { setSeedText(event.target.value); setError(""); }} />
      <button className="mini" type="button" disabled={pending} onClick={randomizeSeed}>RANDOM</button>
    </div>
    {error && <p id="new-game-error" className="error" role="alert">{error}</p>}
    <div className="actions">
      <button type="button" disabled={pending} onClick={onClose}>CANCEL</button>
      <button className="danger" type="submit" disabled={pending}>{pending ? "WORKING" : "REGENERATE"}</button>
    </div>
  </form>;
}

function ContextMenu({ state, onPick, onClose }: { state: ResolvedMenuState; onPick: (entry: MenuEntry) => void; onClose: () => void }): ReactElement {
  const entries = state.entries;
  const [focus, setFocus] = useState(0);

  useEffect(() => {
    const first = entries.findIndex((entry) => entry.available);
    setFocus(first === -1 ? 0 : first);
  }, [entries]);

  useEffect(() => {
    // Capture phase, so Enter picks here instead of focusing the input line.
    const onKeyDown = (event: KeyboardEvent) => {
      if (entries.length === 0) return;
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        event.stopImmediatePropagation();
        const step = event.key === "ArrowDown" ? 1 : -1;
        setFocus((current) => (current + step + entries.length) % entries.length);
      } else if (event.key === "Enter") {
        event.preventDefault();
        event.stopImmediatePropagation();
        const entry = entries[focus];
        if (entry) onPick(entry);
      }
    };
    window.addEventListener("keydown", onKeyDown, true);
    return () => window.removeEventListener("keydown", onKeyDown, true);
  }, [entries, focus, onPick]);

  return <div className="menu" style={{ left: Math.min(state.target.screenX, window.innerWidth - 220), top: Math.min(state.target.screenY, window.innerHeight - 180) }} onClick={(event) => event.stopPropagation()}>
    <div className="target">TILE {state.target.x},{state.target.y},{state.target.z}{state.targetLabel ? ` · ${state.targetLabel}` : ""}</div>
    {state.error ? <div className="menu-empty">{state.error}</div> : entries.length === 0 ? <div className="menu-empty">NO ACTIONS RETURNED</div> : entries.map((entry, index) => <button
      key={`${entry.op}-${index}`}
      className={["op", index === focus ? "focus" : "", entry.tags.includes("ether") ? "ether" : "", entry.tags.includes("illegal") ? "illegal" : "", entry.available ? "" : "off"].filter(Boolean).join(" ")}
      disabled={!entry.available}
      onMouseEnter={() => setFocus(index)}
      onClick={() => (entry.available ? onPick(entry) : onClose())}>
      <span>{entry.label}{entry.subject ? ` ${entry.subject}` : ""}</span>{entry.reason && <small className="why">{entry.reason}</small>}
    </button>)}
  </div>;
}
