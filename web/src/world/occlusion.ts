import type { ChunkStore } from "./ChunkStore";
import { NO_FLOOR } from "./rules";
import { marchRay, type CutoffAt } from "./ray";

const PROBE_HEIGHTS = [1, 2.5, 3.5] as const;
const MAX_STRUCTURE_TILES = 4096;

export type StructureBounds = { minX: number; minY: number; maxX: number; maxY: number };
export type Structure = { tiles: Set<string>; cutoff: number; bounds: StructureBounds };

function tileKey(x: number, y: number): string {
  return `${x},${y}`;
}

function hasStructureCell(store: ChunkStore, x: number, y: number): boolean {
  return store.levelsAt(x, y).some((level) => {
    const cell = store.levelCell(x, y, level.z);
    return cell !== null && (cell.floor_h !== NO_FLOOR || cell.wall_n !== 0 || cell.wall_w !== 0);
  });
}

function structureFrom(store: ChunkStore, seedX: number, seedY: number, viewerH: number): Structure {
  const tiles = new Set<string>();
  const queue: Array<[number, number]> = [[seedX, seedY]];
  let head = 0;
  let minX = seedX;
  let minY = seedY;
  let maxX = seedX;
  let maxY = seedY;
  const floors: number[] = [];

  while (head < queue.length && tiles.size < MAX_STRUCTURE_TILES) {
    const [x, y] = queue[head++]!;
    const key = tileKey(x, y);
    if (tiles.has(key) || !hasStructureCell(store, x, y)) continue;
    tiles.add(key);
    minX = Math.min(minX, x);
    minY = Math.min(minY, y);
    maxX = Math.max(maxX, x);
    maxY = Math.max(maxY, y);

    for (const level of store.levelsAt(x, y)) {
      const cell = store.levelCell(x, y, level.z);
      if (cell && cell.floor_h !== NO_FLOOR) floors.push(cell.floor_h);
    }
    for (const [dx, dy] of [[-1, 0], [1, 0], [0, -1], [0, 1]]) {
      const nextX = x + dx;
      const nextY = y + dy;
      const nextKey = tileKey(nextX, nextY);
      if (!tiles.has(nextKey) && hasStructureCell(store, nextX, nextY)) queue.push([nextX, nextY]);
    }
  }

  const eligibleFloors = floors.filter((floorH) => floorH <= viewerH + 4);
  const floorH = eligibleFloors.length > 0
    ? Math.max(...eligibleFloors)
    : floors.length > 0 ? Math.min(...floors) : viewerH;
  return {
    tiles,
    cutoff: Math.max(viewerH, floorH) + 4,
    bounds: { minX, minY, maxX: maxX + 1, maxY: maxY + 1 },
  };
}

export function occludingStructures(
  store: ChunkStore,
  nx: number,
  ny: number,
  viewerH: number,
  cutoff: number,
): Structure[] {
  const tileX = Math.floor(nx);
  const tileY = Math.floor(ny);
  const s = tileX - tileY;
  const t = tileX + tileY + 1 - viewerH;
  const structures: Structure[] = [];
  const visited = new Set<string>();
  const cutoffAt: CutoffAt = () => cutoff;

  for (const probeHeight of PROBE_HEIGHTS) {
    const hit = marchRay(store, s, t, viewerH + 40, viewerH + probeHeight + 0.5, cutoffAt);
    if (!hit || hit.kind !== "floor" || visited.has(tileKey(hit.x, hit.y))) continue;
    const structure = structureFrom(store, hit.x, hit.y, viewerH);
    for (const key of structure.tiles) visited.add(key);
    if (structure.tiles.size > 0) structures.push(structure);
  }

  return structures;
}

export function isCovered(
  store: ChunkStore,
  x: number,
  y: number,
  viewerH: number,
  cutoffAt: CutoffAt,
): boolean {
  const s = x - y;
  const t = x + y - viewerH;
  return PROBE_HEIGHTS.some((probeHeight) =>
    marchRay(store, s, t, viewerH + 40, viewerH + probeHeight + 0.5, cutoffAt) !== null,
  );
}
