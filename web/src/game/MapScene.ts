import Phaser from "phaser";
import { ClientPrediction } from "../net/prediction";
import { EMPTY_POSITION, type Direction, type Position } from "../net/protocol";
import { WebSocketClient } from "../net/client";
import { ChunkStore } from "../world/ChunkStore";
import { loadMaterials } from "../world/materials";
import { loadObjectKinds } from "../world/objects";
import type { components } from "../net/schema";
import { defaultZoom, loadZoom, saveZoom, stepZoom, WheelAccumulator, type ZoomLevel } from "./zoom";
import { H_PX, TILE_H, TILE_W, keysToWorld, nikoDepth, rowDepth, screenToRay, toScreen } from "./iso";
import { cutoffH } from "../world/cutaway";
import { pickTile } from "../world/pick";
import { isCovered, StructureTracker } from "../world/occlusion";
import { cutoffChunkKeys, changedChunkKeys, materialChunkKeys, structureChunkKeys, tileChunkKeys, viewerHeightChunkKeys, type ChunkBounds } from "./dirty";
import { TERRAIN_SHEETS, TERRAIN_SIDE_SHEETS } from "./terrainSheets";
import { OBJECT_SHEETS } from "./objectSheets";
import { PhysicsAnimator } from "./physics";
import { ExtrasLayer } from "./extras";
import { buildTerrainAtlas } from "./terrainAtlas";
import { ChunkRenderer } from "./chunkRenderer";
type Material = components["schemas"]["MaterialResponse"];
type ObjectKind = components["schemas"]["ObjectKindResponse"];

// Niko's actor id; the client never guesses the player from a map's iteration order.
const PLAYER_ID = "niko";
// Speed is averaged over this window so reconcile snaps do not read as spikes.
const TELEMETRY_INTERVAL = 0.2;
// The server moves Niko one 1/20 s step per input, so a held key repeats at 20 Hz.
const INPUT_INTERVAL = 0.05;
// Below this the prediction and the server agree; above it, an idle Niko is resynced.
const RESYNC_METRES = 0.05;

export type ContextTarget = { x: number; y: number; z: number; screenX: number; screenY: number };

/** Niko's own tile plus the page point his feet are drawn at, used to anchor a menu on him. */
export type PlayerAnchor = ContextTarget;

/** Local avatar readout; `speed` is metres per second across the ground plane. */
export type Telemetry = Position & { speed: number };

type MapSceneOptions = {
  client: WebSocketClient;
  onContextMenu: (target: ContextTarget) => void;
  onTelemetry: (telemetry: Telemetry) => void;
  onZoom: (level: ZoomLevel) => void;
};

export class MapScene extends Phaser.Scene {
  private readonly options: MapSceneOptions;
  private readonly chunks = new ChunkStore();
  private chunkRenderer!: ChunkRenderer;
  private extrasLayer!: ExtrasLayer;
  private physicsAnimator!: PhysicsAnimator;
  private materials: Map<number, Material> | null = null;
  private objectKinds = new Map<string, ObjectKind>();
  private niko!: Phaser.GameObjects.Graphics;
  private nikoGhost!: Phaser.GameObjects.Graphics;
  private tileMarker!: Phaser.GameObjects.Graphics;
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
  private readonly structureTracker = new StructureTracker();
  private telemetryOrigin: { x: number; y: number } | null = null;
  private telemetryElapsed = 0;
  private removeStateListener?: () => void;
  private removeSnapshotListener?: () => void;
  private removeAckListener?: () => void;
  private removeChunkListener?: () => void;
  private removeResultListener?: () => void;

  constructor(options: MapSceneOptions) {
    super("map");
    this.options = options;
  }

  preload(): void {
    this.load.on("loaderror", (file: Phaser.Loader.File) => {
      console.error(`game sheet failed: ${file.key} ${file.url}`);
    });
    for (const [key, sheet] of Object.entries(TERRAIN_SHEETS)) {
      this.load.spritesheet(`sheet:${key}`, sheet, { frameWidth: TILE_W, frameHeight: TILE_H });
    }
    for (const [key, sheet] of Object.entries(TERRAIN_SIDE_SHEETS)) {
      this.load.image(`side:${key}`, sheet);
    }
    for (const [key, sheet] of Object.entries(OBJECT_SHEETS)) {
      this.load.image(`object:${key}`, sheet);
    }
  }

