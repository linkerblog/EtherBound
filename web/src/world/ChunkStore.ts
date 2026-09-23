import type { components } from "../net/schema";
import { LEVEL_H, LEVEL_VOID, NO_FLOOR } from "./rules";
type ChunkLevel = components["schemas"]["ChunkLevelResponse"];
type WorldChunk = components["schemas"]["ChunkResponse"];

export type LevelCell = {
  z: number;
  floor_h: number;
  floor_mat: number;
  wall_n: number;
  wall_w: number;
  edge_flags: number;
  flags: number;
};

export type WorldBounds = { min_x: number; min_y: number; max_x: number; max_y: number };

const DEFAULT_CHUNK_SIZE = 32;

export class ChunkStore {
  private readonly chunks = new Map<string, WorldChunk>();
  private chunkSize = DEFAULT_CHUNK_SIZE;
  private boundsValue: WorldBounds | null = null;
  private materials = new Map<number, { walkable: boolean; walk_cost: number; solid: boolean }>();

  setMaterials(materials: Map<number, { walkable: boolean; walk_cost: number; solid: boolean }>): void {
    this.materials = materials;
  }

  setWorldInfo(chunkSize: number, bounds: number[]): void {
    if (chunkSize > 0) this.chunkSize = chunkSize;
    if (bounds.length === 4) {
      this.boundsValue = {
        min_x: bounds[0],
        min_y: bounds[1],
        max_x: bounds[2],
        max_y: bounds[3],
      };
    }
  }

  clear(): void {
    this.chunks.clear();
  }

  get size(): number {
    return this.chunkSize;
  }

  get bounds(): WorldBounds | null {
    return this.boundsValue;
  }

  set(chunk: WorldChunk): boolean {
    const key = this.key(chunk.cx, chunk.cy);
    const previous = this.chunks.get(key);
    if (previous && previous.revision >= chunk.revision) return false;
    this.chunks.set(key, chunk);
    return true;
  }

  get(cx: number, cy: number): WorldChunk | undefined {
    return this.chunks.get(this.key(cx, cy));
  }

  values(): Iterable<WorldChunk> {
    return this.chunks.values();
  }

  hasLevels(): boolean {
    return [...this.chunks.values()].some((chunk) => chunk.levels.length > 0);
  }

  private cellIndex(x: number, y: number): { chunk: WorldChunk; index: number } | null {
    const size = this.chunkSize;
    const cx = Math.floor(x / size);
    const cy = Math.floor(y / size);
    const chunk = this.get(cx, cy);
    if (!chunk) return null;
    const localX = Math.floor(x) - cx * size;
    const localY = Math.floor(y) - cy * size;
    return { chunk, index: localY * size + localX };
  }

  groundH(x: number, y: number): number | undefined {
    const cell = this.cellIndex(x, y);
    return cell ? cell.chunk.ground_h[cell.index] : undefined;
  }

  surfaceMat(x: number, y: number): number | undefined {
    const cell = this.cellIndex(x, y);
    return cell ? cell.chunk.surface_mat[cell.index] : undefined;
  }

  isWalkable(materialId: number | undefined): boolean {
    return materialId !== undefined && (this.materials.get(materialId)?.walkable ?? false);
  }

  /** Movement cost of the standing surface at a body's current height. */
  walkCost(x: number, y: number, h: number): number {
    const cell = this.cellIndex(x, y);
    if (!cell) return 1;
    const level = cell.chunk.levels.find((entry) => {
      const index = cell.index;
      return entry.floor_h[index] === h;
    });
    const materialId = level ? level.floor_mat[cell.index] :
      cell.chunk.ground_h[cell.index] === h ? cell.chunk.surface_mat[cell.index] : undefined;
    const cost = materialId === undefined ? undefined : this.materials.get(materialId)?.walk_cost;
    return cost !== undefined && cost > 0 ? cost : 1;
  }

