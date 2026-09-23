import type { ChunkStore } from "../world/ChunkStore";
import { canEnter, slopeMultiplier } from "../world/rules";
import type { Direction, Position } from "./protocol";

export const WALK_SPEED = 4;

type PendingInput = { sequence: number; direction: Direction };

function normalize(direction: Direction): Direction {
  const length = Math.hypot(direction.x, direction.y);
  return length > 1 ? { x: direction.x / length, y: direction.y / length } : direction;
}

/** Keeps the local avatar responsive while the server remains authoritative. */
export class ClientPrediction {
  private store: ChunkStore | null = null;
  private authoritative: Position;
  private predicted: Position;
  private pending: PendingInput[] = [];
  private direction: Direction = { x: 0, y: 0 };
  private lastServerUpdate = performance.now();

  constructor(initial: Position) {
    this.authoritative = { ...initial };
    this.predicted = { ...initial };
  }

  get position(): Position {
    return this.predicted;
  }

  attachStore(store: ChunkStore): void {
    this.store = store;
  }

  setDirection(direction: Direction, sequence: number): void {
    this.direction = normalize(direction);
    this.pending.push({ sequence, direction: this.direction });
  }

  /** No key held and every input acknowledged, so the server position is the whole truth. */
  idle(): boolean {
    return this.direction.x === 0 && this.direction.y === 0 && this.pending.length === 0;
  }

  step(seconds: number, paused: boolean): Position {
    if (!paused && seconds > 0) this.advance(seconds);
    return this.predicted;
  }

  private advance(seconds: number): void {
    const store = this.store;
    if (!store) {
      this.predicted = {
        ...this.predicted,
        x: this.predicted.x + this.direction.x * WALK_SPEED * seconds,
        y: this.predicted.y + this.direction.y * WALK_SPEED * seconds,
      };
      return;
    }
    let remaining = seconds * WALK_SPEED;
    const substep = 0.05;
    while (remaining > 0) {
      const step = Math.min(substep, remaining);
      const nextX = this.predicted.x + this.direction.x * step;
      const nextY = this.predicted.y + this.direction.y * step;
      const h = this.predicted.h ?? 0;
      const sourceX = this.predicted.x;
      const sourceY = this.predicted.y;
      if (canEnter(store, this.predicted.x, this.predicted.y, nextX, this.predicted.y, h)) {
        this.predicted.x = nextX;
      }
      if (canEnter(store, this.predicted.x, this.predicted.y, this.predicted.x, nextY, h)) {
        this.predicted.y = nextY;
      }
      const standing = store.standingH(Math.floor(this.predicted.x), Math.floor(this.predicted.y), h);
      if (standing !== undefined) {
        const delta = standing - h;
        if (Math.floor(this.predicted.x) !== Math.floor(sourceX) || Math.floor(this.predicted.y) !== Math.floor(sourceY)) {
          this.predicted.h = standing;
          this.predicted.z = Math.floor(standing / 6);
        }
        remaining -= step / slopeMultiplier(delta);
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
    this.predicted = { ...this.authoritative };
    const elapsed = Math.min((performance.now() - this.lastServerUpdate) / 1000, 0.25);
    this.lastServerUpdate = performance.now();
    if (elapsed > 0) this.advance(elapsed);
    return this.predicted;
  }

  reset(position: Position): void {
    this.authoritative = { ...position };
    this.predicted = { ...position };
    this.pending = [];
  }
}
