import type { PhysicsPosition } from "../net/protocol";
import Phaser from "phaser";
import type { ResultMessage } from "../net/protocol";
import { rowDepth, toScreen } from "./iso";

export function interpolateTrajectory(
  path: PhysicsPosition[],
  progress: number,
): PhysicsPosition | null {
  if (!path.length) return null;
  const t = Math.max(0, Math.min(1, progress));
  const position = t * (path.length - 1);
  const index = Math.min(Math.floor(position), path.length - 1);
  const from = path[index]!;
  const to = path[Math.min(index + 1, path.length - 1)]!;
  const blend = position - index;
  return {
    ...from,
    x: from.x + (to.x - from.x) * blend,
    y: from.y + (to.y - from.y) * blend,
    h: from.h + (to.h - from.h) * blend,
  };
}

type PhysicsAnimation = {
  path: PhysicsPosition[];
  startedAt: number;
  duration: number;
  marker: Phaser.GameObjects.Graphics;
};

export class PhysicsAnimator {
  private readonly animations = new Map<string, PhysicsAnimation>();
  readonly animatedObjectIds = new Set<number>();

  constructor(
    private readonly scene: Phaser.Scene,
    private readonly markDirty: (keys: string[]) => void,
    private readonly chunkKeys: () => string[],
  ) {}

  animate(result: ResultMessage): void {
    if (!result.accepted || !result.trajectory?.length) return;
    const grouped = new Map<string, PhysicsPosition[]>();
    for (const point of result.trajectory) {
      const key = `${point.kind}:${point.id}`;
      const path = grouped.get(key) ?? [];
      path.push(point);
      grouped.set(key, path);
    }
    for (const [key, path] of grouped) {
      if (path.length < 2) continue;
      this.animations.get(key)?.marker.destroy();
      const first = path[0]!;
      if (first.kind === "object" && typeof first.id === "number") this.animatedObjectIds.add(first.id);
      const marker = this.scene.add.graphics();
      marker.fillStyle(0xf4d35e, 0.95).fillEllipse(0, 0, 16, 9);
      marker.lineStyle(2, 0xffffff, 0.95).strokeEllipse(0, 0, 16, 9);
      this.animations.set(key, {
        path,
        startedAt: performance.now(),
        duration: Math.min(1400, Math.max(160, (path.length - 1) * 70)),
        marker,
      });
    }
    this.markDirty(this.chunkKeys());
  }

  update(now: number): void {
    let changed = false;
    for (const [key, animation] of this.animations) {
      const progress = Math.min(1, (now - animation.startedAt) / animation.duration);
      const position = interpolateTrajectory(animation.path, progress);
      if (position === null) continue;
      const screen = toScreen(position.x + 0.5, position.y + 0.5, position.h);
      animation.marker
        .setPosition(screen.sx, screen.sy - 3)
        .setDepth(rowDepth(Math.floor(position.x + position.y)));
      if (progress >= 1) {
        animation.marker.destroy();
        this.animations.delete(key);
        const [kind, id] = key.split(":");
        if (kind === "object") this.animatedObjectIds.delete(Number(id));
        changed = true;
      }
    }
    if (changed) this.markDirty(this.chunkKeys());
  }

  clear(): void {
    for (const animation of this.animations.values()) animation.marker.destroy();
    this.animations.clear();
    this.animatedObjectIds.clear();
  }
}
