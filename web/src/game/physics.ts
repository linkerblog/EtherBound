import type { PhysicsPosition } from "../net/protocol";

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
