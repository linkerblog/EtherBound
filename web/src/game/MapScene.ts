import Phaser from "phaser";
import { ClientPrediction } from "../net/prediction";
import { EMPTY_POSITION, type Direction, type Position } from "../net/protocol";
import { WebSocketClient } from "../net/client";
import { ChunkStore } from "../world/ChunkStore";
import { loadMaterials } from "../world/materials";
import type { components } from "../net/schema";
import { defaultZoom, loadZoom, saveZoom, stepZoom, WheelAccumulator, type ZoomLevel } from "./zoom";
type Material = components["schemas"]["MaterialResponse"];
type WorldChunk = components["schemas"]["ChunkResponse"];
import { EDGE_N_DOORWAY, EDGE_N_WINDOW, EDGE_W_DOORWAY, EDGE_W_WINDOW, LEVEL_H } from "../world/rules";

export const TILE_SIZE = 32;
const CLIFF_H = 2;
// Speed is averaged over this window so reconcile snaps do not read as spikes.
const TELEMETRY_INTERVAL = 0.2;

export type ContextTarget = { x: number; y: number; z: number; screenX: number; screenY: number };

/** Local avatar readout; `speed` is metres per second across the ground plane. */
export type Telemetry = Position & { speed: number };

type MapSceneOptions = {
  client: WebSocketClient;
  onContextMenu: (target: ContextTarget) => void;
  onTelemetry: (telemetry: Telemetry) => void;
  onZoom: (level: ZoomLevel) => void;
};

type ChunkLayers = { ground: Phaser.GameObjects.Graphics; levels: Phaser.GameObjects.Graphics };

export class MapScene extends Phaser.Scene {
  private readonly options: MapSceneOptions;
  private readonly chunks = new ChunkStore();
  private readonly chunkLayers = new Map<string, ChunkLayers>();
  private materials: Map<number, Material> | null = null;
  private niko!: Phaser.GameObjects.Arc;
  private prediction = new ClientPrediction(EMPTY_POSITION);
  private hasAuthoritativePosition = false;
  private viewerH = 0;
  private generation: number | undefined;
  private paused = false;
  private zoom: ZoomLevel | null = null;
  private readonly wheelAccumulator = new WheelAccumulator();
  private keys!: Record<"up" | "down" | "left" | "right", Phaser.Input.Keyboard.Key>;
  private lastDirection: Direction = { x: 0, y: 0 };
  private telemetryOrigin: { x: number; y: number } | null = null;
  private telemetryElapsed = 0;
  private removeStateListener?: () => void;
  private removeAckListener?: () => void;
  private removeChunkListener?: () => void;

  constructor(options: MapSceneOptions) {
    super("map");
    this.options = options;
  }

