import Phaser from "phaser";
import type { components } from "../net/schema";
import { ChunkStore } from "../world/ChunkStore";
import { EDGE_N_DOORWAY, EDGE_N_WINDOW, EDGE_W_DOORWAY, EDGE_W_WINDOW, LEVEL_H, NO_FLOOR } from "../world/rules";
import { TILE_H, TILE_W, baseDepth, faceTile, rowDepth, toScreen } from "./iso";
import { scaleColor, shadeColor, sideTint, sideVariant, spriteTint } from "./terrainSprites";
import type { ObjectFrame, TerrainAtlas } from "./terrainAtlas";
import type { DirtyChunk } from "./dirty";
import { MAX_WALL_H, WallPainter } from "./wallPainter";

type Material = components["schemas"]["MaterialResponse"];
type ObjectKind = components["schemas"]["ObjectKindResponse"];
type TileObject = components["schemas"]["ObjectResponse"];
type WorldChunk = components["schemas"]["ChunkResponse"];
export type ChunkLayers = {
  base: Phaser.GameObjects.Blitter;
  rows: Map<number, Phaser.GameObjects.Blitter>;
  bbox: { minX: number; minY: number; maxX: number; maxY: number };
  hMin: number;
  hMax: number;
  visible: boolean;
};

type ChunkRendererView = {
  readonly viewerH: number;
  readonly nikoTile: string;
  readonly animatedObjectIds: Set<number>;
  readonly materials: Map<number, Material> | null;
  readonly objectKinds: Map<string, ObjectKind>;
  cutoffAt(x: number, y: number): number;
};

export class ChunkRenderer {
  private readonly chunkLayers = new Map<string, ChunkLayers>();
  private readonly walls: WallPainter;

  constructor(
    private readonly scene: Phaser.Scene,
    private readonly chunks: ChunkStore,
    private readonly atlas: TerrainAtlas,
    private readonly view: ChunkRendererView,
  ) {
    this.walls = new WallPainter(chunks, atlas, view, {
      drawMask: (layers, usedRows, frame, x, y, h, row, tint, alpha) =>
        this.drawMask(layers, usedRows, frame, x, y, h, row, tint, alpha),
      materialColor: (id) => this.materialColor(id),
    });
  }

  layersFor(chunk: WorldChunk): ChunkLayers {
    const key = `${chunk.cx},${chunk.cy}`;
    const existing = this.chunkLayers.get(key);
    if (existing) return existing;
    const base = this.scene.add.blitter(0, 0, "terrain");
    base.setDepth(baseDepth(chunk.cx, chunk.cy));
    const { bbox, hMin, hMax } = this.chunkBounds(chunk);
    const layers: ChunkLayers = {
      base,
      rows: new Map(),
      bbox,
      hMin,
      hMax,
      visible: true,
    };
    this.chunkLayers.set(key, layers);
    return layers;
  }

  clearChunkLayers(): void {
    for (const layers of this.chunkLayers.values()) {
      layers.base.destroy();
      for (const row of layers.rows.values()) row.destroy();
    }
    this.chunkLayers.clear();
  }

