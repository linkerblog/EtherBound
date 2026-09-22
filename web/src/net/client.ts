import {
  asRecord,
  readActorPosition,
  readMessage,
  readWorldState,
  type ConnectionState,
  type Direction,
  type Position,
  type ServerMessage,
  type WorldState,
} from "./protocol";

type Listener = (state: WorldState) => void;
type ConnectionListener = (state: ConnectionState) => void;
type AckListener = (position: Position | undefined, sequence?: number) => void;

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

export class WebSocketClient {
  private socket: WebSocket | null = null;
  private world: WorldState = initialWorld;
  private sequence = 0;
  private readonly listeners = new Set<Listener>();
  private readonly connectionListeners = new Set<ConnectionListener>();
  private readonly ackListeners = new Set<AckListener>();

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

  sendInput(direction: Direction, sequence = this.nextSequence()): number {
    this.send({ type: "input", sequence, seq: sequence, direction, vector: direction });
    return sequence;
  }

  sendClock(paused: boolean, speed: number): void {
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
      this.listeners.forEach((listener) => listener(this.world));
    }
    if (message.type === "ack") {
      const sequence = sequenceOf(message);
      const position = readActorPosition(message, "player") ?? this.world.actors.player;
      this.ackListeners.forEach((listener) => listener(position, sequence));
    }
  }

  private emitConnection(state: ConnectionState): void {
    this.connectionListeners.forEach((listener) => listener(state));
  }
}
