import {
  asRecord,
  readActorPosition,
  readMessage,
  readWorldState,
  type ActivityMessage,
  type ConnectionState,
  type Direction,
  type GameAction,
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

function defaultSocketUrl(): string {
  if (import.meta.env.VITE_WS_URL) return import.meta.env.VITE_WS_URL;
  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  return `${protocol}//${window.location.host}/ws`;
}

function sequenceOf(message: ServerMessage): number | undefined {
  const record = asRecord(message.data) ?? message;
  return typeof record.sequence === "number" ? record.sequence : typeof record.seq === "number" ? record.seq : undefined;
}

export async function requestNewGame(seed: number): Promise<void> {
  const response = await fetch("/api/game/new", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ seed }),
  });
  if (!response.ok) throw new Error(`new game request ${response.status}`);
}

export class WebSocketClient {
  private socket: WebSocket | null = null;
  private world: WorldState = initialWorld;
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
    this.emitConnection("connecting");
    this.socket = new WebSocket(url);
    this.socket.addEventListener("open", () => this.emitConnection("open"));
    this.socket.addEventListener("close", () => this.emitConnection("closed"));
    this.socket.addEventListener("error", () => this.emitConnection("error"));
    this.socket.addEventListener("message", (event) => this.receive(event.data));
  }

  disconnect(): void {
    this.socket?.close();
    this.socket = null;
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
      if (message.type === "snapshot") this.snapshotListeners.forEach((listener) => listener(this.world));
      this.listeners.forEach((listener) => listener(this.world));
    }
    if (message.type === "ack") {
      const sequence = sequenceOf(message);
      const position = readActorPosition(message, "player") ?? this.world.actors.player;
      this.ackListeners.forEach((listener) => listener(position, sequence));
    }
    if (message.type === "chunk") {
      const chunk = asChunk(message);
      if (chunk) this.chunkListeners.forEach((listener) => listener(chunk));
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
