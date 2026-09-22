import { useEffect, useRef, useState, type FormEvent, type ReactElement } from "react";
import { createGame, type ContextTarget, type Telemetry } from "../game/MapScene";
import { WebSocketClient } from "../net/client";
import type { ConnectionState } from "../net/protocol";
import type { MenuVerb, WorldState } from "../net/protocol";

type MenuState = { target: ContextTarget; targetLabel?: string; verbs: MenuVerb[]; error?: string } | null;
type ResolvedMenuState = Exclude<MenuState, null>;

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

function readVerbs(value: unknown): MenuVerb[] {
  if (!Array.isArray(value)) return [];
  return value.flatMap((entry) => {
    if (typeof entry === "string") return [{ verb: entry }];
    if (typeof entry !== "object" || entry === null) return [];
    const item = entry as Record<string, unknown>;
    return typeof item.verb === "string" ? [{
      verb: item.verb,
      label: typeof item.label === "string" ? item.label : undefined,
      available: typeof item.available === "boolean" ? item.available : undefined,
      reason: typeof item.reason === "string" ? item.reason : undefined,
      tag: typeof item.tag === "string" ? item.tag : undefined,
    }] : [];
  });
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
  const [echoes, setEchoes] = useState<string[]>([]);
  const [telemetry, setTelemetry] = useState<Telemetry | null>(null);
  const menuRequest = useRef(0);

  useEffect(() => {
    const client = new WebSocketClient();
    clientRef.current = client;
    const removeState = client.onState(setWorld);
    const removeConnection = client.onConnection(setConnection);
    client.connect();
    if (!hostRef.current) return () => {
      removeState();
      removeConnection();
      client.disconnect();
    };
    const game = createGame(hostRef.current, client, (target) => void openMenu(target), setTelemetry);
    return () => {
      game.destroy(true);
      removeState();
      removeConnection();
      client.disconnect();
    };
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Enter" && document.activeElement !== inputRef.current) {
        event.preventDefault();
        inputRef.current?.focus();
      }
      if (event.key === "Escape") setMenu(null);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  async function openMenu(target: ContextTarget): Promise<void> {
    const token = ++menuRequest.current;
    setMenu({ target, verbs: [] });
    const query = new URLSearchParams({ x: String(target.x), y: String(target.y), z: String(target.z) });
    try {
      const response = await fetch(`/api/menu?${query}`);
      if (!response.ok) throw new Error(`menu request ${response.status}`);
      const payload = await response.json() as Record<string, unknown>;
      if (token !== menuRequest.current) return;
      setMenu({
        target,
        targetLabel: typeof payload.target === "string" ? payload.target : undefined,
        verbs: readVerbs(payload.verbs ?? (payload.data as Record<string, unknown> | undefined)?.verbs),
      });
    } catch {
      if (token !== menuRequest.current) return;
      setMenu({ target, verbs: [], error: "SERVER MENU UNAVAILABLE" });
    }
  }

  function setClock(paused: boolean, speed = world.speed): void {
    clientRef.current?.sendClock(paused, speed);
    setWorld((current) => ({ ...current, paused, speed }));
  }

  function submitInput(event: FormEvent): void {
    event.preventDefault();
    const value = input.trim();
    if (!value) return;
    setEchoes((current) => [...current.slice(-3), value]);
    setInput("");
  }

  return <main className="shell" onClick={() => menu && setMenu(null)} onContextMenu={(event) => event.preventDefault()}>
    <div ref={hostRef} className="world" aria-label="EtherBound world" />
    <section className="hud" aria-label="Niko HUD">
      <div className={`hud-panel clock ${world.paused ? "paused" : ""}`} onClick={(event) => event.stopPropagation()}>
        <span className="time">{formatGameTime(world.gameMinute)}</span>
        <button className="mini" aria-label="Pause" onClick={() => setClock(!world.paused)}>II</button>
        {[1, 3, 10].map((speed) => <button key={speed} className={`mini ${!world.paused && world.speed === speed ? "active" : ""}`} onClick={() => setClock(false, speed)}>x{speed}</button>)}
      </div>
      {telemetry && <div className="hud-panel telemetry" aria-label="Position and speed">
        <div className="readout"><span className="label">POS</span><span>{formatPosition(telemetry)}</span></div>
        <div className="readout"><span className="label">SPD</span><span>{telemetry.speed.toFixed(2)} m/s</span></div>
      </div>}
      <div className="hud-panel status" onClick={(event) => event.stopPropagation()}>
        <span className={`pill ${connection === "open" ? "ok" : "warn"}`}>{statusLabel(connection)}</span>
        <span className="pill ether">ETHER: 00%</span>
      </div>
      <div className="hud-panel feed" aria-live="polite" onClick={(event) => event.stopPropagation()}>
        <div className="feed-label">▍INPUT ECHO</div>
        {echoes.map((echo, index) => <div className="feed-item" key={`${echo}-${index}`}><span className="time">{formatGameTime(world.gameMinute)}</span>{echo}</div>)}
      </div>
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
    {menu && <ContextMenu state={menu} onClose={() => setMenu(null)} />}
  </main>;
}

function ContextMenu({ state, onClose }: { state: ResolvedMenuState; onClose: () => void }): ReactElement {
  return <div className="menu" style={{ left: Math.min(state.target.screenX, window.innerWidth - 220), top: Math.min(state.target.screenY, window.innerHeight - 180) }} onClick={(event) => event.stopPropagation()}>
    <div className="target">TILE {state.target.x},{state.target.y},{state.target.z}</div>
    {state.error ? <div className="menu-empty">{state.error}</div> : state.verbs.length === 0 ? <div className="menu-empty">NO ACTIONS RETURNED</div> : state.verbs.map((verb) => <button key={verb.verb} className={`verb ${verb.tag === "ether" ? "ether" : ""} ${verb.available === false ? "off" : ""}`} disabled={verb.available === false} onClick={onClose}>
      <span>{verb.label ?? verb.verb}</span>{verb.reason && <small>{verb.reason}</small>}
    </button>)}
  </div>;
}
