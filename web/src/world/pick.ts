import type { ChunkStore } from "./ChunkStore";
import { marchRay, type CutoffAt } from "./ray";

export type TilePick = { x: number; y: number; z: number };

export function pickTile(
  store: ChunkStore,
  s: number,
  t: number,
  viewerH: number,
  cutoffAt: CutoffAt,
): TilePick | null {
  const hit = marchRay(store, s, t, viewerH + 40, viewerH - 40, cutoffAt);
  return hit ? { x: hit.x, y: hit.y, z: hit.z } : null;
}