  create(): void {
    const atlas = buildTerrainAtlas(this.textures);
    this.physicsAnimator = new PhysicsAnimator(
      this,
      (keys) => this.markDirty(keys),
      () => [...this.chunks.values()].map((chunk) => `${chunk.cx},${chunk.cy}`),
    );
    const thisScene = this;
    this.chunkRenderer = new ChunkRenderer(this, this.chunks, atlas, {
      get viewerH() { return thisScene.viewerH; },
      get nikoTile() { return thisScene.nikoTile; },
      get animatedObjectIds() { return thisScene.physicsAnimator.animatedObjectIds; },
      get materials() { return thisScene.materials; },
      get objectKinds() { return thisScene.objectKinds; },
      cutoffAt: (x, y) => this.cutoffAt(x, y),
    });
    this.extrasLayer = new ExtrasLayer(this, () => this.viewerH, (x, y) => this.cutoffAt(x, y));
    this.prediction.attachStore(this.chunks);
    void loadMaterials().then((materials) => {
      this.materials = materials;
      this.chunks.setMaterials(materials);
      this.markDirty(materialChunkKeys(this.dirtyChunkInfo()));
    });
    void loadObjectKinds().then((kinds) => {
      this.objectKinds = kinds;
      this.chunks.setObjectKinds(kinds);
      this.markDirty([...this.chunks.values()].map((chunk) => `${chunk.cx},${chunk.cy}`));
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
    this.tileMarker = this.add.graphics().setVisible(false);

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
      this.physicsAnimator.clear();
      this.clearChunkLayers();
      this.chunks.clear();
      this.structureTracker.clear();
      this.nikoGhost.setVisible(false);
      this.markTile(null);
      this.extrasLayer.clear();
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
      const player = state.actors[PLAYER_ID];
      if (player) this.prediction.setLoadKg(player.load_kg ?? 0);
      this.extrasLayer.sync(state.actors, performance.now());
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
      if (this.chunkRenderer.hasLayers(key)) this.chunkRenderer.updateChunkBounds(chunk);
      else this.chunkRenderer.layersFor(chunk);
      if (this.structureTracker.touches(chunk.cx, chunk.cy, this.chunks.size)) {
        const bounds = this.structureTracker.rebuild(this.chunks);
        if (bounds.length > 0) {
          this.markDirty(structureChunkKeys(this.dirtyChunkInfo(), this.chunks.size, bounds));
        }
      }
      this.markDirty(changedChunkKeys(this.dirtyChunkInfo(), `${chunk.cx},${chunk.cy}`));
    });
    this.removeResultListener = this.options.client.onResult((result) => this.physicsAnimator.animate(result));
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
    this.physicsAnimator.update(performance.now());
    this.extrasLayer.update(performance.now());
    this.sampleTelemetry(position, seconds);
  }

  /** Niko's tile and the page point his feet are drawn at, for a menu centred on him. */
  playerAnchor(): PlayerAnchor | null {
    if (!this.niko) return null;
    const position = this.prediction.position;
    const camera = this.cameras.main;
    const bounds = this.game.canvas.getBoundingClientRect();
    return {
      x: Math.floor(position.x),
      y: Math.floor(position.y),
      z: position.z,
      screenX: (this.niko.x - camera.worldView.x) * camera.zoom + bounds.left,
      screenY: (this.niko.y - camera.worldView.y) * camera.zoom + bounds.top,
    };
  }

  /** Outlines the top of the tile the radial menu points at; null clears it. */
  markTile(tile: { x: number; y: number; h: number } | null): void {
    if (!this.tileMarker) return;
    this.tileMarker.clear();
    if (!tile) {
      this.tileMarker.setVisible(false);
      return;
    }
    const corners = [
      toScreen(tile.x, tile.y, tile.h),
      toScreen(tile.x + 1, tile.y, tile.h),
      toScreen(tile.x + 1, tile.y + 1, tile.h),
      toScreen(tile.x, tile.y + 1, tile.h),
    ];
    this.tileMarker.lineStyle(1, 0x57c7ff, 1);
    this.tileMarker.strokePoints(corners.map((corner) => ({ x: corner.sx, y: corner.sy })), true);
    // Over the tile's own row, so it shows on a raised top, and under Niko and actors on that row.
    this.tileMarker.setDepth(rowDepth(tile.x + tile.y) + 0.5).setVisible(true);
  }

