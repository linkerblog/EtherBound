import { useEffect, useRef, useState, type FormEvent, type ReactElement } from "react";
import { createGame, type ContextTarget, type MapScene, type Telemetry } from "../game/MapScene";
import type { ZoomLevel } from "../game/zoom";
import { fetchGenerators, WebSocketClient } from "../net/client";
import type { ConnectionState, GeneratorInfo, MenuEntry, MenuResponse, WorldState } from "../net/protocol";
import { ContextMenu, RadialMenu, type MenuState, type RadialState } from "./ContextMenus";
import { DebugView, LlmView } from "./DebugViews";
import { CarryPanel, ViewTabs } from "./HudPanels";
import { NewGamePopover } from "./NewGamePopover";
import { tabFromHotkey, type ViewTab } from "./viewTabs";

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
  const viewportRef = useRef<HTMLDivElement>(null);
  const [world, setWorld] = useState(initialWorld);
  const [connection, setConnection] = useState<ConnectionState>("connecting");
  const [menu, setMenu] = useState<MenuState>(null);
  const [radial, setRadial] = useState<RadialState>(null);
  const [input, setInput] = useState("");
  const [feed, setFeed] = useState<FeedItem[]>([]);
  const [telemetry, setTelemetry] = useState<Telemetry | null>(null);
  const [zoom, setZoom] = useState<ZoomLevel | null>(null);
  const [newGameOpen, setNewGameOpen] = useState(false);
  const [view, setView] = useState<ViewTab>("game");
  // The window key handler is bound once, so it reads the view through a ref.
  const viewRef = useRef<ViewTab>("game");
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
      const tab = tabFromHotkey(event);
      if (tab) {
        event.preventDefault();
        selectView(tab);
        return;
      }
      if (viewRef.current !== "game") {
        if (event.key === "Escape") selectView("game");
        return;
      }
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

  /** Opens the radial menu on Niko: server-built entries for his tile plus any open neighbour
   *  (`radius=1`), grouped by verb and placed by tile around him. */
  async function openRadial(): Promise<void> {
    const anchor = sceneRef.current?.playerAnchor() ?? null;
    if (!anchor) return;
    const token = ++radialRequest.current;
    radialOpen.current = true;
    setMenu(null);
    const center = { x: anchor.screenX, y: anchor.screenY };
    const origin = { x: anchor.x, y: anchor.y, z: anchor.z };
    setRadial({ center, origin, entries: [], places: [] });
    const query = new URLSearchParams({
      x: String(anchor.x),
      y: String(anchor.y),
      z: String(anchor.z),
      radius: "1",
    });
    try {
      const response = await fetch(`/api/menu?${query}`);
      if (!response.ok) throw new Error(`menu request ${response.status}`);
      const payload = await response.json() as MenuResponse;
      if (token !== radialRequest.current) return;
      setRadial({ center, origin, targetLabel: payload.target, entries: payload.ops, places: payload.places });
    } catch {
      if (token !== radialRequest.current) return;
      setRadial({ center, origin, entries: [], places: [], error: "SERVER MENU UNAVAILABLE" });
    }
  }

  function closeRadial(): void {
    radialOpen.current = false;
    radialRequest.current += 1;
    setRadial(null);
    sceneRef.current?.markTile(null);
  }

  /** Only `game` takes world input: the other views cover it and must not walk Niko or open menus. */
  function selectView(next: ViewTab): void {
    if (viewRef.current === next) return;
    viewRef.current = next;
    setView(next);
    if (next === "game") {
      sceneRef.current?.setKeyboardEnabled(true);
      return;
    }
    setMenu(null);
    closeRadial();
    setNewGameOpen(false);
    inputRef.current?.blur();
    sceneRef.current?.setKeyboardEnabled(false);
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
  const onGame = view === "game";

  return <main className="shell" onClick={() => { setMenu(null); setNewGameOpen(false); closeRadial(); }} onContextMenu={(event) => event.preventDefault()}>
    <ViewTabs view={view} onSelect={selectView} />
    <div ref={viewportRef} className="viewport">
      {/* Hidden, never unmounted: `Scale.RESIZE` would collapse a `display: none` parent to 0x0. */}
      <div ref={hostRef} className={`world ${onGame ? "" : "hidden-view"}`} aria-label="EtherBound world" aria-hidden={!onGame} />
      <section id="view-panel-game" role="tabpanel" aria-labelledby="view-tab-game" className={`hud ${onGame ? "" : "hidden-view"}`} aria-label="Niko HUD" aria-hidden={!onGame}>
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
      {view === "debug" && <DebugView generators={generators} />}
      {view === "llm" && <LlmView />}
    </div>
    {menu && <ContextMenu state={menu} viewportRef={viewportRef} onPick={pickEntry} onClose={() => setMenu(null)} />}
    {radial && <RadialMenu state={radial} onPick={pickEntry} onClose={closeRadial} onFocusTile={(tile) => sceneRef.current?.markTile(tile)} />}
  </main>;
}
