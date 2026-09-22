export const ZOOM_LEVELS = [1, 2, 3, 4] as const;
export type ZoomLevel = (typeof ZOOM_LEVELS)[number];

export function defaultZoom(devicePixelRatio: number): ZoomLevel {
  return Math.max(2, Math.min(4, Math.floor(devicePixelRatio || 2))) as ZoomLevel;
}

export function stepZoom(level: ZoomLevel, steps: number): ZoomLevel {
  return Math.max(1, Math.min(4, level + steps)) as ZoomLevel;
}

export function loadZoom(fallback: ZoomLevel): ZoomLevel {
  try {
    const stored = localStorage.getItem("etherbound.zoom");
    const match = ZOOM_LEVELS.find((level) => String(level) === stored);
    return match ?? fallback;
  } catch {
    return fallback;
  }
}

export function saveZoom(level: ZoomLevel): void {
  try {
    localStorage.setItem("etherbound.zoom", String(level));
  } catch {
    // Storage may be unavailable in private browsing or restricted contexts.
  }
}

/** Converts wheel deltas into whole zoom steps without skipping trackpad gestures. */
export class WheelAccumulator {
  private total = 0;
  private lastPush: number | null = null;

  push(deltaY: number, deltaMode: number, now: number): number {
    if (!Number.isFinite(deltaY) || !Number.isFinite(now)) return 0;

    if (this.lastPush !== null && now - this.lastPush > 250) this.total = 0;
    this.lastPush = now;

    const delta = deltaY * (deltaMode === 1 ? 40 : deltaMode === 2 ? 800 : 1);
    if (this.total !== 0 && delta !== 0 && Math.sign(delta) !== Math.sign(this.total)) {
      this.total = 0;
    }
    this.total += delta;

    const steps = Math.trunc(Math.abs(this.total) / 100) * -Math.sign(this.total);
    this.total += Math.sign(this.total) * Math.abs(steps) * 100;
    return steps;
  }
}
