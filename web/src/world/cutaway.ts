import type { ChunkStore } from "./ChunkStore";
import { NO_FLOOR, wallBetween } from "./rules";

export function cutoffH(store: ChunkStore, x: number, y: number, viewerH: number): number {
  const nx = Math.floor(x);
  const ny = Math.floor(y);
  const ground = store.groundH(nx, ny);
  if (ground !== undefined && ground > viewerH) return viewerH + 4;

  for (let dy = -1; dy <= 1; dy += 1) {
    for (let dx = -1; dx <= 1; dx += 1) {
      const tx = nx + dx;
      const ty = ny + dy;
      const roofedByFloor = store.levelsAt(tx, ty).some((level) => {
        const floorH = store.levelCell(tx, ty, level.z)?.floor_h;
        return floorH !== undefined && floorH !== NO_FLOOR && floorH > viewerH;
      });
      if (!roofedByFloor) continue;

      if (dx === 0 && dy === 0) return viewerH + 4;
      if (dx === 0 || dy === 0) {
        if (!wallBetween(store, nx, ny, tx, ty, viewerH)) return viewerH + 4;
        continue;
      }

      const xStep = !wallBetween(store, nx, ny, tx, ny, viewerH) &&
        !wallBetween(store, tx, ny, tx, ty, viewerH);
      const yStep = !wallBetween(store, nx, ny, nx, ty, viewerH) &&
        !wallBetween(store, nx, ty, tx, ty, viewerH);
      if (xStep || yStep) return viewerH + 4;
    }
  }

  return Infinity;
}
