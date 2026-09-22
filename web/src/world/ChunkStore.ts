import type { components } from "../net/schema";
import { LEVEL_VOID } from "./rules";
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
  private materials = new Map<number, { walkable: boolean }>();

  setMaterials(materials: Map<number, { walkable: boolean }>): void {
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
      if (cell && cell.floor_h !== -32768 && (cell.flags & LEVEL_VOID) === 0) consider(cell.floor_h);
    }
    if (ground !== undefined && !this.isVoid(x, y, ground)) {
      const material = this.surfaceMat(x, y);
      if (material !== undefined && this.isWalkable(material)) consider(ground);
    }
    return best;
  }

  private isVoid(x: number, y: number, h: number): boolean {
    const cell = this.levelCell(x, y, Math.floor(h / 6));
    return cell !== null && (cell.flags & 1) !== 0;
  }

  levelsAt(x: number, y: number): ChunkLevel[] {
    const cell = this.cellIndex(x, y);
    if (!cell) return [];
    return cell.chunk.levels;
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

  /** Topmost visible standing z at a tile, for the right-click menu. */
  topmostZ(x: number, y: number, viewerH: number): number {
    const tileX = Math.floor(x);
    const tileY = Math.floor(y);
    const candidates: number[] = [];
    for (const level of this.levelsAt(tileX, tileY)) {
      const cell = this.levelCell(tileX, tileY, level.z);
      if (cell) candidates.push(cell.floor_h);
    }
    const ground = this.groundH(tileX, tileY);
    if (ground !== undefined) candidates.push(ground);
    const visible = candidates.filter((h) => h <= viewerH + 4);
    const h = visible.length > 0 ? Math.max(...visible) : (candidates[0] ?? 0);
    return Math.floor(h / 6);
  }

  private key(cx: number, cy: number): string {
    return `${cx},${cy}`;
  }
}
