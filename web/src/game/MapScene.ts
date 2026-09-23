import Phaser from "phaser";
import { ClientPrediction } from "../net/prediction";
import { EMPTY_POSITION, type Direction, type Position } from "../net/protocol";
import { WebSocketClient } from "../net/client";
import { ChunkStore } from "../world/ChunkStore";
import { loadMaterials } from "../world/materials";
import type { components } from "../net/schema";
import { defaultZoom, loadZoom, saveZoom, stepZoom, WheelAccumulator, type ZoomLevel } from "./zoom";
import { H_PX, TILE_H, TILE_W, baseDepth, faceTile, keysToWorld, nikoDepth, rowDepth, screenToRay, toScreen } from "./iso";
import { cutoffH } from "../world/cutaway";
import { pickTile } from "../world/pick";
import { isCovered, occludingStructures, type Structure } from "../world/occlusion";
import { cutoffChunkKeys, changedChunkKeys, materialChunkKeys, structureChunkKeys, tileChunkKeys, viewerHeightChunkKeys, type DirtyChunk } from "./dirty";
import { diamondMask, edgeLineMask, faceMask, shearSideCell, wallMask, type SideCell, type TileMask } from "./tileMasks";
import { shadeColor, sideTint, sideVariant, spriteTint } from "./terrainSprites";
import { TERRAIN_SHEETS, TERRAIN_SIDE_SHEETS } from "./terrainSheets";
type Material = components["schemas"]["MaterialResponse"];
type WorldChunk = components["schemas"]["ChunkResponse"];
import { EDGE_N_DOORWAY, EDGE_N_WINDOW, EDGE_W_DOORWAY, EDGE_W_WINDOW, LEVEL_H, NO_FLOOR } from "../world/rules";

const CUT_DEPTH = 8;
const CUT_WIDTH = 3;
const MAX_WALL_H = 6;
// Speed is averaged over this window so reconcile snaps do not read as spikes.
const TELEMETRY_INTERVAL = 0.2;
// The server moves Niko one 1/20 s step per input, so a held key repeats at 20 Hz.
const INPUT_INTERVAL = 0.05;
// Below this the prediction and the server agree; above it, an idle Niko is resynced.
const RESYNC_METRES = 0.05;

export type ContextTarget = { x: number; y: number; z: number; screenX: number; screenY: number };

/** Local avatar readout; `speed` is metres per second across the ground plane. */
export type Telemetry = Position & { speed: number };

type MapSceneOptions = {
  client: WebSocketClient;
  onContextMenu: (target: ContextTarget) => void;
  onTelemetry: (telemetry: Telemetry) => void;
  onZoom: (level: ZoomLevel) => void;
};

type ChunkLayers = {
  base: Phaser.GameObjects.Blitter;
  rows: Map<number, Phaser.GameObjects.Blitter>;
  bbox: { minX: number; minY: number; maxX: number; maxY: number };
  hMin: number;
  hMax: number;
  visible: boolean;
};

export class MapScene extends Phaser.Scene {
  private readonly options: MapSceneOptions;
  private readonly chunks = new ChunkStore();
  private readonly chunkLayers = new Map<string, ChunkLayers>();
  private materials: Map<number, Material> | null = null;
  private niko!: Phaser.GameObjects.Graphics;
  private nikoGhost!: Phaser.GameObjects.Graphics;
  private prediction = new ClientPrediction(EMPTY_POSITION);
  private hasAuthoritativePosition = false;
  private viewerH = 0;
  private paused = false;
  private zoom: ZoomLevel | null = null;
  private readonly wheelAccumulator = new WheelAccumulator();
  private keys!: Record<"up" | "down" | "left" | "right", Phaser.Input.Keyboard.Key>;
  private lastDirection: Direction = { x: 0, y: 0 };
  private inputElapsed = 0;
  private nikoTile = "";
  private cutoff = Number.POSITIVE_INFINITY;
  private readonly dirtyChunks = new Set<string>();
  private readonly maskOffsets = new Map<string, { x: number; y: number }>();
  private readonly spriteKeys = new Set<string>();
  private readonly sideKeys = new Set<string>();
  private structures: Structure[] = [];
  private telemetryOrigin: { x: number; y: number } | null = null;
  private telemetryElapsed = 0;
  private removeStateListener?: () => void;
  private removeSnapshotListener?: () => void;
  private removeAckListener?: () => void;
  private removeChunkListener?: () => void;

