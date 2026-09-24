import type { Position } from "../net/protocol";

/** Extras get the green token, never Niko's blue and never Ether's magenta. */
export const EXTRA_COLOR = 0x5af78e;

/** Tick intervals outside this window are not believable mixes, so the ends clamp. */
export const MIN_TICK_MS = 50;
export const MAX_TICK_MS = 1500;

export function clampInterval(intervalMs: number): number {
  if (!Number.isFinite(intervalMs)) return MAX_TICK_MS;
  return Math.min(MAX_TICK_MS, Math.max(MIN_TICK_MS, intervalMs));
}

/** 0 at the previous tick, 1 at the latest, clamped outside the interval. */
export function interpolationFactor(elapsedMs: number, intervalMs: number): number {
  if (!(intervalMs > 0)) return 1;
  const t = elapsedMs / intervalMs;
  return t <= 0 ? 0 : t >= 1 ? 1 : t;
}

export function mixPosition(previous: Position, latest: Position, t: number): Position {
  return {
    x: previous.x + (latest.x - previous.x) * t,
    y: previous.y + (latest.y - previous.y) * t,
    z: latest.z,
    h: latest.h,
  };
}

/**
 * Smooths the body between tick broadcasts. The first sighting snaps: an Extra never slides in
 * from the world origin, and a body that stopped stays where it stopped.
 */
export class ActorInterpolator {
  private previous: Position | null = null;
  private latest: Position | null = null;
  private observedAt = 0;
  private interval = MAX_TICK_MS;

  update(position: Position, now: number): void {
    if (this.latest === null) {
      this.latest = { ...position };
      this.observedAt = now;
      return;
    }
    const moved =
      position.x !== this.latest.x || position.y !== this.latest.y || position.h !== this.latest.h;
    if (moved) {
      this.interval = clampInterval(now - this.observedAt);
      this.previous = this.latest;
    } else if (this.previous !== null) {
      // It repeated a spot mid-mix: finish the mix at the latest position instead of restarting.
      this.previous = this.latest;
    }
    this.latest = { ...position };
    this.observedAt = now;
  }

  sample(now: number): Position | null {
    if (this.latest === null) return null;
    if (this.previous === null) return { ...this.latest };
    return mixPosition(this.previous, this.latest, interpolationFactor(now - this.observedAt, this.interval));
  }

  reset(): void {
    this.previous = null;
    this.latest = null;
    this.interval = MAX_TICK_MS;
  }
}