  /** Gates WASD and the zoom keys while another view covers the world. */
  setKeyboardEnabled(enabled: boolean): void {
    const keyboard = this.input?.keyboard;
    if (!keyboard) return;
    keyboard.enabled = enabled;
    // A key held while the view changes never sees its keyup, so it would keep Niko walking.
    if (!enabled) keyboard.resetKeys();
  }

  shutdown(): void {
    this.removeStateListener?.();
    this.removeSnapshotListener?.();
    this.removeAckListener?.();
    this.removeChunkListener?.();
    this.removeResultListener?.();
    this.physicsAnimator.clear();
    this.options.client.setBeforeCommand(null);
    this.clearChunkLayers();
    this.extrasLayer.clear();
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

  private clearChunkLayers(): void {
    this.chunkRenderer.clearChunkLayers();
    this.dirtyChunks.clear();
  }

  private setViewer(x: number, y: number, h: number): void {
    const tile = `${Math.floor(x)},${Math.floor(y)}`;
    const nextCutoff = cutoffH(this.chunks, x, y, h);
    const oldH = this.viewerH;
    const oldTile = this.nikoTile;
    const oldCutoff = this.cutoff;
    this.viewerH = h;
    this.nikoTile = tile;
    this.cutoff = nextCutoff;
    const structureBounds = this.structureTracker.update(this.chunks, x, y, h, nextCutoff);
    if (structureBounds.length > 0) {
      this.markDirty(structureChunkKeys(this.dirtyChunkInfo(), this.chunks.size, structureBounds));
    }
    if (oldH === h && oldTile === tile && oldCutoff === nextCutoff) return;
    const chunks = this.dirtyChunkInfo();
    if (oldH !== h) this.markDirty(viewerHeightChunkKeys(chunks, oldH, h));
    if (oldCutoff !== nextCutoff) this.markDirty(cutoffChunkKeys(chunks));
    if (oldTile !== tile) {
      const [oldX, oldY] = oldTile ? oldTile.split(",").map(Number) : [x, y];
      this.markDirty(tileChunkKeys(chunks, this.chunks.size, { x: oldX, y: oldY }, { x: Math.floor(x), y: Math.floor(y) }));
    }
  }

  private cutoffAt(x: number, y: number): number {
    return this.structureTracker.cutoffAt(x, y, this.cutoff);
  }

  private updateCulling(): void {
    this.chunkRenderer.cull((bbox) => this.intersectsView(bbox));
  }

  private dirtyChunkInfo() {
    return this.chunkRenderer.dirtyInfo((cx, cy) => (this.chunks.get(cx, cy)?.levels.length ?? 0) > 0);
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
      const bounds = this.chunkRenderer.boundsOf(key)!;
      const x = (cx + 0.5) * this.chunks.size;
      const y = (cy + 0.5) * this.chunks.size;
      return { key, bounds, distance: (x - viewerX) ** 2 + (y - viewerY) ** 2 };
    }).sort((a, b) => a.distance - b.distance);
    for (const { key, bounds } of pending) {
      if (!this.intersectsView(bounds)) continue;
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
    this.chunkRenderer.drawChunk(chunk);
  }

  private intersectsView(bbox: ChunkBounds): boolean {
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
  onScene?: (scene: MapScene) => void,
): Phaser.Game {
  const scene = new MapScene({ client, onContextMenu, onTelemetry, onZoom });
  onScene?.(scene);
  return new Phaser.Game({
    type: Phaser.AUTO,
    parent,
    width: window.innerWidth,
    height: window.innerHeight,
    backgroundColor: "#07080a",
    pixelArt: true,
    render: { antialias: false, roundPixels: true },
    scale: { mode: Phaser.Scale.RESIZE, autoCenter: Phaser.Scale.CENTER_BOTH },
    scene,
  });
}
