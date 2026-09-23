import type { ChunkStore } from "../world/ChunkStore";
import { canEnter, stepMultiplier } from "../world/rules";
import type { Direction, Position } from "./protocol";

export const WALK_SPEED = 4;

type PendingInput = { sequence: number; direction: Direction; dt: number };

function normalize(direction: Direction): Direction {
  const length = Math.hypot(direction.x, direction.y);
  return length > 1 ? { x: direction.x / length, y: direction.y / length } : direction;
}

/** Keeps the local avatar responsive while the server remains authoritative. */
export class ClientPrediction {
  private store: ChunkStore | null = null;
  private authoritative: Position;
  private base: Position;
  private rendered: Position;
  private pending: PendingInput[] = [];
  private direction: Direction = { x: 0, y: 0 };
  private partialSeconds = 0;

  constructor(initial: Position) {
    this.authoritative = { ...initial };
    this.base = { ...initial };
    this.rendered = { ...initial };
  }

  get position(): Position {
    return this.rendered;
  }

  attachStore(store: ChunkStore): void {
    this.store = store;
  }

  pushStep(step: PendingInput): void {
    const input = { ...step, direction: normalize(step.direction) };
    this.pending.push(input);
    this.advance(this.base, input.direction, input.dt);
  }

  /** No key held and every input acknowledged, so the server position is the whole truth. */
  idle(): boolean {
    return this.direction.x === 0 && this.direction.y === 0 && this.partialSeconds === 0 && this.pending.length === 0;
  }

  private advance(position: Position, direction: Direction, seconds: number): void {
    const store = this.store;
    if (!store) {
      position.x += direction.x * WALK_SPEED * seconds;
      position.y += direction.y * WALK_SPEED * seconds;
      return;
    }
    let remaining = seconds * WALK_SPEED;
    const substep = 0.05;
    while (remaining > 0) {
      const step = Math.min(substep, remaining);
      const nextX = position.x + direction.x * step;
      const nextY = position.y + direction.y * step;
      const h = position.h ?? 0;
      const sourceX = position.x;
      const sourceY = position.y;
      if (canEnter(store, position.x, position.y, nextX, position.y, h)) {
        position.x = nextX;
      }
      if (canEnter(store, position.x, position.y, position.x, nextY, h)) {
        position.y = nextY;
      }
      const standing = store.standingH(Math.floor(position.x), Math.floor(position.y), h);
      if (standing !== undefined) {
        const delta = standing - h;
        if (Math.floor(position.x) !== Math.floor(sourceX) || Math.floor(position.y) !== Math.floor(sourceY)) {
          position.h = standing;
          position.z = Math.floor(standing / 6);
        }
        remaining -= step / stepMultiplier(delta, store.walkCost(position.x, position.y, standing));
      } else {
        remaining -= step;
      }
    }
  }

  reconcile(serverPosition: Position, acknowledgedSequence?: number): Position {
    this.authoritative = { ...serverPosition };
    if (acknowledgedSequence !== undefined) {
      this.pending = this.pending.filter((input) => input.sequence > acknowledgedSequence);
    }
    this.rebuildBase();
    return this.rendered;
  }

  render(direction: Direction, partialSeconds: number): Position {
    this.direction = normalize(direction);
    this.partialSeconds = partialSeconds;
    this.rendered = { ...this.base };
    if (partialSeconds > 0) this.advance(this.rendered, this.direction, partialSeconds);
    return this.rendered;
  }

  private rebuildBase(): void {
    this.base = { ...this.authoritative };
    for (const input of this.pending) this.advance(this.base, input.direction, input.dt);
    this.rendered = { ...this.base };
  }

  reset(position: Position): void {
    this.authoritative = { ...position };
    this.base = { ...position };
    this.rendered = { ...position };
    this.pending = [];
    this.direction = { x: 0, y: 0 };
    this.partialSeconds = 0;
  }
}
