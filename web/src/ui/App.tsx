import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent as ReactKeyboardEvent, type ReactElement } from "react";
import { createGame, type ContextTarget, type MapScene, type Telemetry } from "../game/MapScene";
import type { ZoomLevel } from "../game/zoom";
import { fetchGameState, fetchGenerators, requestNewGame, WebSocketClient } from "../net/client";
import type { ConnectionState, GameStateResponse, GeneratorInfo, MenuEntry, MenuResponse, WorldState } from "../net/protocol";
import { defaultValues, flattenOptions, rows, toOptions, type GenRow, type GenValue, type GenValues } from "./genForm";
import { firstAvailable, radialSlots, stepFocus } from "./radialMenu";

type MenuState = { target: ContextTarget; targetLabel?: string; entries: MenuEntry[]; error?: string } | null;
type ResolvedMenuState = Exclude<MenuState, null>;
type RadialState = { center: { x: number; y: number }; targetLabel?: string; entries: MenuEntry[]; error?: string } | null;
type ResolvedRadialState = Exclude<RadialState, null>;
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
  const sceneRef = useRef<MapScene | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const [world, setWorld] = useState(initialWorld);
  const [connection, setConnection] = useState<ConnectionState>("connecting");
  const [menu, setMenu] = useState<MenuState>(null);
  const [radial, setRadial] = useState<RadialState>(null);
  const [input, setInput] = useState("");
  const [feed, setFeed] = useState<FeedItem[]>([]);
  const [telemetry, setTelemetry] = useState<Telemetry | null>(null);
  const [zoom, setZoom] = useState<ZoomLevel | null>(null);
  const [newGameOpen, setNewGameOpen] = useState(false);
  const [debugOpen, setDebugOpen] = useState(false);
  const [generators, setGenerators] = useState<GeneratorInfo[]>([]);
  const menuRequest = useRef(0);
  const radialRequest = useRef(0);
  const radialOpen = useRef(false);
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
    const game = createGame(
      hostRef.current,
      client,
      (target) => void openMenu(target),
      setTelemetry,
      setZoom,
      (scene) => { sceneRef.current = scene; },
    );
    return () => {
      game.destroy(true);
      removeAll();
    };
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const typing = document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement;
      if (event.key === "Enter" && !document.querySelector("#new-game-dialog") && document.activeElement !== inputRef.current) {
        event.preventDefault();
        inputRef.current?.focus();
      }
      if ((event.key === "v" || event.key === "V") && !typing && !event.ctrlKey && !event.metaKey && !event.altKey) {
        event.preventDefault();
        if (radialOpen.current) closeRadial();
        else void openRadial();
      }
      if (event.key === "Escape") {
        setMenu(null);
        setDebugOpen(false);
        closeRadial();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const radialVisible = radial !== null;
  useEffect(() => {
    if (!radialVisible) return;
    // The radial is centred on Niko, so it follows him while he walks.
    const timer = window.setInterval(() => {
      const anchor = sceneRef.current?.playerAnchor();
      if (!anchor) return;
      setRadial((current) => current ? { ...current, center: { x: anchor.screenX, y: anchor.screenY } } : current);
    }, 100);
    return () => window.clearInterval(timer);
  }, [radialVisible]);

  useEffect(() => {
    let active = true;
    void fetchGenerators()
      .then((values) => { if (active) setGenerators(values); })
      .catch(() => { if (active) setGenerators([]); });
    return () => { active = false; };
  }, []);

  async function openMenu(target: ContextTarget): Promise<void> {
    closeRadial();
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

  /** Opens the radial menu on Niko's own tile: the same server-built entries, arranged on him. */
  async function openRadial(): Promise<void> {
    const anchor = sceneRef.current?.playerAnchor() ?? null;
    if (!anchor) return;
    const token = ++radialRequest.current;
    radialOpen.current = true;
    setMenu(null);
    setRadial({ center: { x: anchor.screenX, y: anchor.screenY }, entries: [] });
    const query = new URLSearchParams({ x: String(anchor.x), y: String(anchor.y), z: String(anchor.z) });
    try {
      const response = await fetch(`/api/menu?${query}`);
      if (!response.ok) throw new Error(`menu request ${response.status}`);
      const payload = await response.json() as MenuResponse;
      if (token !== radialRequest.current) return;
      setRadial({ center: { x: anchor.screenX, y: anchor.screenY }, targetLabel: payload.target, entries: payload.ops });
    } catch {
      if (token !== radialRequest.current) return;
      setRadial({ center: { x: anchor.screenX, y: anchor.screenY }, entries: [], error: "SERVER MENU UNAVAILABLE" });
    }
  }

  function closeRadial(): void {
    radialOpen.current = false;
    radialRequest.current += 1;
    setRadial(null);
  }

  function pickEntry(entry: MenuEntry): void {
    const client = clientRef.current;
    if (!client || !entry.available) return;
    // The server built this action; sending it back unchanged is the whole contract.
    sentOps.current.set(client.sendAction(entry.action), entry.label.toUpperCase());
    setMenu(null);
    closeRadial();
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

  return <main className="shell" onClick={() => { setMenu(null); setNewGameOpen(false); closeRadial(); }} onContextMenu={(event) => event.preventDefault()}>
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
        generators={generators}
        onClose={() => setNewGameOpen(false)}
        onSuccess={(seed, generator) => {
          pushFeed(`NEW GAME · ${generator.toUpperCase()} · SEED ${seed}`, "echo");
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
    <DebugDrawer open={debugOpen} onClose={() => setDebugOpen(false)} generators={generators} />
    {menu && <ContextMenu state={menu} onPick={pickEntry} onClose={() => setMenu(null)} />}
    {radial && <RadialMenu state={radial} onPick={pickEntry} onClose={closeRadial} />}
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

function randomSeed(exclude?: number): number {
  const values = new Uint32Array(1);
  let seed: number;
  do {
    window.crypto.getRandomValues(values);
    seed = values[0]! & 0x7fffffff;
  } while (seed === exclude);
  return seed;
}

function DebugDrawer({ open, onClose, generators }: { open: boolean; onClose: () => void; generators: GeneratorInfo[] }): ReactElement {
  return <aside id="debug-drawer" className={`drawer ${open ? "open" : ""}`} aria-label="Debug menu" aria-hidden={!open} onClick={(event) => event.stopPropagation()}>
    <div className="drawer-heading">
      <h2>DEBUG CONSOLE</h2>
      <button className="mini" aria-label="Close debug menu" onClick={onClose}>X</button>
    </div>
    <div className="tabs" role="tablist" aria-label="Debug sections">
      <button className="tab active" id="debug-tab" role="tab" aria-selected="true" aria-controls="debug-panel">MAP</button>
    </div>
    <div className="drawer-content" id="debug-panel" role="tabpanel" aria-labelledby="debug-tab">
      {open && <MapTab generators={generators} />}
    </div>
  </aside>;
}

function MapTab({ generators }: { generators: GeneratorInfo[] }): ReactElement {
  const [state, setState] = useState<GameStateResponse | null>(null);
  const [key, setKey] = useState("");
  const [values, setValues] = useState<GenValues>({});
  const [seedText, setSeedText] = useState("0");
  const [status, setStatus] = useState("");
  const [pending, setPending] = useState(false);
  const initialised = useRef(false);

  useEffect(() => {
    let active = true;
    void fetchGameState()
      .then((loaded) => { if (active) { setState(loaded); setKey(loaded.generator); setSeedText(String(loaded.seed)); } })
      .catch(() => { if (active) setStatus("STATE UNAVAILABLE"); });
    return () => { active = false; };
  }, []);

  const spec = generators.find((item) => item.key === key);

  useEffect(() => {
    if (!spec || !state || initialised.current) return;
    const options = state.generator === spec.key ? state.gen_options : {};
    setValues(flattenOptions(options, spec.fields));
    initialised.current = true;
  }, [spec, state]);

  function chooseGenerator(nextKey: string): void {
    setKey(nextKey);
    const next = generators.find((item) => item.key === nextKey);
    setValues(next ? defaultValues(next.fields) : {});
    setStatus("");
  }

  function change(path: string, value: GenValue): void {
    setValues((current) => ({ ...current, [path]: value }));
    setStatus("");
  }

  if (!spec) {
    return <div className="debug-placeholder">
      <span className="placeholder-mark" aria-hidden="true">[ -- ]</span>
      <h3>LOADING GENERATORS</h3>
      <p>{status || "Reading the map catalog from the server."}</p>
    </div>;
  }

  const rendered = rows(spec.fields, values);
  const seedValid = /^\d+$/.test(seedText) && Number(seedText) <= 2147483647;
  const hasError = rendered.some((row) => !row.hidden && row.error !== null);

  async function regenerate(): Promise<void> {
    if (!spec) return;
    if (!seedValid) { setStatus("SEED MUST BE A WHOLE NUMBER"); return; }
    if (hasError) { setStatus("FIX THE HIGHLIGHTED FIELDS"); return; }
    setPending(true);
    setStatus("");
    try {
      await requestNewGame(Number(seedText), spec.key, toOptions(spec.fields, values));
      setStatus("REGENERATED");
    } catch {
      setStatus("REGENERATE FAILED");
    } finally {
      setPending(false);
    }
  }

  return <div className="gen-panel">
    <div className="gen-meta">
      <div className="readout"><span className="label">MAP</span><span>{spec.name}</span></div>
      <div className="readout"><span className="label">VERSION</span><span>v{spec.version}</span></div>
      <div className="readout"><span className="label">CURRENT</span><span>{state ? `${state.generator} v${state.gen_version}` : "—"}</span></div>
    </div>
    <div className="seed">
      <label className="label" htmlFor="debug-map">MAP</label>
      <select id="debug-map" value={spec.key} onChange={(event) => chooseGenerator(event.target.value)}>
        {generators.map((item) => <option key={item.key} value={item.key}>{item.name}</option>)}
      </select>
    </div>
    <div className="seed">
      <label className="label" htmlFor="debug-seed">SEED</label>
      <input id="debug-seed" type="number" min="0" max="2147483647" value={seedText} aria-invalid={!seedValid} onChange={(event) => { setSeedText(event.target.value); setStatus(""); }} />
      <button className="mini" type="button" onClick={() => setSeedText(String(randomSeed(Number(seedText))))}>RANDOM</button>
    </div>
    <div className="gen-form">
      {rendered.filter((row) => !row.hidden).map((row) => <GenRowView key={row.field.path} row={row} onChange={change} />)}
    </div>
    {spec.bays.length > 0 && <div className="bay-list">
      <div className="feed-label">▍BAYS</div>
      {spec.bays.map((bay) => <div className="row" key={bay.key}><span className="slot">{bay.key.toUpperCase()}</span><span className="item">{bay.x},{bay.y} · {bay.width}×{bay.height}</span></div>)}
    </div>}
    {status && <p className="error" role="status">{status}</p>}
    <div className="actions">
      <button type="button" onClick={() => { setValues(defaultValues(spec.fields)); setStatus(""); }}>RESET</button>
      <button className="danger" type="button" disabled={pending} onClick={() => void regenerate()}>{pending ? "WORKING" : "REGENERATE"}</button>
    </div>
  </div>;
}

function GenRowView({ row, onChange }: { row: GenRow; onChange: (path: string, value: GenValue) => void }): ReactElement {
  const { field, value, error } = row;
  const id = `gen-${field.path}`;
  return <div className="gen-row">
    <label className="label" htmlFor={id}>{field.label}</label>
    {field.kind === "choice"
      ? <select id={id} value={String(value)} onChange={(event) => onChange(field.path, event.target.value)}>
        {field.choices.map((choice) => <option key={choice} value={choice}>{choice}</option>)}
      </select>
      : field.kind === "bool"
        ? <input id={id} type="checkbox" checked={Boolean(value)} onChange={(event) => onChange(field.path, event.target.checked)} />
        : <input id={id} type="number" value={String(value)} min={field.min ?? undefined} max={field.max ?? undefined} step={field.step ?? undefined} aria-invalid={Boolean(error)} onChange={(event) => onChange(field.path, event.target.value === "" ? "" : Number(event.target.value))} />}
    {error && <small className="why">{error}</small>}
  </div>;
}

function NewGamePopover({
  seed: initialSeed,
  generators,
  onClose,
  onSuccess,
}: {
  seed: number;
  generators: GeneratorInfo[];
  onClose: () => void;
  onSuccess: (seed: number, generator: string) => void;
}): ReactElement {
  const [seedText, setSeedText] = useState(String(initialSeed));
  const [generator, setGenerator] = useState("");
  const [error, setError] = useState("");
  const [pending, setPending] = useState(false);
  const pendingRef = useRef(false);
  const seedRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    seedRef.current?.focus();
    seedRef.current?.select();
    let active = true;
    void fetchGameState()
      .then((state) => { if (active) setGenerator(state.generator); })
      .catch(() => {});
    return () => { active = false; };
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
      await requestNewGame(seed, generator || undefined);
      onSuccess(seed, generator || "test");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message.toUpperCase() : "NEW GAME REQUEST FAILED");
    } finally {
      pendingRef.current = false;
      setPending(false);
    }
  }

  function randomizeSeed(): void {
    setSeedText(String(randomSeed(Number(seedText))));
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
      <label className="label" htmlFor="new-game-map">MAP</label>
      <select id="new-game-map" value={generator} onChange={(event) => setGenerator(event.target.value)}>
        {generators.length === 0 && <option value="">test</option>}
        {generators.map((item) => <option key={item.key} value={item.key}>{item.name}</option>)}
      </select>
    </div>
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

function RadialMenu({ state, onPick, onClose }: { state: ResolvedRadialState; onPick: (entry: MenuEntry) => void; onClose: () => void }): ReactElement {
  const { entries, center } = state;
  const slots = radialSlots(entries);
  const [focus, setFocus] = useState(() => firstAvailable(entries));
  const [shake, setShake] = useState(false);

  useEffect(() => { setFocus(firstAvailable(entries)); }, [entries]);

  const focused = entries[focus];
  const extent = slots.length > 0 ? Math.max(...slots.map((slot) => slot.radius)) + 96 : 160;
  const detail = state.error
    ? state.error
    : !focused
      ? "NO ACTIONS"
      : `${focused.label}${focused.subject ? ` ${focused.subject}` : ""}${focused.available ? "" : ` · ${focused.reason ?? "unavailable"}`}`;

  function pick(entry: MenuEntry | undefined): void {
    if (!entry) return;
    if (!entry.available) {
      setShake(true);
      window.setTimeout(() => setShake(false), 260);
      return;
    }
    onPick(entry);
  }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (entries.length === 0) return;
      if (event.key === "ArrowRight" || event.key === "ArrowDown") {
        event.preventDefault();
        event.stopImmediatePropagation();
        setFocus((current) => stepFocus(current, 1, entries.length));
      } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
        event.preventDefault();
        event.stopImmediatePropagation();
        setFocus((current) => stepFocus(current, -1, entries.length));
      } else if (event.key >= "1" && event.key <= "9") {
        const index = Number(event.key) - 1;
        if (index < entries.length) {
          event.preventDefault();
          event.stopImmediatePropagation();
          setFocus(index);
        }
      } else if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        event.stopImmediatePropagation();
        pick(entries[focus]);
      }
    };
    window.addEventListener("keydown", onKeyDown, true);
    return () => window.removeEventListener("keydown", onKeyDown, true);
  }, [entries, focus, onPick]);

  return <div
    className="radial"
    role="menu"
    aria-label="Actions around Niko"
    style={{ left: center.x, top: center.y }}
    onClick={(event) => { event.stopPropagation(); onClose(); }}>
    <svg className="radial-lines" width={extent * 2} height={extent * 2} viewBox={`${-extent} ${-extent} ${extent * 2} ${extent * 2}`} aria-hidden="true">
      {slots.map((slot) => <line key={slot.index} x1={0} y1={0} x2={slot.x} y2={slot.y} />)}
    </svg>
    <div className={`radial-hub ${shake ? "shake" : ""}`}>
      <div className="radial-target">{state.targetLabel ?? "ACTIONS"}</div>
      <div className={`radial-detail ${focused?.available === false ? "off" : ""}`}>{detail}</div>
    </div>
    {slots.map((slot, index) => {
      const entry = slot.entry;
      const classes = [
        "radial-slot",
        index === focus ? "focus" : "",
        entry.available ? "" : "off",
        entry.tags.includes("ether") ? "ether" : "",
        entry.tags.includes("illegal") ? "illegal" : "",
        entry.tags.includes("violent") ? "violent" : "",
      ].filter(Boolean).join(" ");
      return <button
        key={`${entry.op}-${slot.index}`}
        type="button"
        role="menuitem"
        aria-disabled={!entry.available}
        className={classes}
        style={{ left: slot.x, top: slot.y }}
        title={`${entry.label}${entry.subject ? ` ${entry.subject}` : ""}${entry.available ? "" : ` · ${entry.reason ?? "unavailable"}`}`}
        onMouseEnter={() => setFocus(index)}
        onClick={(event) => { event.stopPropagation(); pick(entry); }}>
        <span className="radial-op">{entry.label}</span>
        {entry.subject && <span className="radial-subject">{entry.subject}</span>}
      </button>;
    })}
  </div>;
}
