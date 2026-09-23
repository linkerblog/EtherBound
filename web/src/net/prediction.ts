import type { ChunkStore } from "../world/ChunkStore";
import { BODY_RADIUS_METRES, canEnter, stepMultiplier } from "../world/rules";
import type { Direction, Position } from "./protocol";

export const WALK_SPEED = 4;

export function loadMultiplier(loadKg: number): number {
  return loadKg <= 10 ? 1 : Math.max(0.5, 1 - (loadKg - 10) / 60);
}

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
  private loadKg = 0;

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

  setLoadKg(loadKg: number): void {
    this.loadKg = Math.max(0, loadKg);
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
    const distance = WALK_SPEED * loadMultiplier(this.loadKg) * seconds;
    if (!store) {
      position.x += direction.x * distance;
      position.y += direction.y * distance;
      return;
    }
    let remaining = distance;
    const substep = 0.05;
    let blockedX = direction.x === 0;
    let blockedY = direction.y === 0;
    while (remaining > 0) {
      const step = Math.min(substep, remaining);
      const nextX = position.x + direction.x * step;
      const nextY = position.y + direction.y * step;
      const h = position.h ?? 0;
      const sourceX = position.x;
      const sourceY = position.y;
      let moved = false;
      let collided = false;
      if (!blockedX) {
        if (canEnter(store, position.x, position.y, nextX, position.y, h)) {
          position.x = nextX;
          moved = true;
        } else if (Math.floor(nextX) !== Math.floor(position.x)) {
          const boundary = Math.floor(position.x) + (direction.x > 0 ? 1 : 0);
          position.x = boundary + (direction.x > 0 ? -BODY_RADIUS_METRES : BODY_RADIUS_METRES);
          blockedX = true;
          collided = true;
        }
      }
      if (!blockedY) {
        if (canEnter(store, position.x, position.y, position.x, nextY, h)) {
          position.y = nextY;
          moved = true;
        } else if (Math.floor(nextY) !== Math.floor(position.y)) {
          const boundary = Math.floor(position.y) + (direction.y > 0 ? 1 : 0);
          position.y = boundary + (direction.y > 0 ? -BODY_RADIUS_METRES : BODY_RADIUS_METRES);
          blockedY = true;
          collided = true;
        }
      }
      if (!moved && !collided) break;
      const standing = store.standingH(Math.floor(position.x), Math.floor(position.y), h);
      if (standing !== undefined) {
        const delta = standing - h;
        if (Math.floor(position.x) !== Math.floor(sourceX) || Math.floor(position.y) !== Math.floor(sourceY)) {
          position.h = standing;
          position.z = Math.floor(standing / 6);
        }
        remaining -= moved
          ? step / stepMultiplier(delta, store.walkCost(position.x, position.y, standing))
          : step;
      } else {
        remaining -= step;
      }
      if (blockedX && blockedY) break;
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