  drawChunk(chunk: WorldChunk): void {
    const size = this.chunks.size;
    const layers = this.layersFor(chunk);
    const orderedLevels = [...chunk.levels].sort((a, b) => a.z - b.z);
    layers.base.clear();
    for (const row of layers.rows.values()) row.clear();
    const usedRows = new Set<number>();

    for (let band = 0; band <= 2 * size - 2; band += 1) {
      const minX = Math.max(0, band - size + 1);
      const maxX = Math.min(band, size - 1);
      for (let lx = minX; lx <= maxX; lx += 1) {
        const ly = band - lx;
        const index = ly * size + lx;
        const x = chunk.cx * size + lx;
        const y = chunk.cy * size + ly;
        const row = x + y;
        const tileCutoff = this.view.cutoffAt(x, y);
        const h = chunk.ground_h[index] ?? 0;
        const materialId = chunk.surface_mat[index] ?? 0;
        const fillOnly = this.chunks.isVoid(x, y, h);
        const faceTop = this.chunks.solidTopH(x, y);
        const hasFloor = chunk.levels.some((level) => level.floor_h[index] !== NO_FLOOR);
        const groundShown = (!hasFloor || h <= tileCutoff) && !fillOnly;
        if (groundShown) this.drawTop(layers, usedRows, x, y, h, materialId, row);
        const faceVisible = fillOnly ? faceTop !== undefined && faceTop <= tileCutoff : groundShown;
        if (faceVisible && faceTop !== undefined) {
          const eastTop = this.chunks.solidTopH(x + 1.5, y + 0.5);
          const southTop = this.chunks.solidTopH(x + 0.5, y + 1.5);
          if (eastTop === undefined || faceTop > eastTop) {
            this.drawVertical("e", layers, usedRows, x, y, eastTop ?? faceTop - 4, faceTop, materialId, 0.66, row, 1, fillOnly);
          }
          if (southTop === undefined || faceTop > southTop) {
            this.drawVertical("s", layers, usedRows, x, y, southTop ?? faceTop - 4, faceTop, materialId, 0.82, row, 1, fillOnly);
          }
        }

        for (const level of orderedLevels) {
          const floorH = level.floor_h[index];
          if (floorH !== NO_FLOOR && floorH <= tileCutoff) {
            const floorMaterial = level.floor_mat[index] ?? 0;
            this.drawSurface(layers, usedRows, x, y, floorH, floorMaterial, row);
            const southCell = this.chunks.levelCell(x, y + 1, level.z);
            const eastCell = this.chunks.levelCell(x + 1, y, level.z);
            if (southCell?.floor_h !== floorH) {
              this.drawVertical("s", layers, usedRows, x, y, floorH - 1, floorH, floorMaterial, 0.82, row);
            }
            if (eastCell?.floor_h !== floorH) {
              this.drawVertical("e", layers, usedRows, x, y, floorH - 1, floorH, floorMaterial, 0.66, row);
            }
          }

          const wallBase = this.chunks.wallBaseH(x, y, level.z, floorH);
          const wallN = level.wall_n[index];
          const wallW = level.wall_w[index];
          const edgeFlags = level.edge_flags[index];
          if (wallBase <= tileCutoff) {
            if (wallN && (edgeFlags & EDGE_N_DOORWAY) === 0) {
              const stub = this.walls.isFrontWall("n", x, y, wallBase);
              this.walls.drawWall("n", layers, usedRows, x, y, level.z, wallBase, wallN, tileCutoff, (edgeFlags & EDGE_N_WINDOW) !== 0, stub, row);
            }
            if (wallW && (edgeFlags & EDGE_W_DOORWAY) === 0) {
              const stub = this.walls.isFrontWall("w", x, y, wallBase);
              this.walls.drawWall("w", layers, usedRows, x, y, level.z, wallBase, wallW, tileCutoff, (edgeFlags & EDGE_W_WINDOW) !== 0, stub, row);
            }
          }
        }
        this.drawTileObjects(layers, usedRows, chunk.objects ?? [], x, y, tileCutoff, row);
      }
    }

    for (const [row, graphics] of layers.rows) {
      if (!usedRows.has(row)) {
        graphics.destroy();
        layers.rows.delete(row);
      }
    }
  }

  updateChunkBounds(chunk: WorldChunk): void {
    const layers = this.chunkLayers.get(`${chunk.cx},${chunk.cy}`);
    if (!layers) return;
    const bounds = this.chunkBounds(chunk);
    layers.bbox = bounds.bbox;
    layers.hMin = bounds.hMin;
    layers.hMax = bounds.hMax;
  }
  hasLayers(key: string) { return this.chunkLayers.has(key); }

  boundsOf(key: string): ChunkLayers["bbox"] | undefined { return this.chunkLayers.get(key)?.bbox; }

  cull(isVisible: (bbox: ChunkLayers["bbox"]) => boolean): void {
    for (const layers of this.chunkLayers.values()) {
      const visible = isVisible(layers.bbox);
      if (visible === layers.visible) continue;
      layers.visible = visible;
      layers.base.setVisible(visible);
      for (const row of layers.rows.values()) row.setVisible(visible);
    }
  }

  dirtyInfo(hasLevels: (cx: number, cy: number) => boolean): DirtyChunk[] {
    return [...this.chunkLayers].map(([key, layers]) => {
      const [cx, cy] = key.split(",").map(Number);
      return { key, cx, cy, hMin: layers.hMin, hMax: layers.hMax, hasLevels: hasLevels(cx!, cy!) }; });
  }

