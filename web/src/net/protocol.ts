import type { components } from "./schema";

export type Direction = { x: number; y: number };
export type MenuEntry = components["schemas"]["MenuEntry"];
export type MenuResponse = components["schemas"]["MenuResponse"];
export type GameAction = MenuEntry["action"];
export type ActivitySnapshot = components["schemas"]["ActivitySnapshot"];
export type ResultMessage = components["schemas"]["ResultMessage"];
export type ActivityMessage = components["schemas"]["ActivityMessage"];

export type Position = {
  x: number;
  y: number;
  z: number;
  h?: number;
};

export type ActorState = Position & {
  id: string;
  kind?: string;
  activity?: ActivitySnapshot | null;
};

export type WorldState = {
  gameMinute: number;
  paused: boolean;
  speed: number;
  actors: Record<string, ActorState>;
  world?: { chunk_size: number; level_h: number; bounds: number[] };
  genVersion?: number;
  seed?: number;
};

export type ServerMessage = {
  type: string;
  [key: string]: unknown;
};

export type ConnectionState = "connecting" | "open" | "closed" | "error";

export const EMPTY_POSITION: Position = { x: 32, y: 32, z: 0 };

export function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === "object" && value !== null ? (value as Record<string, unknown>) : null;
}

function numberValue(...values: unknown[]): number | undefined {
  return values.find((value): value is number => typeof value === "number" && Number.isFinite(value));
}

function readActivity(value: unknown): ActivitySnapshot | null {
  const record = asRecord(value);
  if (typeof record?.op !== "string") return null;
  const started = numberValue(record.started_minute);
  const ends = numberValue(record.ends_minute);
  return started === undefined || ends === undefined ? null : { op: record.op, started_minute: started, ends_minute: ends };
}

export function readPosition(value: unknown, fallback = EMPTY_POSITION): Position {
  const record = asRecord(value);
  return {
    x: numberValue(record?.x, record?.position && asRecord(record.position)?.x) ?? fallback.x,
    y: numberValue(record?.y, record?.position && asRecord(record.position)?.y) ?? fallback.y,
    z: numberValue(record?.z, record?.position && asRecord(record.position)?.z) ?? fallback.z,
    h: numberValue(record?.h, record?.position && asRecord(record.position)?.h) ?? fallback.h,
  };
}

export function readActors(value: unknown): Record<string, ActorState> {
  if (Array.isArray(value)) {
    return value.reduce<Record<string, ActorState>>((result, entry) => {
      const actor = asRecord(entry);
      const id = typeof actor?.id === "string" ? actor.id : typeof actor?.actor_id === "string" ? actor.actor_id : undefined;
      if (id) result[id] = { id, kind: typeof actor?.kind === "string" ? actor.kind : undefined, activity: readActivity(actor?.activity), ...readPosition(actor) };
      return result;
    }, {});
  }
  const actors = asRecord(value);
  const result: Record<string, ActorState> = {};
  if (!actors) return result;

  for (const [id, rawActor] of Object.entries(actors)) {
    const actor = asRecord(rawActor);
    if (!actor) continue;
    result[id] = { id, kind: typeof actor.kind === "string" ? actor.kind : undefined, activity: readActivity(actor.activity), ...readPosition(actor) };
  }
  return result;
}

export function readMessage(data: unknown): ServerMessage | null {
  const record = asRecord(data);
  return typeof record?.type === "string" ? (record as ServerMessage) : null;
}

export function readWorldState(message: ServerMessage, previous: WorldState): WorldState {
  const payload = asRecord(message.data) ?? message;
  const clock = asRecord(payload.clock);
  const actorPayload = payload.actors ?? asRecord(payload.state)?.actors;
  const actorMap = readActors(actorPayload);
  return {
    gameMinute: numberValue(payload.game_minute, payload.gameMinute, clock?.game_minute, clock?.gameMinute) ?? previous.gameMinute,
    paused: typeof payload.paused === "boolean" ? payload.paused : typeof clock?.paused === "boolean" ? clock.paused : previous.paused,
    speed: numberValue(payload.speed, clock?.speed) ?? previous.speed,
    actors: message.type === "snapshot" || Object.keys(actorMap).length > 0 ? actorMap : previous.actors,
    world: (asRecord(payload.world) as WorldState["world"]) ?? previous.world,
    genVersion: numberValue(payload.gen_version, payload.genVersion) ?? previous.genVersion,
    seed: numberValue(payload.seed) ?? previous.seed,
  };
}

export function readActorPosition(message: ServerMessage, actorId: string): Position | null {
  const payload = asRecord(message.data) ?? message;
  const actors = asRecord(payload.actors);
  const actor = actors?.[actorId] ?? payload.position ?? payload.actor;
  if (actor) return readPosition(actor);
  return typeof payload.x === "number" && typeof payload.y === "number" ? readPosition(payload) : null;
}
