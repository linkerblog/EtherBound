import type { components } from "../net/schema";
import { EDGE_N_DOORWAY, EDGE_W_DOORWAY, NO_FLOOR } from "../world/rules";
import { wallEndRuns, wallJoints, type WallEdge, type WallJoint } from "./wallJoints";
import { scaleColor, sideVariant } from "./terrainSprites";
import type { TerrainAtlas } from "./terrainAtlas";
import type { ChunkLayers } from "./chunkRenderer";
import type { ChunkStore } from "../world/ChunkStore";
import { isFrontWall as frontWallCutaway, MAX_WALL_H } from "../world/cutaway";

type Material = components["schemas"]["MaterialResponse"];

export { MAX_WALL_H } from "../world/cutaway";

type WallPainterView = {
  readonly viewerH: number;
  readonly nikoTile: string;
  readonly materials: Map<number, Material> | null;
  cutoffAt(x: number, y: number): number;
};

type WallPaint = {
  drawMask: (
    layers: ChunkLayers,
    usedRows: Set<number>,
    frame: string,
    x: number,
    y: number,
    h: number,
    row: number,
    tint: number,
    alpha?: number,
  ) => void;
  materialColor: (id: number) => number;
};

export class WallPainter {
  constructor(
    private readonly chunks: ChunkStore,
    private readonly atlas: TerrainAtlas,
    private readonly view: WallPainterView,
    private readonly paint: WallPaint,
  ) {}

  isFrontWall(edge: "n" | "w", x: number, y: number, base: number): boolean {
    const [nx, ny] = this.view.nikoTile.split(",").map(Number);
    return frontWallCutaway(this.chunks, edge, x, y, base, { x: nx!, y: ny! }, this.view.viewerH);
  }

  drawWall(
    edge: "n" | "w",
    layers: ChunkLayers,
    usedRows: Set<number>,
    x: number,
    y: number,
    z: number,
    base: number,
    materialId: number,
    cutoff: number,
    window: boolean,
    stub: boolean,
    row: number,
  ): void {
    const color = this.paint.materialColor(materialId);
    const light = edge === "n" ? 0.82 : 0.66;
    const key = this.view.materials?.get(materialId)?.key;
    const textured = key !== undefined && this.atlas.wallKeys.has(key);
    const cap = Math.min(base + MAX_WALL_H, cutoff);
    const top = stub ? Math.min(base + 1, cap) : cap;
    if (top <= base) return;
    const run = (bottom: number, runTop: number, tint: number): void => {
      if (textured && key !== undefined) this.drawWallUnits(edge, layers, usedRows, x, y, bottom, runTop, base, stub, key, light, row);
      else this.drawWallRun((units) => `wall${edge.toUpperCase()}${units}`, layers, usedRows, x, y, bottom, runTop, row, tint);
    };
    if (stub || !window) {
      run(base, top, scaleColor(color, light));
    } else {
      run(base, Math.min(base + 2, top), scaleColor(color, light));
      if (top > base + 4) run(base + 4, top, scaleColor(color, light));
      if (top > base + 2) this.drawWallRun((units) => `wall${edge.toUpperCase()}${units}`, layers, usedRows, x, y, base + 2, Math.min(base + 4, top), row, scaleColor(0xd8e6f0, light), 0.45);
    }
    const joints = wallJoints(edge, x, y, z, this.jointLookup);
    if (joints.end) {
      const endTint = scaleColor(color, edge === "n" ? 0.66 : 0.82);
      const frame = (units: 1 | 2 | 4): string => `wallEnd${edge === "n" ? "E" : "S"}${units}`;
      for (const end of wallEndRuns(joints.end, base, window)) {
        this.drawWallRun(frame, layers, usedRows, x, y, end.from, end.to, row, endTint);
      }
    }
    const stripTint = scaleColor(color, 1.12);
    this.paint.drawMask(layers, usedRows, `wallTop${edge.toUpperCase()}`, x, y, top, row, stripTint);
    if (joints.post) this.paint.drawMask(layers, usedRows, "wallPost", x, y, top, row, stripTint);
  }

  private drawnWallTop(edge: "n" | "w", x: number, y: number, z: number): number {
    const floorH = this.chunks.levelCell(x, y, z)?.floor_h ?? NO_FLOOR;
    const base = this.chunks.wallBaseH(x, y, z, floorH);
    const cap = Math.min(base + MAX_WALL_H, this.view.cutoffAt(x, y));
    return this.isFrontWall(edge, x, y, base) ? Math.min(base + 1, cap) : cap;
  }

  private wallJoint(edge: WallEdge, x: number, y: number, z: number): WallJoint {
    const cell = this.chunks.levelCell(x, y, z);
    const base = this.chunks.wallBaseH(x, y, z, cell?.floor_h ?? NO_FLOOR);
    if (!cell) return { shown: false, base, top: base };
    const doorway = edge === "n" ? EDGE_N_DOORWAY : EDGE_W_DOORWAY;
    const wall = edge === "n" ? cell.wall_n : cell.wall_w;
    const shown = wall !== 0 && (cell.edge_flags & doorway) === 0;
    return { shown, base, top: shown ? this.drawnWallTop(edge, x, y, z) : base };
  }

  private readonly jointLookup = (edge: WallEdge, x: number, y: number, z: number): WallJoint =>
    this.wallJoint(edge, x, y, z);

  private drawWallUnits(
    edge: "n" | "w",
    layers: ChunkLayers,
    usedRows: Set<number>,
    x: number,
    y: number,
    bottom: number,
    top: number,
    base: number,
    stub: boolean,
    key: string,
    light: number,
    row: number,
  ): void {
    const tint = scaleColor(0xffffff, light);
    for (let unitTop = bottom + 1; unitTop <= top; unitTop += 1) {
      const part = !stub && unitTop === base + MAX_WALL_H ? "cap" : "fill";
      const frame = `wall:${key}:${edge}:${part}:${sideVariant(x, y, edge, unitTop)}`;
      this.paint.drawMask(layers, usedRows, frame, x, y, unitTop, row, tint);
    }
  }

  private drawWallRun(
    frame: (units: 1 | 2 | 4) => string,
    layers: ChunkLayers,
    usedRows: Set<number>,
    x: number,
    y: number,
    bottom: number,
    top: number,
    row: number,
    tint: number,
    alpha = 1,
  ): void {
    let current = bottom;
    while (current < top) {
      const units = ([4, 2, 1] as const).find((size) => size <= top - current)!;
      this.paint.drawMask(layers, usedRows, frame(units), x, y, current + units, row, tint, alpha);
      current += units;
    }
  }
}