  private materialColor(id: number): number {
    const raw = this.view.materials?.get(id)?.color;
    if (!raw) return id === 0 ? 0x426b4d : 0x5a7450;
    return parseInt(raw.slice(1), 16);
  }

  private shade(base: number, h: number): number {
    return shadeColor(base, h);
  }

  private targetBlitter(layers: ChunkLayers, usedRows: Set<number>, row: number): Phaser.GameObjects.Blitter {
    usedRows.add(row);
    let blitter = layers.rows.get(row);
    if (!blitter) {
      blitter = this.scene.add.blitter(0, 0, "terrain");
      blitter.setDepth(rowDepth(row)).setVisible(layers.visible);
      layers.rows.set(row, blitter);
    }
    return blitter;
  }

  private blitterFor(layers: ChunkLayers, usedRows: Set<number>, row: number, h: number): Phaser.GameObjects.Blitter {
    return h <= this.view.viewerH ? layers.base : this.targetBlitter(layers, usedRows, row);
  }

  private drawTop(layers: ChunkLayers, usedRows: Set<number>, x: number, y: number, h: number, materialId: number, row: number): void {
    this.drawSurface(layers, usedRows, x, y, h, materialId, row);
  }

  private drawSurface(layers: ChunkLayers, usedRows: Set<number>, x: number, y: number, h: number, materialId: number, row: number): void {
    const materialKey = this.view.materials?.get(materialId)?.key;
    const color = this.materialColor(materialId);
    const textured = materialKey !== undefined && this.atlas.spriteKeys.has(materialKey);
    const frame = textured ? `${materialKey}${this.variant(x, y)}` : "top";
    this.drawMask(layers, usedRows, frame, x, y, h, row, textured ? spriteTint(color, h) : this.shade(color, h));
  }

  private drawTileObjects(layers: ChunkLayers, usedRows: Set<number>, objects: TileObject[], x: number, y: number, cutoff: number, row: number): void {
    const tileObjects = objects
      .filter((object) => object.x === x && object.y === y && !this.view.animatedObjectIds.has(object.id))
      .sort((a, b) => a.id - b.id);
    const pileHeights = new Map<number, TileObject>();
    for (const object of tileObjects) {
      const kind = this.view.objectKinds.get(object.kind);
      if (!kind || !this.restingSurfaceDrawn(x, y, object.h, object.id, cutoff)) continue;
      if (!kind.solid) {
        pileHeights.set(object.h, object);
        continue;
      }
      const materialColor = this.objectMaterialColor(kind.material);
      const frames = this.atlas.objectFrames.get(kind.key);
      if (!frames || !this.atlas.objectSpriteKeys.has(kind.key)) {
        this.drawObjectPrism(layers, usedRows, x, y, object.h, kind.height, materialColor, row);
        continue;
      }
      const relativeH = this.view.viewerH - object.h;
      if (relativeH <= 0) {
        this.drawObjectSprite(layers, usedRows, frames.full, x, y, object.h, row, true);
      } else if (relativeH >= kind.height) {
        this.drawObjectSprite(layers, usedRows, frames.full, x, y, object.h, row, false);
      } else {
        const split = Math.floor(relativeH);
        const lower = frames.lower.get(split);
        const upper = frames.upper.get(split);
        if (lower && upper) {
          this.drawObjectSprite(layers, usedRows, lower, x, y, object.h, row, false);
          this.drawObjectSprite(layers, usedRows, upper, x, y, object.h, row, true);
        } else {
          this.drawObjectSprite(layers, usedRows, frames.full, x, y, object.h, row, relativeH < kind.height / 2);
        }
      }
    }
    for (const [h, object] of pileHeights) {
      const kind = this.view.objectKinds.get(object.kind);
      if (!kind) continue;
      const at = toScreen(x + 0.5, y + 0.5, h);
      const bob = this.blitterFor(layers, usedRows, row, h);
      const marker = bob.create(at.sx - 6, at.sy - 3, "object:pile");
      marker.setTint(this.objectMaterialColor(kind.material));
    }
  }

