import type { ChunkStore } from "./ChunkStore";
import { EDGE_N_DOORWAY, EDGE_W_DOORWAY, NO_FLOOR, wallBetween } from "./rules";

export const CUT_DEPTH = 8;
export const CUT_WIDTH = 3;
export const MAX_WALL_H = 6;

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

export type TilePosition = { x: number; y: number };
export type WallEdge = "n" | "w";

function shownWall(store: ChunkStore, edge: WallEdge, x: number, y: number, viewerH: number): boolean {
  const doorway = edge === "n" ? EDGE_N_DOORWAY : EDGE_W_DOORWAY;
  return store.levelsAt(x, y).some((level) => {
    const cell = store.levelCell(x, y, level.z);
    if (!cell) return false;
    const wall = edge === "n" ? cell.wall_n : cell.wall_w;
    if (wall === 0 || (cell.edge_flags & doorway) !== 0) return false;
    const base = store.wallBaseH(x, y, level.z, cell.floor_h);
    return base < viewerH + 4 && base + MAX_WALL_H > viewerH;
  });
}

function gridCell(value: number): number {
  const nearest = Math.round(value);
  return Math.abs(value - nearest) < 1e-9 ? nearest : Math.floor(value);
}

export function wallInTheWay(store: ChunkStore, from: TilePosition, to: TilePosition, viewerH: number): boolean {
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  const minX = Math.min(from.x, to.x);
  const maxX = Math.max(from.x, to.x);
  const minY = Math.min(from.y, to.y);
  const maxY = Math.max(from.y, to.y);

  if (dx !== 0) {
    for (let x = Math.floor(minX) + 1; x < maxX; x += 1) {
      const t = (x - from.x) / dx;
      if (shownWall(store, "w", x, gridCell(from.y + dy * t), viewerH)) return true;
    }
  }
  if (dy !== 0) {
    for (let y = Math.floor(minY) + 1; y < maxY; y += 1) {
      const t = (y - from.y) / dy;
      if (shownWall(store, "n", gridCell(from.x + dx * t), y, viewerH)) return true;
    }
  }
  return false;
}

export function isFrontWall(
  store: ChunkStore,
  edge: WallEdge,
  x: number,
  y: number,
  base: number,
  nikoTile: TilePosition,
  viewerH: number,
): boolean {
  const { x: nx, y: ny } = nikoTile;
  const mx = edge === "n" ? x + 0.5 : x;
  const my = edge === "n" ? y : y + 0.5;
  const inWindow = (edge === "n" ? y > ny : x > nx) &&
    (mx + my) - (nx + ny + 1) > 0 && (mx + my) - (nx + ny + 1) <= CUT_DEPTH &&
    Math.abs((mx - my) - (nx - ny)) <= CUT_WIDTH &&
    base < viewerH + 4 && base + MAX_WALL_H > viewerH;
  if (!inWindow) return false;

  const from = { x: nx + 0.5, y: ny + 0.5 };
  const to = { x: mx, y: my };
  return !wallInTheWay(store, from, to, viewerH);
}