  constructor(options: MapSceneOptions) {
    super("map");
    this.options = options;
  }

  preload(): void {
    this.load.on("loaderror", (file: Phaser.Loader.File) => {
      console.error(`terrain sheet failed: ${file.key} ${file.url}`);
    });
    for (const [key, sheet] of Object.entries(TERRAIN_SHEETS)) {
      this.load.spritesheet(`sheet:${key}`, sheet, { frameWidth: TILE_W, frameHeight: TILE_H });
    }
    for (const [key, sheet] of Object.entries(TERRAIN_SIDE_SHEETS)) {
      this.load.image(`side:${key}`, sheet);
    }
  }

  create(): void {
    this.createTerrainAtlas();
    this.prediction.attachStore(this.chunks);
    void loadMaterials().then((materials) => {
      this.materials = materials;
      this.chunks.setMaterials(materials);
      this.markDirty(materialChunkKeys(this.dirtyChunkInfo()));
    });

    this.niko = this.add.graphics();
    this.niko.fillStyle(0x000000, 0.35);
    this.niko.fillEllipse(0, 0, 27, 13);
    this.niko.fillStyle(0x2583ff, 1);
    this.niko.fillRoundedRect(-9, -60, 18, 58, 6);
    this.niko.lineStyle(2, 0xffffff, 1);
    this.niko.strokeRoundedRect(-9, -60, 18, 58, 6);
    this.cameras.main.startFollow(this.niko, true, 0.12, 0.12);

    this.nikoGhost = this.add.graphics();
    this.nikoGhost.fillStyle(0x2583ff, 0.35);
    this.nikoGhost.fillRoundedRect(-9, -60, 18, 58, 6);
    this.nikoGhost.lineStyle(2, 0xffffff, 0.9);
    this.nikoGhost.strokeRoundedRect(-9, -60, 18, 58, 6);
    this.nikoGhost.setDepth(Number.MAX_SAFE_INTEGER).setVisible(false);

    this.keys = {
      up: this.input.keyboard!.addKey(Phaser.Input.Keyboard.KeyCodes.W),
      down: this.input.keyboard!.addKey(Phaser.Input.Keyboard.KeyCodes.S),
      left: this.input.keyboard!.addKey(Phaser.Input.Keyboard.KeyCodes.A),
      right: this.input.keyboard!.addKey(Phaser.Input.Keyboard.KeyCodes.D),
    };

    this.input.on("wheel", (pointer: Phaser.Input.Pointer, _objects: unknown[], _deltaX: number, deltaY: number) => {
      const event = pointer.event as WheelEvent;
      if (event.ctrlKey || event.metaKey) return;
      const steps = this.wheelAccumulator.push(deltaY, event.deltaMode, performance.now());
      if (steps !== 0 && this.zoom !== null) this.applyZoom(stepZoom(this.zoom, steps));
    });
    this.input.keyboard!.on("keydown", (event: KeyboardEvent) => {
      if (document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement) return;
      if (event.ctrlKey || event.metaKey || event.altKey || this.zoom === null) return;

      if (event.key === "+" || event.key === "=") {
        event.preventDefault();
        this.applyZoom(stepZoom(this.zoom, 1));
      } else if (event.key === "-") {
        event.preventDefault();
        this.applyZoom(stepZoom(this.zoom, -1));
      } else if (event.key === "0") {
        event.preventDefault();
        this.applyZoom(defaultZoom(window.devicePixelRatio));
      }
    });

    this.input.on("pointerdown", (pointer: Phaser.Input.Pointer) => {
      if (!pointer.rightButtonDown()) return;
      const ray = screenToRay(pointer.worldX, pointer.worldY);
      const target = pickTile(this.chunks, ray.s, ray.t, this.viewerH, (x, y) => this.cutoffAt(x, y));
      if (!target) return;
      const canvasBounds = this.game.canvas.getBoundingClientRect();
      this.options.onContextMenu({
        ...target,
        screenX: pointer.x + canvasBounds.left,
        screenY: pointer.y + canvasBounds.top,
      });
    });
    this.input.mouse?.disableContextMenu();
    this.applyZoom(loadZoom(defaultZoom(window.devicePixelRatio)));

    this.removeSnapshotListener = this.options.client.onSnapshot(() => {
      this.clearChunkLayers();
      this.chunks.clear();
      this.structures = [];
      this.nikoGhost.setVisible(false);
      this.hasAuthoritativePosition = false;
      this.telemetryOrigin = null;
      this.telemetryElapsed = 0;
    });
    this.options.client.setBeforeCommand(() => this.flushPartialStep());
    this.removeStateListener = this.options.client.onState((state) => {
      this.paused = state.paused;
      if (state.world) {
        this.chunks.setWorldInfo(state.world.chunk_size, state.world.bounds);
        this.applyCameraBounds(state.world.bounds);
      }
      const player = state.actors.player ?? state.actors.niko ?? Object.values(state.actors)[0];
      if (player && !this.hasAuthoritativePosition) {
        this.prediction.reset(player);
        this.hasAuthoritativePosition = true;
        this.telemetryOrigin = null;
      } else if (player && this.prediction.idle()) {
        // Climbing and being lowered by a dig move Niko with no ack to reconcile against.
        const predicted = this.prediction.position;
        const drift = Math.hypot(player.x - predicted.x, player.y - predicted.y);
        if (drift > RESYNC_METRES || (player.h !== undefined && player.h !== predicted.h)) {
          this.prediction.reset(player);
        }
      }
    });
    this.removeAckListener = this.options.client.onAck((position, sequence) => {
      if (!position) return;
      this.prediction.reconcile(position, sequence);
    });
    this.removeChunkListener = this.options.client.onChunk((chunk) => {
      if (!this.chunks.set(chunk)) return;
      const key = `${chunk.cx},${chunk.cy}`;
      if (this.chunkLayers.has(key)) this.updateChunkBounds(chunk);
      else this.layersFor(chunk);
      if (this.structures.some((structure) => this.chunkTouchesStructure(chunk.cx, chunk.cy, structure))) {
        const [x, y] = this.nikoTile.split(",").map(Number);
        this.refreshStructures(x, y, this.viewerH, this.cutoff);
      }
      this.markDirty(changedChunkKeys(this.dirtyChunkInfo(), `${chunk.cx},${chunk.cy}`));
    });
  }

