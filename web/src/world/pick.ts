import type { ChunkStore } from "./ChunkStore";
import { LEVEL_VOID, NO_FLOOR } from "./rules";

export type TilePick = { x: number; y: number; z: number };

export function pickTile(
  store: ChunkStore,
  s: number,
  t: number,
  viewerH: number,
  cutoff: number,
): TilePick | null {
  for (let step = 0; step <= 160; step += 1) {
    const hs = viewerH + 40 - step * 0.5;
    const x = Math.floor((s + t + hs) / 2);
    const y = Math.floor((t + hs - s) / 2);

    for (const level of store.levelsAt(x, y)) {
      const cell = store.levelCell(x, y, level.z);
      if (
        cell &&
        cell.floor_h !== NO_FLOOR &&
        (cell.flags & LEVEL_VOID) === 0 &&
        cell.floor_h <= cutoff &&
        hs <= cell.floor_h &&
        cell.floor_h < hs + 0.5
      ) {
        return { x, y, z: Math.floor(cell.floor_h / 6) };
      }
    }

    const groundH = store.groundH(x, y);
    if (groundH === undefined || hs > groundH) continue;
    const hasBuildingFloor = store.levelsAt(x, y).some((level) => {
      const floorH = store.levelCell(x, y, level.z)?.floor_h;
      return floorH !== undefined && floorH !== NO_FLOOR;
    });
    if (hasBuildingFloor && groundH > cutoff) continue;
    return { x, y, z: Math.floor(groundH / 6) };
  }

  return null;
}
