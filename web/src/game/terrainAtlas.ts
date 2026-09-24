import Phaser from "phaser";
import { H_PX, TILE_H, TILE_W } from "./iso";
import { OBJECT_SHEETS } from "./objectSheets";
import { OBJECT_SPRITES } from "./objectSprites";
import { shearSideCell, shearWallCell, wallEndMask, wallMask, wallPostMask, wallTopMask, diamondMask, faceMask, type SideCell, type TileMask } from "./tileMasks";
import { TERRAIN_SHEETS, TERRAIN_SIDE_SHEETS } from "./terrainSheets";

export type ObjectFrame = { name: string; anchorX: number; anchorY: number };
export type ObjectFrameSet = { full: ObjectFrame; lower: Map<number, ObjectFrame>; upper: Map<number, ObjectFrame> };
export type PendingObjectFrame = {
  name: string;
  source: HTMLImageElement;
  sourceY: number;
  width: number;
  height: number;
  anchorX: number;
  anchorY: number;
};

export function buildTerrainAtlas(textures: Phaser.Textures.TextureManager) {
  const maskOffsets = new Map<string, { x: number; y: number }>();
  const spriteKeys = new Set<string>();
  const sideKeys = new Set<string>();
  const wallKeys = new Set<string>();
  const objectFrames = new Map<string, ObjectFrameSet>();
  const objectSpriteKeys = new Set<string>();
  const masks: Array<[string, TileMask]> = [["top", diamondMask()]];
  for (const side of ["s", "e"] as const) {
    for (const units of [1, 2, 4, 8] as const) masks.push([`face${side.toUpperCase()}${units}`, faceMask(side, units)]);
  }
  for (const edge of ["n", "w"] as const) {
    for (const units of [1, 2, 4] as const) masks.push([`wall${edge.toUpperCase()}${units}`, wallMask(edge, units)]);
    masks.push([`wallTop${edge.toUpperCase()}`, wallTopMask(edge)]);
  }
  for (const units of [1, 2, 4] as const) masks.push([`wallEndE${units}`, wallEndMask("e", units)]);
  for (const units of [1, 2, 4] as const) masks.push([`wallEndS${units}`, wallEndMask("s", units)]);
  masks.push(["wallPost", wallPostMask()]);

  const spriteSheets = Object.entries(TERRAIN_SHEETS).flatMap(([key]) => {
    const source = textures.get(`sheet:${key}`).getSourceImage() as HTMLImageElement;
    if (source.width !== 256 || source.height !== 32) {
      console.warn(`Skipping ${key} terrain sprite sheet: expected 256x32, received ${source.width}x${source.height}`);
      return [];
    }
    return [[key, source] as const];
  });

  const sideFrames: Array<[string, SideCell]> = [];
  const wallFrames: Array<[string, SideCell]> = [];
  const pendingObjectFrames: PendingObjectFrame[] = [];
  let sideWidth = 0;
  let sideHeight = 0;
  let wallWidth = 0;
  let wallHeight = 0;
  let objectWidth = 0;
  let objectHeight = 0;
  for (const [key, file] of Object.entries(TERRAIN_SIDE_SHEETS)) {
    const texture = textures.get(`side:${key}`);
    const source = texture.getSourceImage() as HTMLImageElement | undefined;
    if (!source || (source.width === 0 && source.height === 0)) {
      console.warn(`Skipping ${key} terrain side sheet: ${file} not loaded`);
      continue;
    }
    if (source.width !== 128 || source.height !== 32) {
      console.warn(`Skipping ${key} terrain side sheet: expected 128x32, received ${source.width}x${source.height}`);
      continue;
    }
    const sheet = { width: source.width, height: source.height, data: readPixels(source) };
    if (sideWidth + 16 * 32 > 4096) {
      console.warn(`Skipping ${key} terrain side sheet: atlas side band would exceed 4096 px`);
    } else {
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
      sideKeys.add(key);
    }
    if (wallWidth + 16 * 32 > 4096) {
      console.warn(`Skipping ${key} terrain wall sheet: atlas wall band would exceed 4096 px`);
    } else {
      for (const edge of ["n", "w"] as const) {
        for (const part of ["cap", "fill"] as const) {
          for (const variant of [0, 1, 2, 3] as const) {
            const cell = shearWallCell(sheet, edge, part, variant);
            wallFrames.push([`wall:${key}:${edge}:${part}:${variant}`, cell]);
            wallHeight = Math.max(wallHeight, cell.height);
          }
        }
      }
      wallWidth += 16 * 32;
      wallKeys.add(key);
    }
  }

  for (const [kind, file] of Object.entries(OBJECT_SHEETS)) {
    const texture = textures.get(`object:${kind}`);
    const source = texture?.getSourceImage() as HTMLImageElement | undefined;
    const sprite = OBJECT_SPRITES[kind];
    if (!source || !sprite || source.width === 0 || source.height === 0) {
      console.warn(`Skipping ${kind} object sprite: ${file} not loaded`);
      continue;
    }
    const frames: PendingObjectFrame[] = [{
      name: `object:${kind}:full`, source, sourceY: 0, width: source.width, height: source.height,
      anchorX: sprite.anchorX, anchorY: sprite.anchorY,
    }];
    for (let relativeH = 1; relativeH <= 4; relativeH += 1) {
      const cut = sprite.anchorY - relativeH * H_PX;
      if (cut <= 0 || cut >= source.height) continue;
      frames.push(
        { name: `object:${kind}:upper:${relativeH}`, source, sourceY: 0, width: source.width, height: cut, anchorX: sprite.anchorX, anchorY: sprite.anchorY },
        { name: `object:${kind}:lower:${relativeH}`, source, sourceY: cut, width: source.width, height: source.height - cut, anchorX: sprite.anchorX, anchorY: sprite.anchorY - cut },
      );
    }
    const width = frames.reduce((sum, frame) => sum + frame.width, 0);
    if (objectWidth + width > 4096) {
      console.warn(`Skipping ${kind} object sprite: atlas object band would exceed 4096 px`);
      continue;
    }
    pendingObjectFrames.push(...frames);
    objectWidth += width;
    objectHeight = Math.max(objectHeight, ...frames.map((frame) => frame.height));
    objectSpriteKeys.add(kind);
  }

  const slotCount = spriteSheets.length * 4 + masks.length;
  const objectY = 160 + sideHeight + wallHeight;
  const atlas = textures.createCanvas(
    "terrain",
    Math.max(slotCount * TILE_W, sideWidth, wallWidth, objectWidth, 12),
    objectY + objectHeight + 6,
  );
  if (!atlas) throw new Error("Failed to create terrain atlas");
  const context = atlas.context;
  let spriteSlot = 0;
  for (const [key, source] of spriteSheets) {
    context.drawImage(source, spriteSlot * TILE_W, 0);
    for (let frame = 0; frame < 4; frame += 1) {
      atlas.add(`${key}${frame}`, 0, (spriteSlot + frame) * TILE_W, 0, TILE_W, TILE_H);
    }
    spriteKeys.add(key);
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
    maskOffsets.set(name, { x: mask.offsetX, y: mask.offsetY });
  });
  let sideX = 0;
  for (const [name, cell] of sideFrames) {
    const image = context.createImageData(cell.width, cell.height);
    image.data.set(cell.rgba);
    context.putImageData(image, sideX, 160);
    atlas.add(name, 0, sideX, 160, cell.width, cell.height);
    maskOffsets.set(name, { x: cell.offsetX, y: cell.offsetY });
    sideX += cell.width;
  }
  let wallX = 0;
  const wallY = 160 + sideHeight;
  for (const [name, cell] of wallFrames) {
    const image = context.createImageData(cell.width, cell.height);
    image.data.set(cell.rgba);
    context.putImageData(image, wallX, wallY);
    atlas.add(name, 0, wallX, wallY, cell.width, cell.height);
    maskOffsets.set(name, { x: cell.offsetX, y: cell.offsetY });
    wallX += cell.width;
  }
  let objectX = 0;
  for (const frame of pendingObjectFrames) {
    context.drawImage(frame.source, 0, frame.sourceY, frame.width, frame.height, objectX, objectY, frame.width, frame.height);
    atlas.add(frame.name, 0, objectX, objectY, frame.width, frame.height);
    registerObjectFrame(objectFrames, frame);
    objectX += frame.width;
  }
  const pile = context.createImageData(12, 6);
  for (let y = 0; y < 6; y += 1) {
    for (let x = 0; x < 12; x += 1) {
      if (Math.abs(x - 5.5) / 6 + Math.abs(y - 2.5) / 3 > 1) continue;
      const at = (y * 12 + x) * 4;
      pile.data[at] = 255;
      pile.data[at + 1] = 255;
      pile.data[at + 2] = 255;
      pile.data[at + 3] = 255;
    }
  }
  context.putImageData(pile, 0, objectY + objectHeight);
  atlas.add("object:pile", 0, 0, objectY + objectHeight, 12, 6);
  atlas.refresh();
  return { maskOffsets, spriteKeys, sideKeys, wallKeys, objectFrames, objectSpriteKeys };
}

export function registerObjectFrame(objectFrames: Map<string, ObjectFrameSet>, frame: PendingObjectFrame): void {
  const info = { name: frame.name, anchorX: frame.anchorX, anchorY: frame.anchorY };
  const [, kind, part, heightText] = frame.name.split(":");
  let set = objectFrames.get(kind!);
  if (!set) {
    set = { full: info, lower: new Map(), upper: new Map() };
    objectFrames.set(kind!, set);
  }
  if (part === "upper") set.upper.set(Number(heightText), info);
  else if (part === "lower") set.lower.set(Number(heightText), info);
  else set.full = info;
}

/** Reads a loaded sheet's pixels so the pure shearing helper can run outside a Phaser texture. */
export function readPixels(source: HTMLImageElement): Uint8ClampedArray {
  const canvas = document.createElement("canvas");
  canvas.width = source.width;
  canvas.height = source.height;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Failed to read terrain side sheet pixels");
  context.drawImage(source, 0, 0);
  return context.getImageData(0, 0, source.width, source.height).data;
}
