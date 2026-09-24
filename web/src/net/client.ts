import {
  asRecord,
  readActorPosition,
  readMessage,
  readWorldState,
  type ActivityMessage,
  type ConnectionState,
  type Direction,
  type GameAction,
  type GameStateResponse,
  type GeneratorInfo,
  type Position,
  type ResultMessage,
  type ServerMessage,
  type WorldState,
} from "./protocol";
import type { components } from "./schema";
type WorldChunk = components["schemas"]["ChunkResponse"];

type Listener = (state: WorldState) => void;
type ConnectionListener = (state: ConnectionState) => void;
type AckListener = (position: Position | undefined, sequence?: number) => void;
type ChunkListener = (chunk: WorldChunk) => void;
type SnapshotListener = (state: WorldState) => void;
type ResultListener = (result: ResultMessage) => void;
type ActivityListener = (activity: ActivityMessage) => void;

function asChunk(value: ServerMessage): WorldChunk | null {
  const numeric = (field: unknown): field is number[] =>
    Array.isArray(field) && field.every((entry) => typeof entry === "number");
  if (
    typeof value.cx !== "number" ||
    typeof value.cy !== "number" ||
    typeof value.revision !== "number" ||
    !numeric(value.ground_h) ||
    !numeric(value.surface_mat) ||
    !Array.isArray(value.levels)
  ) {
    return null;
  }
  return value as unknown as WorldChunk;
}

const initialWorld: WorldState = { gameMinute: 0, paused: false, speed: 1, actors: {} };

// The Vite dev server is ready seconds before the API server, and hot reload restarts it, so a
// socket can fail to open or drop mid-session. Retry with a small backoff until it holds.
const RECONNECT_MIN_MS = 250;
const RECONNECT_MAX_MS = 4000;

function defaultSocketUrl(): string {
  if (import.meta.env.VITE_WS_URL) return import.meta.env.VITE_WS_URL;
  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  return `${protocol}//${window.location.host}/ws`;
}

function sequenceOf(message: ServerMessage): number | undefined {
  const record = asRecord(message.data) ?? message;
  return typeof record.sequence === "number" ? record.sequence : typeof record.seq === "number" ? record.seq : undefined;
}

export async function requestNewGame(
  seed: number,
  generator?: string,
  options?: Record<string, unknown>,
): Promise<void> {
  const response = await fetch("/api/game/new", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ seed, generator, options }),
  });
  if (!response.ok) throw new Error(`new game request ${response.status}`);
}

export async function fetchGenerators(): Promise<GeneratorInfo[]> {
  const response = await fetch("/api/gen");
  if (!response.ok) throw new Error(`generator request ${response.status}`);
  return (await response.json()) as GeneratorInfo[];
}

export async function fetchGameState(): Promise<GameStateResponse> {
  const response = await fetch("/api/game/state");
  if (!response.ok) throw new Error(`state request ${response.status}`);
  return (await response.json()) as GameStateResponse;
}

export class WebSocketClient {
  private socket: WebSocket | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private reconnectDelay = RECONNECT_MIN_MS;
  private url: string | null = null;
  private closed = false;
  private world: WorldState = initialWorld;
  // The server sends each chunk once per connection, so the client must keep every one it has
  // received and replay it to listeners that appear later (the Phaser scene loads after connect).
  private readonly chunks = new Map<string, WorldChunk>();
  private sequence = 0;
  private readonly listeners = new Set<Listener>();
  private readonly connectionListeners = new Set<ConnectionListener>();
  private readonly ackListeners = new Set<AckListener>();
  private readonly chunkListeners = new Set<ChunkListener>();
  private readonly snapshotListeners = new Set<SnapshotListener>();
  private readonly resultListeners = new Set<ResultListener>();
  private readonly activityListeners = new Set<ActivityListener>();
  private beforeCommand: (() => void) | null = null;

  connect(url = defaultSocketUrl()): void {
    this.closed = false;
    this.url = url;
    this.open(url);
  }

  disconnect(): void {
    this.closed = true;
    this.url = null;
    this.clearReconnect();
    this.socket?.close();
    this.socket = null;
    this.chunks.clear();
  }

  get current(): WorldState {
    return this.world;
  }

  nextSequence(): number {
    this.sequence += 1;
    return this.sequence;
  }

  sendInput(direction: Direction, dt: number, sequence = this.nextSequence()): number {
    this.send({ type: "input", sequence, dx: direction.x, dy: direction.y, dt });
    return sequence;
  }

  setBeforeCommand(handler: (() => void) | null): void {
    this.beforeCommand = handler;
  }