  update(_: number, delta: number): void {
    const seconds = Math.min(delta / 1000, 0.1);
    const direction = this.readDirection();
    const changed = direction.x !== this.lastDirection.x || direction.y !== this.lastDirection.y;
    const held = direction.x !== 0 || direction.y !== 0;
    if (changed) {
      this.flushPartialStep();
      this.lastDirection = direction;
    }
    if (held && !this.paused) {
      this.inputElapsed += seconds;
      while (this.inputElapsed >= INPUT_INTERVAL) {
        this.sendMovementStep(direction, INPUT_INTERVAL);
        this.inputElapsed -= INPUT_INTERVAL;
      }
    }
    const position = this.prediction.render(
      this.paused ? { x: 0, y: 0 } : direction,
      this.paused ? 0 : this.inputElapsed,
    );
    this.setViewer(position.x, position.y, position.h ?? this.viewerH);
    this.flushDirtyQueue();
    const screen = toScreen(position.x, position.y, position.h ?? this.viewerH);
    this.niko.setPosition(screen.sx, screen.sy);
    this.niko.setDepth(nikoDepth(position.x, position.y));
    this.nikoGhost.setPosition(screen.sx, screen.sy).setVisible(
      isCovered(this.chunks, position.x, position.y, position.h ?? this.viewerH, (x, y) => this.cutoffAt(x, y)),
    );
    this.updateCulling();
    this.sampleTelemetry(position, seconds);
  }

  shutdown(): void {
    this.removeStateListener?.();
    this.removeSnapshotListener?.();
    this.removeAckListener?.();
    this.removeChunkListener?.();
    this.options.client.setBeforeCommand(null);
    this.clearChunkLayers();
  }