  /** The standing surface of a tile closest to `h`: a stored floor, else the ground. */
  standingH(x: number, y: number, h: number): number | undefined {
    const ground = this.groundH(x, y);
    let best: number | undefined;
    const consider = (candidate: number): void => {
      if (Math.abs(candidate - h) > 1) return;
      if (best === undefined || Math.abs(candidate - h) < Math.abs(best - h)) best = candidate;
    };
    for (const level of this.levelsAt(x, y)) {
      const cell = this.levelCell(x, y, level.z);
      if (cell && cell.floor_h !== NO_FLOOR && this.isWalkable(cell.floor_mat) && this.hasHeadroom(x, y, cell.floor_h)) {
        consider(cell.floor_h);
      }
    }
    if (ground !== undefined && !this.isVoid(x, y, ground) && this.hasHeadroom(x, y, ground)) {
      const material = this.surfaceMat(x, y);
      if (material !== undefined && this.isWalkable(material)) consider(ground);
    }
    return best;
  }

  isVoid(x: number, y: number, h: number): boolean {
    const cell = this.levelCell(x, y, Math.floor(h / 6));
    return cell !== null && (cell.flags & LEVEL_VOID) !== 0;
  }

  /**
   * The top of the solid volume in a column: the ground, or the bottom of the contiguous VOID
   * bands under it. Undefined for a tile the store has not loaded.
   */
  solidTopH(x: number, y: number): number | undefined {
    const ground = this.groundH(x, y);
    if (ground === undefined) return undefined;
    let z = Math.floor(ground / LEVEL_H);
    if (!this.isVoid(x, y, z * LEVEL_H)) return ground;
    while (this.isVoid(x, y, (z - 1) * LEVEL_H)) z -= 1;
    return z * LEVEL_H;
  }

  private hasHeadroom(x: number, y: number, h: number): boolean {
    for (let offset = 1; offset <= 3; offset += 1) {
      if (this.isSolid(x, y, h + offset)) return false;
    }
    return true;
  }

  private isSolid(x: number, y: number, h: number): boolean {
    const tile = this.cellIndex(x, y);
    if (!tile) return true;
    const slab = tile.chunk.levels.find((level) => level.floor_h[tile.index] === h);
    if (slab) return this.materials.get(slab.floor_mat[tile.index])?.solid ?? true;
    if (h >= tile.chunk.ground_h[tile.index] || this.isVoid(x, y, h)) return false;
    // Strata are not streamed to the client; below-ground volumes are conservatively solid.
    return true;
  }

  levelsAt(x: number, y: number): ChunkLevel[] {
    const cell = this.cellIndex(x, y);
    if (!cell) return [];
    return cell.chunk.levels;
  }

  wallBaseH(x: number, y: number, z: number, floorH: number): number {
    if (floorH !== NO_FLOOR) return floorH;
    const limit = (z + 1) * LEVEL_H;
    const supports = [this.groundH(x, y), ...this.levelsAt(x, y)
      .filter((level) => level.z < z)
      .map((level) => this.levelCell(x, y, level.z)?.floor_h)
      .filter((height): height is number => height !== undefined && height !== NO_FLOOR)];
    const belowLevel = supports.filter((height): height is number => height !== undefined && height < limit);
    return belowLevel.length > 0 ? Math.max(...belowLevel) : z * LEVEL_H;
  }

  /** One tile's view into a level band, or null when the band has no data. */
  levelCell(x: number, y: number, z: number): LevelCell | null {
    const cell = this.cellIndex(x, y);
    if (!cell) return null;
    const level = cell.chunk.levels.find((entry) => entry.z === z);
    if (!level) return null;
    const index = cell.index;
    return {
      z,
      floor_h: level.floor_h[index],
      floor_mat: level.floor_mat[index],
      wall_n: level.wall_n[index],
      wall_w: level.wall_w[index],
      edge_flags: level.edge_flags[index],
      flags: level.flags[index],
    };
  }

  private key(cx: number, cy: number): string {
    return `${cx},${cy}`;
  }
}