  private drawObjectSprite(layers: ChunkLayers, usedRows: Set<number>, frame: ObjectFrame, x: number, y: number, h: number, row: number, aboveFeet: boolean): void {
    const at = toScreen(x + 0.5, y + 0.5, h);
    const bob = this.blitterFor(layers, usedRows, row, aboveFeet ? this.view.viewerH + 1 : this.view.viewerH);
    bob.create(at.sx - frame.anchorX, at.sy - frame.anchorY, frame.name);
  }

  private drawObjectPrism(layers: ChunkLayers, usedRows: Set<number>, x: number, y: number, h: number, height: number, color: number, row: number): void {
    if (height <= 0) return;
    const top = h + height;
    const topTint = this.shade(color, top);
    this.drawMask(layers, usedRows, "top", x, y, top, row, topTint);
    const lowerTop = Math.min(top, this.view.viewerH);
    const upperBottom = Math.max(h, this.view.viewerH);
    if (h < lowerTop) {
      const south = faceTile("s", x, y);
      const east = faceTile("e", x, y);
       this.drawFaceRun(layers, usedRows, "s", south.x, south.y, h, lowerTop, row, scaleColor(topTint, 0.82), 1);
       this.drawFaceRun(layers, usedRows, "e", east.x, east.y, h, lowerTop, row, scaleColor(topTint, 0.66), 1);
    }
    if (upperBottom < top) {
      const upperTint = this.shade(color, top);
      const south = faceTile("s", x, y);
      const east = faceTile("e", x, y);
       this.drawFaceRun(layers, usedRows, "s", south.x, south.y, upperBottom, top, row, scaleColor(upperTint, 0.82), 1);
       this.drawFaceRun(layers, usedRows, "e", east.x, east.y, upperBottom, top, row, scaleColor(upperTint, 0.66), 1);
    }
  }

  private objectMaterialColor(key: string): number {
    const material = [...(this.view.materials?.values() ?? [])].find((entry) => entry.key === key);
    return material ? this.materialColor(material.id) : 0x8b5d3c;
  }

  private restingSurfaceDrawn(x: number, y: number, h: number, excludeObjectId: number, cutoff: number, visited = new Set<number>()): boolean {
    if (visited.has(excludeObjectId)) return false;
    visited.add(excludeObjectId);
    const ground = this.chunks.groundH(x, y);
    const levels = this.chunks.levelsAt(x, y);
    const hasFloor = levels.some((level) => {
      const floorH = this.chunks.levelCell(x, y, level.z)?.floor_h;
      return floorH !== undefined && floorH !== NO_FLOOR;
    });
    if (ground === h && !this.chunks.isVoid(x, y, ground) && (!hasFloor || ground <= cutoff)) return true;
    if (levels.some((level) => this.chunks.levelCell(x, y, level.z)?.floor_h === h && h <= cutoff)) return true;
    return this.chunks.objectsAt(x, y).some((object) => {
      if (object.id === excludeObjectId) return false;
      const kind = this.view.objectKinds.get(object.kind);
      return kind?.surface && object.h + kind.height === h && object.h <= cutoff &&
        this.restingSurfaceDrawn(x, y, object.h, object.id, cutoff, new Set(visited));
    });
  }

  private variant(x: number, y: number): number {
    let hash = Math.imul(x, 0x45d9f3b) ^ Math.imul(y, 0x119de1f3);
    hash = Math.imul(hash ^ (hash >>> 16), 0x45d9f3b);
    return (hash ^ (hash >>> 16)) & 3;
  }

  private drawMask(
    layers: ChunkLayers,
    usedRows: Set<number>,
    frame: string,
    x: number,
    y: number,
    h: number,
    row: number,
    tint: number,
    alpha = 1,
  ): void {
    const at = toScreen(x, y, h);
    const offset = this.atlas.maskOffsets.get(frame) ?? { x: 0, y: 0 };
    const bob = this.blitterFor(layers, usedRows, row, h).create(
      at.sx - TILE_W / 2 + offset.x,
      at.sy + offset.y,
      frame,
    );
    bob.setTint(tint);
    bob.alpha = alpha;
  }

