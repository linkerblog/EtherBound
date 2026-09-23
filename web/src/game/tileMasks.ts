export type TileMask = {
  width: number;
  height: number;
  offsetX: number;
  offsetY: number;
  alpha: Uint8Array;
};

type Pixel = { x: number; y: number };

function opaqueDiamondPixel(x: number, y: number): boolean {
  return Math.abs(x - 31.5) <= 2 * Math.min(y, 31 - y) + 0.5;
}

function packPixels(pixels: Pixel[]): TileMask {
  const minX = Math.min(...pixels.map((pixel) => pixel.x));
  const minY = Math.min(...pixels.map((pixel) => pixel.y));
  const maxX = Math.max(...pixels.map((pixel) => pixel.x));
  const maxY = Math.max(...pixels.map((pixel) => pixel.y));
  const width = maxX - minX + 1;
  const height = maxY - minY + 1;
  const alpha = new Uint8Array(width * height);
  for (const pixel of pixels) alpha[(pixel.y - minY) * width + pixel.x - minX] = 255;
  return { width, height, offsetX: minX, offsetY: minY, alpha };
}

function topRow(x: number): number {
  for (let y = 0; y < 32; y += 1) {
    if (opaqueDiamondPixel(x, y)) return y;
  }
  return 15;
}

function bottomRow(x: number): number {
  for (let y = 31; y >= 0; y -= 1) {
    if (opaqueDiamondPixel(x, y)) return y;
  }
  return 15;
}

export function diamondMask(): TileMask {
  const pixels: Pixel[] = [];
  for (let y = 0; y < 32; y += 1) {
    for (let x = 0; x < 64; x += 1) {
      if (opaqueDiamondPixel(x, y)) pixels.push({ x, y });
    }
  }
  return packPixels(pixels);
}

export function faceMask(side: "s" | "e", units: 1 | 2 | 4 | 8): TileMask {
  const pixels: Pixel[] = [];
  const firstX = side === "s" ? 0 : 32;
  const lastX = firstX + 31;
  for (let x = firstX; x <= lastX; x += 1) {
    const bottom = bottomRow(x);
    for (let y = bottom + 1; y <= bottom + 16 * units; y += 1) pixels.push({ x, y });
  }
  return packPixels(pixels);
}

export function wallMask(edge: "n" | "w", units: 1 | 2 | 4): TileMask {
  const pixels: Pixel[] = [];
  const firstX = edge === "n" ? 32 : 0;
  const lastX = firstX + 31;
  for (let x = firstX; x <= lastX; x += 1) {
    const top = topRow(x);
    for (let y = top - 16 * units; y < top; y += 1) pixels.push({ x, y });
  }
  return packPixels(pixels);
}

export function edgeLineMask(edge: "n" | "w"): TileMask {
  const pixels: Pixel[] = [];
  const firstX = edge === "n" ? 32 : 0;
  const lastX = firstX + 31;
  for (let x = firstX; x <= lastX; x += 1) {
    pixels.push({ x, y: topRow(x) });
  }
  return packPixels(pixels);
}

export function maskHasPixel(mask: TileMask, x: number, y: number): boolean {
  const localX = x - mask.offsetX;
  const localY = y - mask.offsetY;
  return localX >= 0 && localX < mask.width && localY >= 0 && localY < mask.height &&
    mask.alpha[localY * mask.width + localX] !== 0;
}
