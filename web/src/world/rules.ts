import type { ChunkStore } from "./ChunkStore";

/** Half-metre units: one `h` unit is 0.5 m, a body is 2 m (4 units) tall. */
export const MAX_STEP_H = 1;
export const HEADROOM_H = 4;
export const H_PER_METRE = 2;
export const LEVEL_H = 6;

export const SLOPE_UP_MULTIPLIER = 0.6;
export const SLOPE_DOWN_MULTIPLIER = 0.85;

export const EDGE_N_DOORWAY = 1;
export const EDGE_N_WINDOW = 2;
export const EDGE_W_DOORWAY = 4;
export const EDGE_W_WINDOW = 8;
export const LEVEL_VOID = 1;
export const LEVEL_ROOF = 4;

export function slopeMultiplier(deltaH: number): number {
  if (deltaH > 0) return SLOPE_UP_MULTIPLIER;
  if (deltaH < 0) return SLOPE_DOWN_MULTIPLIER;
  return 1;
}

/** Mirrors the server standing rule for prediction. The server always wins. */
export function canEnter(
  store: ChunkStore,
  fromX: number,
  fromY: number,
  toX: number,
  toY: number,
  h: number,
): boolean {
  const fromTile = { x: Math.floor(fromX), y: Math.floor(fromY) };
  const toTile = { x: Math.floor(toX), y: Math.floor(toY) };
  if (fromTile.x === toTile.x && fromTile.y === toTile.y) return true;
  const diagonal = Math.abs(toTile.x - fromTile.x) === 1 && Math.abs(toTile.y - fromTile.y) === 1;
  if (diagonal) {
    const orthogonalX = canEnter(store, fromX, fromY, toTile.x, fromTile.y, h);
    const orthogonalY = canEnter(store, fromX, fromY, fromTile.x, toTile.y, h);
    if (!orthogonalX || !orthogonalY) return false;
  }
  const targetH = store.standingH(toTile.x, toTile.y, h);
  if (targetH === undefined || Math.abs(targetH - h) > MAX_STEP_H) return false;
  if (diagonal) return true;
  return !wallBetween(store, fromTile.x, fromTile.y, toTile.x, toTile.y, h);
}

function levelBlocks(
  wall: number,
  flags: number,
  doorwayFlag: number,
  windowFlag: number,
): boolean {
  if (!wall) return false;
  if (flags & doorwayFlag) return false;
  return true; // windows block bodies
}

/** Edge walls block bodies unless the edge is a doorway; windows always block. */
export function wallBetween(
  store: ChunkStore,
  x1: number,
  y1: number,
  x2: number,
  y2: number,
  h: number,
): boolean {
  const dx = x2 - x1;
  const dy = y2 - y1;
  if (dy === -1) return wallOnEdge(store, x1, y1, "n", h);
  if (dy === 1) return wallOnEdge(store, x2, y2, "n", h);
  if (dx === -1) return wallOnEdge(store, x1, y1, "w", h);
  if (dx === 1) return wallOnEdge(store, x2, y2, "w", h);
  return false;
}

function wallOnEdge(store: ChunkStore, x: number, y: number, edge: "n" | "w", h: number): boolean {
  const zMin = Math.floor(h / LEVEL_H) - 1;
  const zMax = Math.floor((h + HEADROOM_H) / LEVEL_H) + 1;
  for (let z = zMin; z <= zMax; z += 1) {
    const cell = store.levelCell(x, y, z);
    if (!cell) continue;
    const wall = edge === "n" ? cell.wall_n : cell.wall_w;
    if (!wall || cell.floor_h === -32768 || (cell.flags & LEVEL_VOID) !== 0) continue;
    const bottom = cell.floor_h;
    if (!(bottom < h + HEADROOM_H && bottom + LEVEL_H > h)) continue;
    const doorway = edge === "n" ? EDGE_N_DOORWAY : EDGE_W_DOORWAY;
    if (levelBlocks(wall, cell.edge_flags, doorway, edge === "n" ? EDGE_N_WINDOW : EDGE_W_WINDOW)) {
      return true;
    }
  }
  return false;
}