  private flushPartialStep(): void {
    if (this.inputElapsed > 0 && (this.lastDirection.x !== 0 || this.lastDirection.y !== 0)) {
      this.sendMovementStep(this.lastDirection, this.inputElapsed);
    }
    this.inputElapsed = 0;
  }

  private sendMovementStep(direction: Direction, dt: number): void {
    const sequence = this.options.client.nextSequence();
    this.prediction.pushStep({ sequence, direction, dt });
    this.options.client.sendInput(direction, dt, sequence);
  }

  private sampleTelemetry(position: Position, seconds: number): void {
    this.telemetryElapsed += seconds;
    if (this.telemetryElapsed < TELEMETRY_INTERVAL) return;
    // The predicted position is mutated in place, so the origin must be a copy.
    const origin = this.telemetryOrigin ?? position;
    const speed = Math.hypot(position.x - origin.x, position.y - origin.y) / this.telemetryElapsed;
    this.telemetryOrigin = { x: position.x, y: position.y };
    this.telemetryElapsed = 0;
    this.options.onTelemetry({ x: position.x, y: position.y, z: position.z, h: position.h, speed });
  }

  private readDirection(): Direction {
    if (document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement) {
      return { x: 0, y: 0 };
    }
    const kx = Number(this.keys.right.isDown) - Number(this.keys.left.isDown);
    const ky = Number(this.keys.down.isDown) - Number(this.keys.up.isDown);
    return keysToWorld(kx, ky);
  }

  private applyZoom(level: ZoomLevel): void {
    if (level === this.zoom) return;
    this.cameras.main.setZoom(level);
    this.zoom = level;
    saveZoom(level);
    this.options.onZoom(level);
    this.updateCulling();
  }

  private applyCameraBounds(bounds: number[]): void {
    if (bounds.length !== 4) return;
    const [minX, minY, maxX, maxY] = bounds;
    const left = (minX - maxY - 1) * TILE_W;
    const right = (maxX + 1 - minY) * TILE_W;
    const top = (minX + minY) * TILE_H / 2 - 32 * H_PX;
    const bottom = (maxX + maxY + 2) * TILE_H / 2 + 16 * H_PX;
    this.cameras.main.setBounds(left, top, right - left, bottom - top);
  }

  private materialColor(id: number): number {
    const raw = this.materials?.get(id)?.color;
    if (!raw) return id === 0 ? 0x426b4d : 0x5a7450;
    return parseInt(raw.slice(1), 16);
  }

