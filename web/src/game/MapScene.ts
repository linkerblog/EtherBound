import Phaser from "phaser";
import { ClientPrediction } from "../net/prediction";
import { EMPTY_POSITION, type Direction, type Position } from "../net/protocol";
import { WebSocketClient } from "../net/client";
import { ChunkStore } from "../world/ChunkStore";
import { loadMaterials } from "../world/materials";
import type { components } from "../net/schema";
import { defaultZoom, loadZoom, saveZoom, stepZoom, WheelAccumulator, type ZoomLevel } from "./zoom";
import { H_PX, TILE_H, TILE_W, baseDepth, keysToWorld, nikoDepth, rowDepth, screenToRay, toScreen } from "./iso";
import { cutoffH } from "../world/cutaway";
import { pickTile } from "../world/pick";
import grassSprites from "../../../src/sprites/grass/grass_x4_2.png";
type Material = components["schemas"]["MaterialResponse"];
type WorldChunk = components["schemas"]["ChunkResponse"];
import { EDGE_N_DOORWAY, EDGE_N_WINDOW, EDGE_W_DOORWAY, EDGE_W_WINDOW, LEVEL_H, NO_FLOOR, LEVEL_VOID } from "../world/rules";

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
  base: Phaser.GameObjects.Graphics;
  rows: Map<number, Phaser.GameObjects.Graphics>;
  grass: Phaser.GameObjects.Image[];
  bbox: { minX: number; minY: number; maxX: number; maxY: number };
};