  create(): void {
    this.prediction.attachStore(this.chunks);
    void loadMaterials().then((materials) => {
      this.materials = materials;
      this.chunks.setMaterials(materials);
      this.redrawAll();
    });

    this.niko = this.add.circle(0, 0, 10, 0x2583ff);
    this.niko.setStrokeStyle(2, 0xffffff);
    this.niko.setDepth(1000);
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
      const x = Math.floor(pointer.worldX / TILE_SIZE);
      const y = Math.floor(pointer.worldY / TILE_SIZE);
      this.options.onContextMenu({
        x,
        y,
        z: this.chunks.topmostZ(x, y, this.viewerH),
        screenX: pointer.x,
        screenY: pointer.y,
      });
    });
    this.input.mouse?.disableContextMenu();
    this.applyZoom(loadZoom(defaultZoom(window.devicePixelRatio)));

    this.removeStateListener = this.options.client.onState((state) => {
      this.paused = state.paused;
      if (state.world) {
        if (state.genVersion !== undefined && this.generation !== undefined && state.genVersion !== this.generation) {
          this.chunks.clear();
          this.chunkLayers.forEach((layers) => { layers.ground.destroy(); layers.levels.destroy(); });
          this.chunkLayers.clear();
          this.hasAuthoritativePosition = false;
        }
        this.generation = state.genVersion;
        this.chunks.setWorldInfo(state.world.chunk_size, state.world.bounds);
        this.applyCameraBounds(state.world.bounds);
      }
      const player = state.actors.player ?? state.actors.niko ?? Object.values(state.actors)[0];
      if (player && !this.hasAuthoritativePosition) {
        this.prediction.reset(player);
        this.viewerH = player.h ?? player.z * 6;
        this.hasAuthoritativePosition = true;
        this.telemetryOrigin = null;
      }
    });
    this.removeAckListener = this.options.client.onAck((position, sequence) => {
      if (!position) return;
      const previousH = this.viewerH;
      this.prediction.reconcile(position, sequence);
      this.viewerH = position.h ?? previousH;
      if (this.viewerH !== previousH) this.redrawLevels();
    });
    this.removeChunkListener = this.options.client.onChunk((chunk) => {
      if (!this.chunks.set(chunk)) return;
      this.drawChunk(chunk);
    });
  }

  update(_: number, delta: number): void {
    const seconds = Math.min(delta / 1000, 0.1);
    const direction = this.readDirection();
    if (direction.x !== this.lastDirection.x || direction.y !== this.lastDirection.y) {
      const sequence = this.options.client.nextSequence();
      this.prediction.setDirection(direction, sequence);
      this.options.client.sendInput(direction, sequence);
      this.lastDirection = direction;
    }
    const position = this.prediction.step(seconds, this.paused);
    const previousH = this.viewerH;
    this.viewerH = position.h ?? this.viewerH;
    if (this.viewerH !== previousH) this.redrawLevels();
    this.niko.setPosition(position.x * TILE_SIZE, position.y * TILE_SIZE);
    this.sampleTelemetry(position, seconds);
  }

  shutdown(): void {
    this.removeStateListener?.();
    this.removeAckListener?.();
    this.removeChunkListener?.();
    this.chunkLayers.clear();
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
    return {
      x: Number(this.keys.right.isDown) - Number(this.keys.left.isDown),
      y: Number(this.keys.down.isDown) - Number(this.keys.up.isDown),
    };
  }

  private applyZoom(level: ZoomLevel): void {
    if (level === this.zoom) return;
    this.cameras.main.setZoom(level);
    this.zoom = level;
    saveZoom(level);
    this.options.onZoom(level);
  }

  private applyCameraBounds(bounds: number[]): void {
    if (bounds.length !== 4) return;
    const [minX, minY, maxX, maxY] = bounds;
    this.cameras.main.setBounds(
      minX * TILE_SIZE,
      minY * TILE_SIZE,
      (maxX - minX + 1) * TILE_SIZE,
      (maxY - minY + 1) * TILE_SIZE,
    );
  }

  private materialColor(id: number): number {
    const raw = this.materials?.get(id)?.color;
    if (!raw) return id === 0 ? 0x426b4d : 0x5a7450;
    return parseInt(raw.slice(1), 16);
  }

  private layersFor(chunk: WorldChunk): ChunkLayers {
    const key = `${chunk.cx},${chunk.cy}`;
    let layers = this.chunkLayers.get(key);
    if (!layers) {
      layers = { ground: this.add.graphics(), levels: this.add.graphics() };
      this.chunkLayers.set(key, layers);
    }
    return layers;
  }

  private shade(base: number, h: number): number {
    const shade = Math.max(-20, Math.min(48, -h * 3));
    const red = Math.max(0, Math.min(255, (base >> 16) + shade));
    const green = Math.max(0, Math.min(255, ((base >> 8) & 0xff) + shade));
    const blue = Math.max(0, Math.min(255, (base & 0xff) + shade));
    return (red << 16) | (green << 8) | blue;
  }

  private redrawAll(): void {
    for (const chunk of this.chunks.values()) this.drawChunk(chunk);
  }

  private redrawLevels(): void {
    for (const chunk of this.chunks.values()) {
      if (chunk.levels.length > 0) this.drawChunkLevels(chunk);
    }
  }

  private drawChunk(chunk: WorldChunk): void {
    const size = this.chunks.size;
    const { ground } = this.layersFor(chunk);
    ground.clear();
    const originX = chunk.cx * size * TILE_SIZE;
    const originY = chunk.cy * size * TILE_SIZE;
    for (let y = 0; y < size; y += 1) {
      for (let x = 0; x < size; x += 1) {
        const index = y * size + x;
        const h = chunk.ground_h[index] ?? 0;
        ground.fillStyle(this.shade(this.materialColor(chunk.surface_mat[index] ?? 0), h), 1);
        ground.fillRect(originX + x * TILE_SIZE, originY + y * TILE_SIZE, TILE_SIZE, TILE_SIZE);
      }
    }
    // Cliff lines where neighbouring tiles differ by a metre or more.
    ground.lineStyle(1, 0x0c0f12, 0.8);
    for (let y = 0; y < size; y += 1) {
      for (let x = 0; x < size; x += 1) {
        const h = chunk.ground_h[y * size + x] ?? 0;
        const east = x + 1 < size ? (chunk.ground_h[y * size + x + 1] ?? h) : (this.chunks.groundH(chunk.cx * size + x + 1 + 0.5, chunk.cy * size + y + 0.5) ?? h);
        const south = y + 1 < size ? (chunk.ground_h[(y + 1) * size + x] ?? h) : (this.chunks.groundH(chunk.cx * size + x + 0.5, chunk.cy * size + y + 1 + 0.5) ?? h);
        const px = originX + x * TILE_SIZE;
        const py = originY + y * TILE_SIZE;
        if (Math.abs(east - h) >= CLIFF_H) ground.lineBetween(px + TILE_SIZE, py, px + TILE_SIZE, py + TILE_SIZE);
        if (Math.abs(south - h) >= CLIFF_H) ground.lineBetween(px, py + TILE_SIZE, px + TILE_SIZE, py + TILE_SIZE);
      }
    }
    this.drawChunkLevels(chunk);
  }

  /** Cutaway: floors and walls more than 2 m above Niko stay hidden. */
  private isVisibleH(h: number): boolean {
    return h <= this.viewerH + 4;
  }

  private drawChunkLevels(chunk: WorldChunk): void {
    const size = this.chunks.size;
    const { levels } = this.layersFor(chunk);
    levels.clear();
    const originX = chunk.cx * size * TILE_SIZE;
    const originY = chunk.cy * size * TILE_SIZE;
    for (const level of chunk.levels) {
      for (let y = 0; y < size; y += 1) {
        for (let x = 0; x < size; x += 1) {
          const index = y * size + x;
          const floorH = level.floor_h[index];
          const px = originX + x * TILE_SIZE;
          const py = originY + y * TILE_SIZE;
          if (floorH === -32768) {
            const wallN = level.wall_n[index];
            const wallW = level.wall_w[index];
            if (wallN || wallW) {
              const wallColor = this.materialColor(wallN || wallW);
              const flags = level.edge_flags[index];
              if (wallN) this.drawEdge(levels, px, py, px + TILE_SIZE, py, flags & EDGE_N_DOORWAY, flags & EDGE_N_WINDOW, wallColor);
              if (wallW) this.drawEdge(levels, px, py, px, py + TILE_SIZE, flags & EDGE_W_DOORWAY, flags & EDGE_W_WINDOW, wallColor);
            }
            continue;
          }
          if (this.isVisibleH(floorH)) {
            levels.fillStyle(this.shade(this.materialColor(level.floor_mat[index] ?? 0), floorH), 1);
            levels.fillRect(px, py, TILE_SIZE, TILE_SIZE);
          }
          const wallColor = this.materialColor(level.wall_n[index] || level.wall_w[index]);
          const wallN = level.wall_n[index];
          const wallW = level.wall_w[index];
          const flags = level.edge_flags[index];
          if (wallN && this.isVisibleH(floorH)) {
            this.drawEdge(levels, px, py, px + TILE_SIZE, py, flags & EDGE_N_DOORWAY, flags & EDGE_N_WINDOW, wallColor);
          }
          if (wallW && this.isVisibleH(floorH)) {
            this.drawEdge(levels, px, py, px, py + TILE_SIZE, flags & EDGE_W_DOORWAY, flags & EDGE_W_WINDOW, wallColor);
          }
        }
      }
    }
  }

  private drawEdge(
    gfx: Phaser.GameObjects.Graphics,
    x1: number,
    y1: number,
    x2: number,
    y2: number,
    doorway: number,
    window: number,
    color: number,
  ): void {
    if (doorway) return;
    if (window) {
      gfx.lineStyle(1, 0xd8e6f0, 0.9);
      if (y1 === y2) {
        for (let t = 0; t < TILE_SIZE; t += 6) gfx.lineBetween(x1 + t, y1, Math.min(x1 + t + 3, x2), y1);
      } else {
        for (let t = 0; t < TILE_SIZE; t += 6) gfx.lineBetween(x1, y1 + t, x1, Math.min(y1 + t + 3, y2));
      }
      return;
    }
    gfx.lineStyle(3, this.shade(color, LEVEL_H), 1);
    gfx.lineBetween(x1, y1, x2, y2);
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