  private createTerrainAtlas(): void {
    const masks: Array<[string, TileMask]> = [["top", diamondMask()]];
    for (const side of ["s", "e"] as const) {
      for (const units of [1, 2, 4, 8] as const) masks.push([`face${side.toUpperCase()}${units}`, faceMask(side, units)]);
    }
    for (const edge of ["n", "w"] as const) {
      for (const units of [1, 2, 4] as const) masks.push([`wall${edge.toUpperCase()}${units}`, wallMask(edge, units)]);
      masks.push([`line${edge.toUpperCase()}`, edgeLineMask(edge)]);
    }

    const spriteSheets = Object.entries(TERRAIN_SHEETS).flatMap(([key]) => {
      const source = this.textures.get(`sheet:${key}`).getSourceImage() as HTMLImageElement;
      if (source.width !== 256 || source.height !== 32) {
        console.warn(`Skipping ${key} terrain sprite sheet: expected 256x32, received ${source.width}x${source.height}`);
        return [];
      }
      return [[key, source] as const];
    });

    const sideFrames: Array<[string, SideCell]> = [];
    let sideWidth = 0;
    let sideHeight = 0;
    for (const [key, file] of Object.entries(TERRAIN_SIDE_SHEETS)) {
      const texture = this.textures.get(`side:${key}`);
      const source = texture.getSourceImage() as HTMLImageElement | undefined;
      if (!source || (source.width === 0 && source.height === 0)) {
        console.warn(`Skipping ${key} terrain side sheet: ${file} not loaded`);
        continue;
      }
      if (source.width !== 128 || source.height !== 32) {
        console.warn(`Skipping ${key} terrain side sheet: expected 128x32, received ${source.width}x${source.height}`);
        continue;
      }
      if (sideWidth + 16 * 32 > 4096) {
        console.warn(`Skipping ${key} terrain side sheet: atlas side band would exceed 4096 px`);
        continue;
      }
      const sheet = { width: source.width, height: source.height, data: this.readPixels(source) };
      for (const side of ["s", "e"] as const) {
        for (const part of ["cap", "fill"] as const) {
          for (const variant of [0, 1, 2, 3] as const) {
            const cell = shearSideCell(sheet, side, part, variant);
            sideFrames.push([`side:${key}:${side}:${part}:${variant}`, cell]);
            sideHeight = Math.max(sideHeight, cell.height);
          }
        }
      }
      sideWidth += 16 * 32;
      this.sideKeys.add(key);
    }

    const slotCount = spriteSheets.length * 4 + masks.length;
    const atlas = this.textures.createCanvas("terrain", Math.max(slotCount * TILE_W, sideWidth), 160 + sideHeight);
    if (!atlas) throw new Error("Failed to create terrain atlas");
    const context = atlas.context;
    let spriteSlot = 0;
    for (const [key, source] of spriteSheets) {
      context.drawImage(source, spriteSlot * TILE_W, 0);
      for (let frame = 0; frame < 4; frame += 1) {
        atlas.add(`${key}${frame}`, 0, (spriteSlot + frame) * TILE_W, 0, TILE_W, TILE_H);
      }
      this.spriteKeys.add(key);
      spriteSlot += 4;
    }
    masks.forEach(([name, mask], index) => {
      const x = (spriteSlot + index) * TILE_W;
      const image = context.createImageData(mask.width, mask.height);
      for (let pixel = 0; pixel < mask.alpha.length; pixel += 1) {
        const at = pixel * 4;
        image.data[at] = 255;
        image.data[at + 1] = 255;
        image.data[at + 2] = 255;
        image.data[at + 3] = mask.alpha[pixel] ?? 0;
      }
      context.putImageData(image, x, 0);
      atlas.add(name, 0, x, 0, mask.width, mask.height);
      this.maskOffsets.set(name, { x: mask.offsetX, y: mask.offsetY });
    });
    let sideX = 0;
    for (const [name, cell] of sideFrames) {
      const image = context.createImageData(cell.width, cell.height);
      image.data.set(cell.rgba);
      context.putImageData(image, sideX, 160);
      atlas.add(name, 0, sideX, 160, cell.width, cell.height);
      this.maskOffsets.set(name, { x: cell.offsetX, y: cell.offsetY });
      sideX += cell.width;
    }
    atlas.refresh();
  }

  /** Reads a loaded sheet's pixels so the pure shearing helper can run outside a Phaser texture. */
  private readPixels(source: HTMLImageElement): Uint8ClampedArray {
    const canvas = document.createElement("canvas");
    canvas.width = source.width;
    canvas.height = source.height;
    const context = canvas.getContext("2d");
    if (!context) throw new Error("Failed to read terrain side sheet pixels");
    context.drawImage(source, 0, 0);
    return context.getImageData(0, 0, source.width, source.height).data;
  }