export class MapScene extends Phaser.Scene {
  private readonly options: MapSceneOptions;
  private readonly chunks = new ChunkStore();
  private readonly chunkLayers = new Map<string, ChunkLayers>();
  private materials: Map<number, Material> | null = null;
  private niko!: Phaser.GameObjects.Graphics;
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
  private lastView: { x: number; y: number; width: number; height: number } | null = null;
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
    this.load.spritesheet("grass", grassSprites, { frameWidth: TILE_W, frameHeight: TILE_H });
  }

  create(): void {
    this.prediction.attachStore(this.chunks);
    void loadMaterials().then((materials) => {
      this.materials = materials;
      this.chunks.setMaterials(materials);
      this.redrawAll();
    });

    this.niko = this.add.graphics();
    this.niko.fillStyle(0x000000, 0.35);
    this.niko.fillEllipse(0, 0, 27, 13);
    this.niko.fillStyle(0x2583ff, 1);
    this.niko.fillRoundedRect(-9, -60, 18, 58, 6);
    this.niko.lineStyle(2, 0xffffff, 1);
    this.niko.strokeRoundedRect(-9, -60, 18, 58, 6);
    this.cameras.main.startFollow(this.niko, true, 0.12, 0.12);

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
      const target = pickTile(this.chunks, ray.s, ray.t, this.viewerH, this.cutoff);
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
        this.setViewer(player.x, player.y, player.h ?? player.z * LEVEL_H);
        this.hasAuthoritativePosition = true;
        this.telemetryOrigin = null;
      } else if (player && this.prediction.idle()) {
        // Climbing and being lowered by a dig move Niko with no ack to reconcile against.
        const predicted = this.prediction.position;
        const drift = Math.hypot(player.x - predicted.x, player.y - predicted.y);
        if (drift > RESYNC_METRES || (player.h !== undefined && player.h !== predicted.h)) {
          this.prediction.reset(player);
          this.setViewer(player.x, player.y, player.h ?? player.z * LEVEL_H);
        }
      }
    });
    this.removeAckListener = this.options.client.onAck((position, sequence) => {
      if (!position) return;
      this.prediction.reconcile(position, sequence);
      this.setViewer(position.x, position.y, position.h ?? this.viewerH);
    });
    this.removeChunkListener = this.options.client.onChunk((chunk) => {
      if (!this.chunks.set(chunk)) return;
      this.drawChunk(chunk);
      // A changed border tile moves the cliff lines its neighbours draw along their edges.
      for (const [dx, dy] of [[-1, 0], [1, 0], [0, -1], [0, 1]]) {
        const neighbour = this.chunks.get(chunk.cx + dx, chunk.cy + dy);
        if (neighbour) this.drawChunk(neighbour);
      }
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
    const screen = toScreen(position.x, position.y, position.h ?? this.viewerH);
    this.niko.setPosition(screen.sx, screen.sy);
    this.niko.setDepth(nikoDepth(position.x, position.y));
    this.refreshView();
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
    this.lastView = null;
    this.refreshView();
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

  private layersFor(chunk: WorldChunk): ChunkLayers {
    const key = `${chunk.cx},${chunk.cy}`;
    let layers = this.chunkLayers.get(key);
    if (layers) return layers;
    const base = this.add.graphics();
    base.setDepth(baseDepth(chunk.cx, chunk.cy));
    layers = {
      base,
      rows: new Map(),
      grass: [],
      bbox: { minX: 0, minY: 0, maxX: 0, maxY: 0 },
    };
    this.chunkLayers.set(key, layers);
    return layers;
  }

  private clearChunkLayers(): void {
    for (const layers of this.chunkLayers.values()) {
      layers.base.destroy();
      for (const row of layers.rows.values()) row.destroy();
      for (const grass of layers.grass) grass.destroy();
    }
    this.chunkLayers.clear();
  }

  private setViewer(x: number, y: number, h: number): void {
    const tile = `${Math.floor(x)},${Math.floor(y)}`;
    const nextCutoff = cutoffH(this.chunks, x, y, h);
    if (h === this.viewerH && tile === this.nikoTile && nextCutoff === this.cutoff) return;
    this.viewerH = h;
    this.nikoTile = tile;
    this.cutoff = nextCutoff;
    this.redrawVisible();
  }

  private shade(base: number, h: number): number {
    const offset = Math.max(-20, Math.min(48, -h * 3));
    const red = Math.max(0, Math.min(255, (base >> 16) + offset));
    const green = Math.max(0, Math.min(255, ((base >> 8) & 0xff) + offset));
    const blue = Math.max(0, Math.min(255, (base & 0xff) + offset));
    return (red << 16) | (green << 8) | blue;
  }

  private tintGrass(h: number): number {
    const shaded = this.shade(0x6a9f4b, h);
    const ratio = (channel: number, base: number): number => Math.min(255, Math.round(channel * 255 / base));
    return (ratio((shaded >> 16) & 0xff, 0x6a) << 16) |
      (ratio((shaded >> 8) & 0xff, 0x9f) << 8) |
      ratio(shaded & 0xff, 0x4b);
  }

  private redrawAll(): void {
    for (const chunk of this.chunks.values()) this.drawChunk(chunk);
    this.updateCulling();
  }

  private drawChunk(chunk: WorldChunk): void {
    const size = this.chunks.size;
    const layers = this.layersFor(chunk);
    const orderedLevels = [...chunk.levels].sort((a, b) => a.z - b.z);
    layers.base.clear();
    for (const row of layers.rows.values()) row.clear();
    for (const sprite of layers.grass) sprite.destroy();
    layers.grass.length = 0;
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
        const h = chunk.ground_h[index] ?? 0;
        const materialId = chunk.surface_mat[index] ?? 0;
        const hasFloor = chunk.levels.some((level) => level.floor_h[index] !== NO_FLOOR);
        const groundShown = !hasFloor || h <= this.cutoff;
        if (groundShown) {
          this.drawTop(layers, usedRows, x, y, h, materialId, row, chunk);
          const eastH = this.chunks.groundH(x + 1.5, y + 0.5);
          const southH = this.chunks.groundH(x + 0.5, y + 1.5);
          if (eastH === undefined || h > eastH) {
            this.drawVertical(layers, usedRows, x + 1, y, x + 1, y + 1, eastH ?? h - 4, h, this.shade(this.materialColor(materialId), h), 0.66, row);
          }
          if (southH === undefined || h > southH) {
            this.drawVertical(layers, usedRows, x, y + 1, x + 1, y + 1, southH ?? h - 4, h, this.shade(this.materialColor(materialId), h), 0.82, row);
          }
        }

        for (const level of orderedLevels) {
          const floorH = level.floor_h[index];
          const flags = level.flags[index];
          if (floorH !== NO_FLOOR && floorH <= this.cutoff && (flags & LEVEL_VOID) === 0) {
            const floorMaterial = level.floor_mat[index] ?? 0;
            this.drawDiamond(layers, usedRows, x, y, floorH, this.shade(this.materialColor(floorMaterial), floorH), row);
            const southCell = this.chunks.levelCell(x, y + 1, level.z);
            const eastCell = this.chunks.levelCell(x + 1, y, level.z);
            if (southCell?.floor_h !== floorH) {
              this.drawVertical(layers, usedRows, x, y + 1, x + 1, y + 1, floorH - 1, floorH, this.shade(this.materialColor(floorMaterial), floorH), 0.82, row);
            }
            if (eastCell?.floor_h !== floorH) {
              this.drawVertical(layers, usedRows, x + 1, y, x + 1, y + 1, floorH - 1, floorH, this.shade(this.materialColor(floorMaterial), floorH), 0.66, row);
            }
          }

          const wallBase = this.chunks.wallBaseH(x, y, level.z, floorH);
          const wallN = level.wall_n[index];
          const wallW = level.wall_w[index];
          const edgeFlags = level.edge_flags[index];
          if (wallBase <= this.cutoff) {
            if (wallN && (edgeFlags & EDGE_N_DOORWAY) === 0) {
              const stub = this.isFrontWall("n", x, y, wallBase);
              this.drawWall(layers, usedRows, x, y, x + 1, y, wallBase, wallN, (edgeFlags & EDGE_N_WINDOW) !== 0, stub, row);
            }
            if (wallW && (edgeFlags & EDGE_W_DOORWAY) === 0) {
              const stub = this.isFrontWall("w", x, y, wallBase);
              this.drawWall(layers, usedRows, x, y, x, y + 1, wallBase, wallW, (edgeFlags & EDGE_W_WINDOW) !== 0, stub, row);
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
    layers.bbox = this.chunkBounds(chunk);
  }

  private targetGraphics(layers: ChunkLayers, usedRows: Set<number>, row: number): Phaser.GameObjects.Graphics {
    usedRows.add(row);
    let graphics = layers.rows.get(row);
    if (!graphics) {
      graphics = this.add.graphics();
      graphics.setDepth(rowDepth(row));
      layers.rows.set(row, graphics);
    }
    return graphics;
  }

  private graphicsFor(layers: ChunkLayers, usedRows: Set<number>, row: number, h: number): Phaser.GameObjects.Graphics {
    return h <= this.viewerH ? layers.base : this.targetGraphics(layers, usedRows, row);
  }

  private drawTop(layers: ChunkLayers, usedRows: Set<number>, x: number, y: number, h: number, materialId: number, row: number, chunk: WorldChunk): void {
    const material = this.materials?.get(materialId);
    if (material?.key === "grass") {
      const at = toScreen(x + 0.5, y + 0.5, h);
      this.drawDiamond(layers, usedRows, x, y, h, this.shade(this.materialColor(materialId), h), row);
      if (!this.inGrassView(at.sx, at.sy)) return;
      const sprite = this.add.image(at.sx, at.sy, "grass", this.grassFrame(x, y));
      sprite.setOrigin(0.5, 0.5).setTint(this.tintGrass(h));
      sprite.setDepth(h <= this.viewerH ? baseDepth(chunk.cx, chunk.cy) : rowDepth(row));
      layers.grass.push(sprite);
      if (h > this.viewerH) usedRows.add(row);
      return;
    }
    this.drawDiamond(layers, usedRows, x, y, h, this.shade(this.materialColor(materialId), h), row);
  }

  private grassFrame(x: number, y: number): number {
    let hash = Math.imul(x, 0x45d9f3b) ^ Math.imul(y, 0x119de1f3);
    hash = Math.imul(hash ^ (hash >>> 16), 0x45d9f3b);
    return (hash ^ (hash >>> 16)) & 3;
  }

  private drawDiamond(layers: ChunkLayers, usedRows: Set<number>, x: number, y: number, h: number, color: number, row: number): void {
    const top = toScreen(x, y, h);
    const right = toScreen(x + 1, y, h);
    const bottom = toScreen(x + 1, y + 1, h);
    const left = toScreen(x, y + 1, h);
    const graphics = this.graphicsFor(layers, usedRows, row, h);
    this.fillTriangle(graphics, top, right, bottom, color, 1);
    this.fillTriangle(graphics, top, bottom, left, color, 1);
  }

  private fillTriangle(
    graphics: Phaser.GameObjects.Graphics,
    a: { sx: number; sy: number },
    b: { sx: number; sy: number },
    c: { sx: number; sy: number },
    color: number,
    alpha: number,
  ): void {
    graphics.fillStyle(color, alpha);
    graphics.fillTriangle(a.sx, a.sy, b.sx, b.sy, c.sx, c.sy);
  }

  private drawVertical(
    layers: ChunkLayers,
    usedRows: Set<number>,
    x1: number,
    y1: number,
    x2: number,
    y2: number,
    h0: number,
    h1: number,
    color: number,
    light: number,
    row: number,
    alpha = 1,
  ): void {
    const lowerTop = Math.min(h1, this.viewerH);
    if (h0 < lowerTop) this.quad(layers.base, x1, y1, x2, y2, h0, lowerTop, this.scaleColor(color, light), alpha);
    const upperBottom = Math.max(h0, this.viewerH);
    if (upperBottom < h1) this.quad(this.targetGraphics(layers, usedRows, row), x1, y1, x2, y2, upperBottom, h1, this.scaleColor(color, light), alpha);
  }

  private quad(gfx: Phaser.GameObjects.Graphics, x1: number, y1: number, x2: number, y2: number, h0: number, h1: number, color: number, alpha: number): void {
    const a = toScreen(x1, y1, h0);
    const b = toScreen(x2, y2, h0);
    const c = toScreen(x2, y2, h1);
    const d = toScreen(x1, y1, h1);
    this.fillTriangle(gfx, a, b, c, color, alpha);
    this.fillTriangle(gfx, a, c, d, color, alpha);
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
    layers: ChunkLayers,
    usedRows: Set<number>,
    x1: number,
    y1: number,
    x2: number,
    y2: number,
    base: number,
    materialId: number,
    window: boolean,
    stub: boolean,
    row: number,
  ): void {
    const color = this.materialColor(materialId);
    const light = y1 === y2 ? 0.82 : 0.66;
    const cap = Math.min(base + MAX_WALL_H, this.cutoff);
    const top = stub ? Math.min(base + 1, cap) : cap;
    if (top <= base) return;
    if (stub || !window) {
      this.drawVertical(layers, usedRows, x1, y1, x2, y2, base, top, color, light, row);
    } else {
      this.drawVertical(layers, usedRows, x1, y1, x2, y2, base, Math.min(base + 2, top), color, light, row);
      if (top > base + 4) this.drawVertical(layers, usedRows, x1, y1, x2, y2, base + 4, top, color, light, row);
      if (top > base + 2) this.drawVertical(layers, usedRows, x1, y1, x2, y2, base + 2, Math.min(base + 4, top), 0xd8e6f0, light, row, 0.45);
    }
    if (!stub && top === base + MAX_WALL_H) {
      const a = toScreen(x1, y1, top);
      const b = toScreen(x2, y2, top);
      const graphics = this.graphicsFor(layers, usedRows, row, top);
      graphics.lineStyle(1, this.scaleColor(color, 1.12), 1).lineBetween(a.sx, a.sy, b.sx, b.sy);
    }
  }

  private chunkBounds(chunk: WorldChunk): ChunkLayers["bbox"] {
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
      minX: Math.min(...points.map((point) => point.sx)) - TILE_W,
      minY: Math.min(...points.map((point) => point.sy)) - TILE_H,
      maxX: Math.max(...points.map((point) => point.sx)) + TILE_W,
      maxY: Math.max(...points.map((point) => point.sy)) + TILE_H,
    };
  }

  private updateCulling(): void {
    const view = this.cameras.main.worldView;
    for (const layers of this.chunkLayers.values()) {
      const visible = layers.bbox.maxX >= view.x - TILE_W && layers.bbox.minX <= view.right + TILE_W &&
        layers.bbox.maxY >= view.y - TILE_H && layers.bbox.minY <= view.bottom + TILE_H;
      layers.base.setVisible(visible);
      for (const row of layers.rows.values()) row.setVisible(visible);
      for (const grass of layers.grass) grass.setVisible(visible);
    }
  }

  private refreshView(): void {
    const view = this.cameras.main.worldView;
    const previous = this.lastView;
    const moved = !previous || Math.hypot(view.centerX - previous.x, view.centerY - previous.y) > 96 ||
      view.width !== previous.width || view.height !== previous.height;
    if (moved) {
      this.lastView = { x: view.centerX, y: view.centerY, width: view.width, height: view.height };
      this.redrawVisible();
    } else {
      this.updateCulling();
    }
  }

  private redrawVisible(): void {
    for (const chunk of this.chunks.values()) {
      if (this.intersectsView(this.chunkBounds(chunk))) this.drawChunk(chunk);
    }
    this.updateCulling();
  }

  private inGrassView(x: number, y: number): boolean {
    const view = this.cameras.main.worldView;
    const margin = 256;
    return x + TILE_W / 2 >= view.x - margin && x - TILE_W / 2 <= view.right + margin &&
      y + TILE_H / 2 >= view.y - margin && y - TILE_H / 2 <= view.bottom + margin;
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