  /** Submits an action exactly as the server's menu built it. */
  sendAction(action: GameAction): number {
    this.beforeCommand?.();
    const sequence = this.nextSequence();
    this.send({ type: "action", sequence, action });
    return sequence;
  }

  sendClock(paused: boolean, speed: number): void {
    if (paused) this.beforeCommand?.();
    this.send({ type: "clock", paused, speed });
  }

  onState(listener: Listener): () => void {
    this.listeners.add(listener);
    listener(this.world);
    return () => this.listeners.delete(listener);
  }

  onConnection(listener: ConnectionListener): () => void {
    this.connectionListeners.add(listener);
    return () => this.connectionListeners.delete(listener);
  }

  onAck(listener: AckListener): () => void {
    this.ackListeners.add(listener);
    return () => this.ackListeners.delete(listener);
  }

  onChunk(listener: ChunkListener): () => void {
    this.chunkListeners.add(listener);
    for (const chunk of this.chunks.values()) listener(chunk);
    return () => this.chunkListeners.delete(listener);
  }

  onSnapshot(listener: SnapshotListener): () => void {
    this.snapshotListeners.add(listener);
    return () => this.snapshotListeners.delete(listener);
  }

  onResult(listener: ResultListener): () => void {
    this.resultListeners.add(listener);
    return () => this.resultListeners.delete(listener);
  }

  onActivity(listener: ActivityListener): () => void {
    this.activityListeners.add(listener);
    return () => this.activityListeners.delete(listener);
  }

  private open(url: string): void {
    this.emitConnection("connecting");
    const socket = new WebSocket(url);
    this.socket = socket;
    socket.addEventListener("open", () => {
      if (this.socket !== socket) return;
      this.reconnectDelay = RECONNECT_MIN_MS;
      this.emitConnection("open");
    });
    socket.addEventListener("close", () => {
      if (this.socket !== socket) return;
      this.emitConnection("closed");
      this.scheduleReconnect();
    });
    socket.addEventListener("error", () => {
      if (this.socket !== socket) return;
      this.emitConnection("error");
    });
    socket.addEventListener("message", (event) => this.receive(event.data));
  }

  private scheduleReconnect(): void {
    if (this.closed || this.url === null || this.reconnectTimer !== null) return;
    const delay = this.reconnectDelay;
    this.reconnectDelay = Math.min(this.reconnectDelay * 2, RECONNECT_MAX_MS);
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      if (!this.closed && this.url !== null) this.open(this.url);
    }, delay);
  }

  private clearReconnect(): void {
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    this.reconnectDelay = RECONNECT_MIN_MS;
  }

  private send(message: Record<string, unknown>): void {
    if (this.socket?.readyState === WebSocket.OPEN) this.socket.send(JSON.stringify(message));
  }

  private receive(raw: unknown): void {
    let parsed: unknown;
    try {
      parsed = typeof raw === "string" ? JSON.parse(raw) : raw;
    } catch {
      return;
    }
    const message = readMessage(parsed);
    if (!message) return;
    if (message.type === "snapshot" || message.type === "tick") {
      this.world = readWorldState(message, this.world);
      if (message.type === "snapshot") {
        // Clear before the listeners run: the scene drops its own store on snapshot, so replaying
        // cached chunks from the previous world afterwards would bring them back.
        this.chunks.clear();
        this.snapshotListeners.forEach((listener) => listener(this.world));
      }
      this.listeners.forEach((listener) => listener(this.world));
    }
    if (message.type === "ack") {
      const sequence = sequenceOf(message);
      const position = readActorPosition(message, "player") ?? this.world.actors.player;
      this.ackListeners.forEach((listener) => listener(position, sequence));
    }
    if (message.type === "chunk") {
      const chunk = asChunk(message);
      if (!chunk) return;
      const key = `${chunk.cx},${chunk.cy}`;
      const cached = this.chunks.get(key);
      if (!cached || chunk.revision > cached.revision) this.chunks.set(key, chunk);
      this.chunkListeners.forEach((listener) => listener(chunk));
    }
    if (message.type === "result" && typeof message.sequence === "number" && typeof message.accepted === "boolean") {
      const result = message as unknown as ResultMessage;
      this.resultListeners.forEach((listener) => listener(result));
    }
    if (message.type === "activity" && typeof message.op === "string" && typeof message.outcome === "string") {
      const activity = message as unknown as ActivityMessage;
      this.activityListeners.forEach((listener) => listener(activity));
    }
  }

  private emitConnection(state: ConnectionState): void {
    this.connectionListeners.forEach((listener) => listener(state));
  }
}
