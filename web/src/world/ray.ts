import type { ChunkStore } from "./ChunkStore";
import { NO_FLOOR } from "./rules";

export type RayHit = { x: number; y: number; h: number; kind: "floor" | "ground"; z: number };
export type CutoffAt = (x: number, y: number) => number;

export function marchRay(
  store: ChunkStore,
  s: number,
  t: number,
  fromH: number,
  toH: number,
  cutoffAt: CutoffAt,
): RayHit | null {
  for (let step = 0; ; step += 1) {
    const h = fromH - step * 0.5;
    if (h < toH) break;
    const x = Math.floor((s + t + h) / 2);
    const y = Math.floor((t + h - s) / 2);

    for (const level of store.levelsAt(x, y)) {
      const cell = store.levelCell(x, y, level.z);
      if (
        cell &&
        cell.floor_h !== NO_FLOOR &&
        cell.floor_h <= cutoffAt(x, y) &&
        h <= cell.floor_h &&
        cell.floor_h < h + 0.5
      ) {
        return { x, y, h: cell.floor_h, kind: "floor", z: Math.floor(cell.floor_h / 6) };
      }
    }

    const groundH = store.groundH(x, y);
    if (groundH === undefined || h > groundH || store.isVoid(x, y, groundH)) continue;
    const hasBuildingFloor = store.levelsAt(x, y).some((level) => {
      const floorH = store.levelCell(x, y, level.z)?.floor_h;
      return floorH !== undefined && floorH !== NO_FLOOR;
    });
    if (hasBuildingFloor && groundH > cutoffAt(x, y)) continue;
    return { x, y, h: groundH, kind: "ground", z: Math.floor(groundH / 6) };
  }

  return null;
}