  private drawVertical(
    side: "s" | "e",
    layers: ChunkLayers,
    usedRows: Set<number>,
    tileX: number,
    tileY: number,
    h0: number,
    h1: number,
    materialId: number,
    light: number,
    row: number,
    alpha = 1,
    fillOnly = false,
  ): void {
    const tile = faceTile(side, tileX, tileY);
    const key = this.view.materials?.get(materialId)?.key;
    const textured = key !== undefined && this.atlas.sideKeys.has(key);
    const color = this.materialColor(materialId);
    const lowerTop = Math.min(h1, this.view.viewerH);
    const upperBottom = Math.max(h0, this.view.viewerH);
    if (textured && key !== undefined) {
      if (h0 < lowerTop) this.drawSideRun(side, layers, usedRows, tile.x, tile.y, h0, lowerTop, h1, key, color, light, fillOnly, row, alpha);
      if (upperBottom < h1) this.drawSideRun(side, layers, usedRows, tile.x, tile.y, upperBottom, h1, h1, key, color, light, fillOnly, row, alpha);
      return;
    }
    const tint = scaleColor(this.shade(color, h1), light);
    if (h0 < lowerTop) this.drawFaceRun(layers, usedRows, side, tile.x, tile.y, h0, lowerTop, row, tint, alpha);
    if (upperBottom < h1) this.drawFaceRun(layers, usedRows, side, tile.x, tile.y, upperBottom, h1, row, tint, alpha);
  }

  /** Textured faces stack one unit at a time: a cap on the face's real top, fill below. */
  private drawSideRun(
    side: "s" | "e",
    layers: ChunkLayers,
    usedRows: Set<number>,
    x: number,
    y: number,
    bottom: number,
    top: number,
    faceTop: number,
    key: string,
    color: number,
    light: number,
    fillOnly: boolean,
    row: number,
    alpha: number,
  ): void {
    const tint = sideTint(color, faceTop, light);
    for (let unitTop = bottom + 1; unitTop <= top; unitTop += 1) {
      const part = !fillOnly && unitTop === faceTop ? "cap" : "fill";
      const frame = `side:${key}:${side}:${part}:${sideVariant(x, y, side, unitTop)}`;
      this.drawMask(layers, usedRows, frame, x, y, unitTop, row, tint, alpha);
    }
  }

  private drawFaceRun(
    layers: ChunkLayers,
    usedRows: Set<number>,
    side: "s" | "e",
    x: number,
    y: number,
    bottom: number,
    top: number,
    row: number,
    tint: number,
    alpha: number,
  ): void {
    let current = bottom;
    while (current < top) {
      const units = ([8, 4, 2, 1] as const).find((size) => size <= top - current)!;
      this.drawMask(layers, usedRows, `face${side.toUpperCase()}${units}`, x, y, current + units, row, tint, alpha);
      current += units;
    }
  }

  private chunkBounds(chunk: WorldChunk): { bbox: ChunkLayers["bbox"]; hMin: number; hMax: number } {
    const size = this.chunks.size;
    let minH = Math.min(...chunk.ground_h) - 4;
    let maxH = Math.max(...chunk.ground_h);
    for (const level of chunk.levels) {
      for (let index = 0; index < level.floor_h.length; index += 1) {
        const floorH = level.floor_h[index];
        if (floorH !== NO_FLOOR) {
          minH = Math.min(minH, floorH - 1);
          maxH = Math.max(maxH, floorH + MAX_WALL_H);
        }
        if (level.wall_n[index] || level.wall_w[index]) {
          maxH = Math.max(maxH, (level.z + 1) * LEVEL_H);
        }
      }
    }
    for (const object of chunk.objects ?? []) {
      const kind = this.view.objectKinds.get(object.kind);
      minH = Math.min(minH, object.h - 1);
      maxH = Math.max(maxH, object.h + (kind?.height ?? 1));
    }
    const corners = [
      [chunk.cx * size, chunk.cy * size],
      [(chunk.cx + 1) * size, chunk.cy * size],
      [chunk.cx * size, (chunk.cy + 1) * size],
      [(chunk.cx + 1) * size, (chunk.cy + 1) * size],
    ];
    const points = corners.flatMap(([x, y]) => [toScreen(x!, y!, minH), toScreen(x!, y!, maxH)]);
    return {
      hMin: minH,
      hMax: maxH,
      bbox: {
        minX: Math.min(...points.map((point) => point.sx)) - TILE_W,
        minY: Math.min(...points.map((point) => point.sy)) - TILE_H,
        maxX: Math.max(...points.map((point) => point.sx)) + TILE_W,
        maxY: Math.max(...points.map((point) => point.sy)) + TILE_H,
      },
    };
  }
}
