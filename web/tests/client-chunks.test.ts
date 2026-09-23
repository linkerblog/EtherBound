import assert from "node:assert/strict";
import test from "node:test";
import type { components } from "../src/net/schema";
import { WebSocketClient } from "../src/net/client";

type Chunk = components["schemas"]["ChunkResponse"];

class FakeSocket {
  static readonly OPEN = 1;
  static latest: FakeSocket | null = null;
  readonly readyState = FakeSocket.OPEN;
  private readonly handlers = new Map<string, Array<(event: { data: unknown }) => void>>();

  constructor(readonly url: string) {
    FakeSocket.latest = this;
  }

  addEventListener(type: string, listener: (event: { data: unknown }) => void): void {
    const list = this.handlers.get(type) ?? [];
    list.push(listener);
    this.handlers.set(type, list);
  }

  close(): void {}

  emit(data: unknown): void {
    for (const listener of this.handlers.get("message") ?? []) listener({ data: JSON.stringify(data) });
  }

  emitClose(): void {
    for (const listener of this.handlers.get("close") ?? []) listener({ data: undefined });
  }
}

(globalThis as { WebSocket?: unknown }).WebSocket = FakeSocket;

function chunk(cx: number, cy: number, revision: number): Chunk {
  return { cx, cy, revision, ground_h: [], surface_mat: [], levels: [] } as unknown as Chunk;
}

function connect(): { client: WebSocketClient; socket: FakeSocket } {
  const client = new WebSocketClient();
  client.connect("ws://test");
  assert.ok(FakeSocket.latest);
  return { client, socket: FakeSocket.latest };
}

test("chunks received before any listener are replayed to a late listener", () => {
  const { client, socket } = connect();
  socket.emit({ type: "chunk", ...chunk(0, 0, 1) });

  const received: Chunk[] = [];
  client.onChunk((entry) => received.push(entry));
  assert.equal(received.length, 1);
  assert.equal(received[0].revision, 1);
});

test("a snapshot clears the cache so a late listener receives nothing old", () => {
  const { client, socket } = connect();
  socket.emit({ type: "chunk", ...chunk(0, 0, 1) });
  socket.emit({ type: "snapshot" });

  const received: Chunk[] = [];
  client.onChunk((entry) => received.push(entry));
  assert.equal(received.length, 0);
});

test("an older revision does not replace a newer cached one", () => {
  const { client, socket } = connect();
  socket.emit({ type: "chunk", ...chunk(0, 0, 2) });
  socket.emit({ type: "chunk", ...chunk(0, 0, 1) });

  const received: Chunk[] = [];
  client.onChunk((entry) => received.push(entry));
  assert.equal(received.length, 1);
  assert.equal(received[0].revision, 2);
});

test("disconnect clears the cache", () => {
  const { client, socket } = connect();
  socket.emit({ type: "chunk", ...chunk(0, 0, 1) });
  client.disconnect();

  const received: Chunk[] = [];
  client.onChunk((entry) => received.push(entry));
  assert.equal(received.length, 0);
});

test("a dropped socket reconnects", async () => {
  const { socket } = connect();
  socket.emitClose();
  await new Promise((resolve) => setTimeout(resolve, 600));
  assert.notEqual(FakeSocket.latest, socket);
});

test("disconnect stops reconnecting", async () => {
  const { client, socket } = connect();
  client.disconnect();
  socket.emitClose();
  await new Promise((resolve) => setTimeout(resolve, 600));
  assert.equal(FakeSocket.latest, socket);
});
