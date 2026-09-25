import type { ChunkStore } from "./ChunkStore";
import { NO_FLOOR } from "./rules";
import { marchRay, type CutoffAt, type RayHit } from "./ray";

const PROBE_HEIGHTS = [1, 2.5, 3.5] as const;
const BODY_HALF_S = 9 / 32; // Half the 18 px Niko body, converted to ray units.
const MAX_STRUCTURE_TILES = 4096;

export type StructureBounds = { minX: number; minY: number; maxX: number; maxY: number };
export type Structure = {
  tiles: Set<string>;
  cutoff: number;
  bounds: StructureBounds;
  seed: { x: number; y: number };
  anchor: { x: number; y: number };
};

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

  const eligible = floors.some((floorH) => floorH <= viewerH + 4);
  const lowest = floors.length > 0 ? Math.min(...floors) : viewerH;
  return {
    tiles,
    cutoff: eligible ? viewerH + 4 : lowest + 4,
    bounds: { minX, minY, maxX: maxX + 1, maxY: maxY + 1 },
    seed: { x: seedX, y: seedY },
    anchor: { x: seedX, y: seedY },
  };
}

export function bodyHits(
  store: ChunkStore,
  x: number,
  y: number,
  viewerH: number,
  cutoffAt: CutoffAt,
): RayHit[] {
  const hits: RayHit[] = [];
  for (const probeHeight of PROBE_HEIGHTS) {
    for (const ds of [-BODY_HALF_S, 0, BODY_HALF_S]) {
      const hit = marchRay(
        store,
        x - y + ds,
        x + y - (viewerH + probeHeight),
        viewerH + 40,
        viewerH + probeHeight + 0.5,
        cutoffAt,
      );
      if (hit) hits.push(hit);
    }
  }
  return hits;
}

export function occludingStructures(
  store: ChunkStore,
  x: number,
  y: number,
  viewerH: number,
  cutoff: number,
): Structure[] {
  const tileX = Math.floor(x);
  const tileY = Math.floor(y);
  const structures: Structure[] = [];
  const visited = new Set<string>();
  const cutoffAt: CutoffAt = () => cutoff;

  for (const hit of bodyHits(store, x, y, viewerH, cutoffAt)) {
    const key = tileKey(hit.x, hit.y);
    if (hit.kind !== "floor" || visited.has(key)) continue;
    const structure = structureFrom(store, hit.x, hit.y, viewerH);
    for (const tile of structure.tiles) visited.add(tile);
    if (structure.tiles.size > 0) {
      structure.anchor = { x: tileX, y: tileY };
      structures.push(structure);
    }
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
  return bodyHits(store, x, y, viewerH, cutoffAt).length > 0;
}

function structureSignature(structure: Structure): string {
  return `${[...structure.tiles].sort().join(";")}|${structure.cutoff}`;
}

function changedBounds(previous: Structure[], current: Structure[]): StructureBounds[] {
  const previousBySeed = new Map(previous.map((structure) => [tileKey(structure.seed.x, structure.seed.y), structure]));
  const currentBySeed = new Map(current.map((structure) => [tileKey(structure.seed.x, structure.seed.y), structure]));
  const seeds = new Set([...previousBySeed.keys(), ...currentBySeed.keys()]);
  const bounds: StructureBounds[] = [];

  for (const seed of seeds) {
    const before = previousBySeed.get(seed);
    const after = currentBySeed.get(seed);
    if (before && after && structureSignature(before) === structureSignature(after)) continue;
    if (before) bounds.push(before.bounds);
    if (after) bounds.push(after.bounds);
  }

  return bounds;
}

export class StructureTracker {
  private structures: Structure[] = [];
  private viewerH: number | null = null;
  private cutoff: number | null = null;

  update(store: ChunkStore, x: number, y: number, viewerH: number, cutoff: number): StructureBounds[] {
    const previous = this.structures;
    const heightChanged = this.viewerH !== null && this.viewerH !== viewerH;
    if (this.viewerH !== viewerH || this.cutoff !== cutoff) this.rebuildKept(store, viewerH);
    this.viewerH = viewerH;
    this.cutoff = cutoff;

    const tileX = Math.floor(x);
    const tileY = Math.floor(y);
    const hits = new Set<Structure>();
    const visited = new Set<string>();
    for (const hit of bodyHits(store, x, y, viewerH, () => cutoff)) {
      if (hit.kind !== "floor") continue;
      const key = tileKey(hit.x, hit.y);
      const kept = this.structures.find((structure) => structure.tiles.has(key));
      if (kept) {
        kept.anchor = { x: tileX, y: tileY };
        hits.add(kept);
        continue;
      }
      if (visited.has(key)) continue;

      const structure = structureFrom(store, hit.x, hit.y, viewerH);
      if (structure.tiles.size === 0) continue;
      for (const tile of structure.tiles) visited.add(tile);
      structure.anchor = { x: tileX, y: tileY };

      const merged = this.structures.filter((current) => [...current.tiles].some((tile) => structure.tiles.has(tile)));
      if (merged.length > 0) {
        structure.anchor = merged[0]!.anchor;
        this.structures = this.structures.filter((current) => !merged.includes(current));
      }
      this.structures.push(structure);
      hits.add(structure);
    }

    this.structures = this.structures.filter((structure) => {
      const released = Math.abs(structure.anchor.x - tileX) > 1 || Math.abs(structure.anchor.y - tileY) > 1;
      return !released && !(heightChanged && !hits.has(structure));
    });

    return changedBounds(previous, this.structures);
  }

  rebuild(store: ChunkStore): StructureBounds[] {
    const previous = this.structures;
    if (this.viewerH !== null) this.rebuildKept(store, this.viewerH);
    return changedBounds(previous, this.structures);
  }

  cutoffAt(x: number, y: number, globalCutoff: number): number {
    const key = tileKey(Math.floor(x), Math.floor(y));
    return Math.min(globalCutoff, this.structures.find((structure) => structure.tiles.has(key))?.cutoff ?? Infinity);
  }

  touches(cx: number, cy: number, size: number): boolean {
    const minX = cx * size;
    const minY = cy * size;
    const maxX = minX + size;
    const maxY = minY + size;
    return this.structures.some(({ bounds }) =>
      minX < bounds.maxX && maxX > bounds.minX && minY < bounds.maxY && maxY > bounds.minY,
    );
  }

  clear(): void {
    this.structures = [];
    this.viewerH = null;
    this.cutoff = null;
  }

  private rebuildKept(store: ChunkStore, viewerH: number): void {
    const rebuiltStructures: Structure[] = [];
    for (const previous of this.structures) {
      if (!hasStructureCell(store, previous.seed.x, previous.seed.y)) continue;
      const rebuilt = structureFrom(store, previous.seed.x, previous.seed.y, viewerH);
      if (rebuilt.tiles.size === 0) continue;
      const joined = rebuiltStructures.find((structure) => structure.tiles.has(tileKey(previous.seed.x, previous.seed.y)));
      if (joined) continue;
      rebuilt.anchor = previous.anchor;
      rebuiltStructures.push(rebuilt);
    }
    this.structures = rebuiltStructures;
  }
}