  private layersFor(chunk: WorldChunk): ChunkLayers {
    const key = `${chunk.cx},${chunk.cy}`;
    const existing = this.chunkLayers.get(key);
    if (existing) return existing;
    const base = this.add.blitter(0, 0, "terrain");
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

  private clearChunkLayers(): void {
    for (const layers of this.chunkLayers.values()) {
      layers.base.destroy();
      for (const row of layers.rows.values()) row.destroy();
    }
    this.chunkLayers.clear();
    this.dirtyChunks.clear();
  }

  private setViewer(x: number, y: number, h: number): void {
    const tile = `${Math.floor(x)},${Math.floor(y)}`;
    const nextCutoff = cutoffH(this.chunks, x, y, h);
    if (h === this.viewerH && tile === this.nikoTile && nextCutoff === this.cutoff) return;
    const oldH = this.viewerH;
    const oldTile = this.nikoTile;
    const oldCutoff = this.cutoff;
    this.viewerH = h;
    this.nikoTile = tile;
    this.cutoff = nextCutoff;
    if (oldH !== h || oldTile !== tile || oldCutoff !== nextCutoff) {
      this.refreshStructures(Math.floor(x), Math.floor(y), h, nextCutoff);
    }
    const chunks = this.dirtyChunkInfo();
    if (oldH !== h) this.markDirty(viewerHeightChunkKeys(chunks, oldH, h));
    if (oldCutoff !== nextCutoff) this.markDirty(cutoffChunkKeys(chunks));
    if (oldTile !== tile) {
      const [oldX, oldY] = oldTile ? oldTile.split(",").map(Number) : [x, y];
      this.markDirty(tileChunkKeys(chunks, this.chunks.size, { x: oldX, y: oldY }, { x: Math.floor(x), y: Math.floor(y) }));
    }
  }

  private cutoffAt(x: number, y: number): number {
    const tile = `${Math.floor(x)},${Math.floor(y)}`;
    const structureCutoff = this.structures.find((structure) => structure.tiles.has(tile))?.cutoff ?? Infinity;
    return Math.min(this.cutoff, structureCutoff);
  }

  private refreshStructures(x: number, y: number, h: number, cutoff: number): void {
    const previous = this.structures;
    const next = occludingStructures(this.chunks, x, y, h, cutoff);
    const signature = (structures: Structure[]): string[] => structures
      .map((structure) => `${[...structure.tiles].sort().join(";")}|${structure.cutoff}`)
      .sort();
    if (JSON.stringify(signature(previous)) === JSON.stringify(signature(next))) return;
    this.structures = next;
    const bounds = [...previous, ...next].map((structure) => structure.bounds);
    this.markDirty(structureChunkKeys(this.dirtyChunkInfo(), this.chunks.size, bounds));
  }

  private chunkTouchesStructure(cx: number, cy: number, structure: Structure): boolean {
    const minX = cx * this.chunks.size;
    const minY = cy * this.chunks.size;
    const maxX = minX + this.chunks.size;
    const maxY = minY + this.chunks.size;
    const bounds = structure.bounds;
    return minX < bounds.maxX && maxX > bounds.minX && minY < bounds.maxY && maxY > bounds.minY;
  }

  private shade(base: number, h: number): number {
    return shadeColor(base, h);
  }

  private drawChunk(chunk: WorldChunk): void {
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
        const tileCutoff = this.cutoffAt(x, y);
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
              const stub = this.isFrontWall("n", x, y, wallBase);
              this.drawWall("n", layers, usedRows, x, y, wallBase, wallN, tileCutoff, (edgeFlags & EDGE_N_WINDOW) !== 0, stub, row);
            }
            if (wallW && (edgeFlags & EDGE_W_DOORWAY) === 0) {
              const stub = this.isFrontWall("w", x, y, wallBase);
              this.drawWall("w", layers, usedRows, x, y, wallBase, wallW, tileCutoff, (edgeFlags & EDGE_W_WINDOW) !== 0, stub, row);
            }
          }
        }
      }
    }

    for (const [row, graphics] of layers.rows) {
      if (!usedRows.has(row)) {
        graphics.destroy();
        layers.rows.delete(row);
      }
    }
  }

  private targetBlitter(layers: ChunkLayers, usedRows: Set<number>, row: number): Phaser.GameObjects.Blitter {
    usedRows.add(row);
    let blitter = layers.rows.get(row);
    if (!blitter) {
      blitter = this.add.blitter(0, 0, "terrain");
      blitter.setDepth(rowDepth(row)).setVisible(layers.visible);
      layers.rows.set(row, blitter);
    }
    return blitter;
  }

  private blitterFor(layers: ChunkLayers, usedRows: Set<number>, row: number, h: number): Phaser.GameObjects.Blitter {
    return h <= this.viewerH ? layers.base : this.targetBlitter(layers, usedRows, row);
  }

  private drawTop(layers: ChunkLayers, usedRows: Set<number>, x: number, y: number, h: number, materialId: number, row: number): void {
    this.drawSurface(layers, usedRows, x, y, h, materialId, row);
  }

  private drawSurface(layers: ChunkLayers, usedRows: Set<number>, x: number, y: number, h: number, materialId: number, row: number): void {
    const materialKey = this.materials?.get(materialId)?.key;
    const color = this.materialColor(materialId);
    const textured = materialKey !== undefined && this.spriteKeys.has(materialKey);
    const frame = textured ? `${materialKey}${this.variant(x, y)}` : "top";
    this.drawMask(layers, usedRows, frame, x, y, h, row, textured ? spriteTint(color, h) : this.shade(color, h));
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
    const offset = this.maskOffsets.get(frame) ?? { x: 0, y: 0 };
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
    const key = this.materials?.get(materialId)?.key;
    const textured = key !== undefined && this.sideKeys.has(key);
    const color = this.materialColor(materialId);
    const lowerTop = Math.min(h1, this.viewerH);
    const upperBottom = Math.max(h0, this.viewerH);
    if (textured && key !== undefined) {
      if (h0 < lowerTop) this.drawSideRun(side, layers, usedRows, tile.x, tile.y, h0, lowerTop, h1, key, color, light, fillOnly, row, alpha);
      if (upperBottom < h1) this.drawSideRun(side, layers, usedRows, tile.x, tile.y, upperBottom, h1, h1, key, color, light, fillOnly, row, alpha);
      return;
    }
    const tint = this.scaleColor(this.shade(color, h1), light);
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

  private scaleColor(color: number, factor: number): number {
    return (Math.round(((color >> 16) & 0xff) * factor) << 16) |
      (Math.round(((color >> 8) & 0xff) * factor) << 8) |
      Math.round((color & 0xff) * factor);
  }

  private isFrontWall(edge: "n" | "w", x: number, y: number, base: number): boolean {
    const [nx, ny] = this.nikoTile.split(",").map(Number);
    const mx = edge === "n" ? x + 0.5 : x;
    const my = edge === "n" ? y : y + 0.5;
    return (edge === "n" ? y > ny : x > nx) &&
      (mx + my) - (nx + ny + 1) > 0 && (mx + my) - (nx + ny + 1) <= CUT_DEPTH &&
      Math.abs((mx - my) - (nx - ny)) <= CUT_WIDTH &&
      base < this.viewerH + 4 && base + MAX_WALL_H > this.viewerH;
  }

  private drawWall(
    edge: "n" | "w",
    layers: ChunkLayers,
    usedRows: Set<number>,
    x: number,
    y: number,
    base: number,
    materialId: number,
    cutoff: number,
    window: boolean,
    stub: boolean,
    row: number,
  ): void {
    const color = this.materialColor(materialId);
    const light = edge === "n" ? 0.82 : 0.66;
    const cap = Math.min(base + MAX_WALL_H, cutoff);
    const top = stub ? Math.min(base + 1, cap) : cap;
    if (top <= base) return;
    if (stub || !window) {
      this.drawWallRun(layers, usedRows, edge, x, y, base, top, row, this.scaleColor(color, light));
    } else {
      this.drawWallRun(layers, usedRows, edge, x, y, base, Math.min(base + 2, top), row, this.scaleColor(color, light));
      if (top > base + 4) this.drawWallRun(layers, usedRows, edge, x, y, base + 4, top, row, this.scaleColor(color, light));
      if (top > base + 2) this.drawWallRun(layers, usedRows, edge, x, y, base + 2, Math.min(base + 4, top), row, this.scaleColor(0xd8e6f0, light), 0.45);
    }
    if (!stub && top === base + MAX_WALL_H) {
      this.drawMask(layers, usedRows, `line${edge.toUpperCase()}`, x, y, top, row, this.scaleColor(color, 1.12));
    }
  }

  private drawWallRun(
    layers: ChunkLayers,
    usedRows: Set<number>,
    edge: "n" | "w",
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
      this.drawMask(layers, usedRows, `wall${edge.toUpperCase()}${units}`, x, y, current + units, row, tint, alpha);
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
    const corners = [
      [chunk.cx * size, chunk.cy * size],
      [(chunk.cx + 1) * size, chunk.cy * size],
      [chunk.cx * size, (chunk.cy + 1) * size],
      [(chunk.cx + 1) * size, (chunk.cy + 1) * size],
    ];
    const points = corners.flatMap(([x, y]) => [toScreen(x, y, minH), toScreen(x, y, maxH)]);
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

  private updateChunkBounds(chunk: WorldChunk): void {
    const layers = this.chunkLayers.get(`${chunk.cx},${chunk.cy}`);
    if (!layers) return;
    const bounds = this.chunkBounds(chunk);
    layers.bbox = bounds.bbox;
    layers.hMin = bounds.hMin;
    layers.hMax = bounds.hMax;
  }

  private updateCulling(): void {
    const view = this.cameras.main.worldView;
    for (const layers of this.chunkLayers.values()) {
      const visible = this.intersectsView(layers.bbox);
      if (visible === layers.visible) continue;
      layers.visible = visible;
      layers.base.setVisible(visible);
      for (const row of layers.rows.values()) row.setVisible(visible);
    }
  }

  private dirtyChunkInfo(): DirtyChunk[] {
    return [...this.chunkLayers].map(([key, layers]) => {
      const [cx, cy] = key.split(",").map(Number);
      return {
        key,
        cx,
        cy,
        hMin: layers.hMin,
        hMax: layers.hMax,
        hasLevels: (this.chunks.get(cx, cy)?.levels.length ?? 0) > 0,
      };
    });
  }

  private markDirty(keys: string[]): void {
    for (const key of keys) {
      const [cx, cy] = key.split(",").map(Number);
      if (this.chunks.get(cx, cy)) this.dirtyChunks.add(key);
    }
  }

  private flushDirtyQueue(): void {
    const [viewerX, viewerY] = this.nikoTile ? this.nikoTile.split(",").map(Number) : [0, 0];
    const inCore = (key: string): boolean => {
      const [cx, cy] = key.split(",").map(Number);
      return Math.abs(cx - Math.floor(viewerX / this.chunks.size)) <= 1 &&
        Math.abs(cy - Math.floor(viewerY / this.chunks.size)) <= 1;
    };
    for (const key of [...this.dirtyChunks].filter(inCore)) {
      this.drawDirtyChunk(key);
    }

    const started = performance.now();
    const pending = [...this.dirtyChunks].map((key) => {
      const [cx, cy] = key.split(",").map(Number);
      const layers = this.chunkLayers.get(key)!;
      const x = (cx + 0.5) * this.chunks.size;
      const y = (cy + 0.5) * this.chunks.size;
      return { key, layers, distance: (x - viewerX) ** 2 + (y - viewerY) ** 2 };
    }).sort((a, b) => a.distance - b.distance);
    for (const { key, layers } of pending) {
      if (!this.intersectsView(layers.bbox)) continue;
      if (performance.now() - started >= 4) break;
      this.drawDirtyChunk(key);
    }
    this.updateCulling();
  }

  private drawDirtyChunk(key: string): void {
    const [cx, cy] = key.split(",").map(Number);
    const chunk = this.chunks.get(cx, cy);
    if (!chunk) {
      this.dirtyChunks.delete(key);
      return;
    }
    this.dirtyChunks.delete(key);
    this.drawChunk(chunk);
  }

  private intersectsView(bbox: ChunkLayers["bbox"]): boolean {
    const view = this.cameras.main.worldView;
    return bbox.maxX >= view.x - TILE_W && bbox.minX <= view.right + TILE_W &&
      bbox.maxY >= view.y - TILE_H && bbox.minY <= view.bottom + TILE_H;
  }

}

export function createGame(
  parent: HTMLElement,
  client: WebSocketClient,
  onContextMenu: (target: ContextTarget) => void,
  onTelemetry: (telemetry: Telemetry) => void,
  onZoom: (level: ZoomLevel) => void,
): Phaser.Game {
  return new Phaser.Game({
    type: Phaser.AUTO,
    parent,
    width: window.innerWidth,
    height: window.innerHeight,
    backgroundColor: "#07080a",
    pixelArt: true,
    render: { antialias: false, roundPixels: true },
    scale: { mode: Phaser.Scale.RESIZE, autoCenter: Phaser.Scale.CENTER_BOTH },
    scene: new MapScene({ client, onContextMenu, onTelemetry, onZoom }),
  });
}
