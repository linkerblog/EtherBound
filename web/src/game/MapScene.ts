import Phaser from "phaser";
import { ClientPrediction } from "../net/prediction";
import { EMPTY_POSITION, type Direction } from "../net/protocol";
import { WebSocketClient } from "../net/client";

export const TILE_SIZE = 32;

export type ContextTarget = { x: number; y: number; z: number; screenX: number; screenY: number };

type MapSceneOptions = {
  client: WebSocketClient;
  onContextMenu: (target: ContextTarget) => void;
};

export class MapScene extends Phaser.Scene {
  private readonly options: MapSceneOptions;
  private niko!: Phaser.GameObjects.Arc;
  private prediction = new ClientPrediction(EMPTY_POSITION);
  private hasAuthoritativePosition = false;
  private paused = false;
  private keys!: Record<"up" | "down" | "left" | "right", Phaser.Input.Keyboard.Key>;
  private lastDirection: Direction = { x: 0, y: 0 };
  private removeStateListener?: () => void;
  private removeAckListener?: () => void;

  constructor(options: MapSceneOptions) {
    super("map");
    this.options = options;
  }

  create(): void {
    this.drawPlaceholderMap();
    this.niko = this.add.circle(32 * TILE_SIZE + TILE_SIZE / 2, 32 * TILE_SIZE + TILE_SIZE / 2, 10, 0x2583ff);
    this.niko.setStrokeStyle(2, 0xffffff);
    this.cameras.main.setBounds(0, 0, 64 * TILE_SIZE, 64 * TILE_SIZE);
    this.cameras.main.startFollow(this.niko, true, 0.12, 0.12);
    this.cameras.main.setZoom(this.integerZoom());

    this.keys = {
      up: this.input.keyboard!.addKey(Phaser.Input.Keyboard.KeyCodes.W),
      down: this.input.keyboard!.addKey(Phaser.Input.Keyboard.KeyCodes.S),
      left: this.input.keyboard!.addKey(Phaser.Input.Keyboard.KeyCodes.A),
      right: this.input.keyboard!.addKey(Phaser.Input.Keyboard.KeyCodes.D),
    };

    this.input.on("pointerdown", (pointer: Phaser.Input.Pointer) => {
      if (!pointer.rightButtonDown()) return;
      this.options.onContextMenu({
        x: Math.floor(pointer.worldX / TILE_SIZE),
        y: Math.floor(pointer.worldY / TILE_SIZE),
        z: 0,
        screenX: pointer.x,
        screenY: pointer.y,
      });
    });
    this.input.mouse?.disableContextMenu();

    this.removeStateListener = this.options.client.onState((state) => {
      this.paused = state.paused;
      const player = state.actors.player ?? state.actors.niko ?? Object.values(state.actors)[0];
      if (player && !this.hasAuthoritativePosition) {
        this.prediction.reset(player);
        this.hasAuthoritativePosition = true;
      }
    });
    this.removeAckListener = this.options.client.onAck((position, sequence) => {
      if (position) this.prediction.reconcile(position, sequence);
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
    this.niko.setPosition(position.x * TILE_SIZE + TILE_SIZE / 2, position.y * TILE_SIZE + TILE_SIZE / 2);
  }

  shutdown(): void {
    this.removeStateListener?.();
    this.removeAckListener?.();
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

  private drawPlaceholderMap(): void {
    const graphics = this.add.graphics();
    graphics.fillStyle(0x11151b);
    graphics.fillRect(0, 0, 64 * TILE_SIZE, 64 * TILE_SIZE);
    for (let y = 0; y < 64; y += 1) {
      for (let x = 0; x < 64; x += 1) {
        const isRoad = x > 27 && x < 37 || y > 27 && y < 37;
        const color = isRoad ? 0x252a31 : (x + y) % 2 === 0 ? 0x17221f : 0x14201d;
        graphics.fillStyle(color, 1);
        graphics.fillRect(x * TILE_SIZE + 1, y * TILE_SIZE + 1, TILE_SIZE - 2, TILE_SIZE - 2);
      }
    }
    graphics.lineStyle(1, 0x2d4a45, 0.65);
    for (let i = 0; i <= 64; i += 1) {
      graphics.lineBetween(i * TILE_SIZE, 0, i * TILE_SIZE, 64 * TILE_SIZE);
      graphics.lineBetween(0, i * TILE_SIZE, 64 * TILE_SIZE, i * TILE_SIZE);
    }
  }

  private integerZoom(): number {
    return Math.max(2, Math.min(4, Math.floor(window.devicePixelRatio || 2)));
  }
}

export function createGame(parent: HTMLElement, client: WebSocketClient, onContextMenu: (target: ContextTarget) => void): Phaser.Game {
  return new Phaser.Game({
    type: Phaser.AUTO,
    parent,
    width: window.innerWidth,
    height: window.innerHeight,
    backgroundColor: "#07080a",
    pixelArt: true,
    render: { antialias: false, roundPixels: true },
    scale: { mode: Phaser.Scale.RESIZE, autoCenter: Phaser.Scale.CENTER_BOTH },
    scene: new MapScene({ client, onContextMenu }),
  });
}
