import type { Direction, Position } from "./protocol";

export const WALK_SPEED = 4;

type PendingInput = { sequence: number; direction: Direction };

function normalize(direction: Direction): Direction {
  const length = Math.hypot(direction.x, direction.y);
  return length > 1 ? { x: direction.x / length, y: direction.y / length } : direction;
}

function advance(position: Position, direction: Direction, seconds: number, speed: number): Position {
  const vector = normalize(direction);
  return { ...position, x: position.x + vector.x * speed * seconds, y: position.y + vector.y * speed * seconds };
}

/** Keeps the local avatar responsive while the server remains authoritative. */
export class ClientPrediction {
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

  setDirection(direction: Direction, sequence: number): void {
    this.direction = normalize(direction);
    this.pending.push({ sequence, direction: this.direction });
  }

  step(seconds: number, paused: boolean): Position {
    if (!paused && seconds > 0) this.predicted = advance(this.predicted, this.direction, seconds, WALK_SPEED);
    return this.predicted;
  }

  reconcile(serverPosition: Position, acknowledgedSequence?: number): Position {
    this.authoritative = { ...serverPosition };
    if (acknowledgedSequence !== undefined) {
      this.pending = this.pending.filter((input) => input.sequence > acknowledgedSequence);
    }
    this.predicted = { ...this.authoritative };
    const elapsed = Math.min((performance.now() - this.lastServerUpdate) / 1000, 0.25);
    this.predicted = advance(this.predicted, this.direction, elapsed, WALK_SPEED);
    this.lastServerUpdate = performance.now();
    return this.predicted;
  }

  reset(position: Position): void {
    this.authoritative = { ...position };
    this.predicted = { ...position };
    this.pending = [];
  }
}
