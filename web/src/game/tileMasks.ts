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
    for (let y = top; y < top + 16 * units; y += 1) pixels.push({ x, y });
  }
  return packPixels(pixels);
}

// A quarter-metre wall projects to eight pixels along its edge at x1.
export const WALL_T_PX = 8;

export function wallTopMask(edge: "n" | "w"): TileMask {
  const pixels: Pixel[] = [];
  for (let y = -WALL_T_PX; y < 32; y += 1) {
    for (let x = 32; x < 64 + WALL_T_PX; x += 1) {
      const dx = x + 0.5 - 32;
      const dy = y + 0.5;
      const along = (dx / 2 + dy) / 32;
      const across = (dx / 2 - dy) / WALL_T_PX;
      if (along < 0 || along >= 1 || across < 0 || across > 1) continue;
      pixels.push({ x: edge === "n" ? x : 63 - x, y });
    }
  }
  return packPixels(pixels);
}

/** A wall run's end face hangs from its draw height, one 16-px unit per `units`. */
export function wallEndMask(face: "e" | "s", units: 1 | 2 | 4): TileMask {
  const pixels: Pixel[] = [];
  for (let column = 0; column < WALL_T_PX; column += 1) {
    const eastX = 64 + column;
    const x = face === "e" ? eastX : 63 - eastX;
    const top = Math.round(16 - (column + 0.5) / 2);
    for (let row = 0; row < 16 * units; row += 1) pixels.push({ x, y: top + row });
  }
  return packPixels(pixels);
}

/** The corner post fills the rhombus at the join of two quarter-metre wall tops. */
export function wallPostMask(): TileMask {
  const pixels: Pixel[] = [];
  for (let y = -WALL_T_PX; y < 0; y += 1) {
    for (let x = 32 - WALL_T_PX; x < 32 + WALL_T_PX; x += 1) {
      const dx = x + 0.5 - 32;
      const dy = y + 0.5 + WALL_T_PX / 2;
      if (Math.abs(dx) / WALL_T_PX + Math.abs(dy) / (WALL_T_PX / 2) <= 1) {
        pixels.push({ x, y });
      }
    }
  }
  return packPixels(pixels);
}

export function maskHasPixel(mask: TileMask, x: number, y: number): boolean {
  const localX = x - mask.offsetX;
  const localY = y - mask.offsetY;
  return localX >= 0 && localX < mask.width && localY >= 0 && localY < mask.height &&
    mask.alpha[localY * mask.width + localX] !== 0;
}

type SideSheet = { width: number; height: number; data: Uint8ClampedArray | Uint8Array };

export type SideCell = {
  width: number;
  height: number;
  offsetX: number;
  offsetY: number;
  rgba: Uint8Array;
};

/**
 * Shears one 32x16 side-sheet cell into the exact footprint of `faceMask(side, 1)`. Every mask
 * column takes its whole 16-pixel column from the sheet, moved down to the face's bottom contour,
 * so no pixel is dropped. The cap cell is row 0 and the fill cell row 16 of the sheet.
 */
export function shearSideCell(
  sheet: SideSheet,
  side: "s" | "e",
  part: "cap" | "fill",
  variant: 0 | 1 | 2 | 3,
): SideCell {
  const firstX = side === "s" ? 0 : 32;
  const lastX = firstX + 31;
  let minY = Number.POSITIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  for (let x = firstX; x <= lastX; x += 1) {
    const bottom = bottomRow(x);
    minY = Math.min(minY, bottom + 1);
    maxY = Math.max(maxY, bottom + 16);
  }
  const width = lastX - firstX + 1;
  const height = maxY - minY + 1;
  const rgba = new Uint8Array(width * height * 4);
  const partRow = part === "cap" ? 0 : 16;
  for (let x = firstX; x <= lastX; x += 1) {
    const sheetX = variant * 32 + (x - firstX);
    const bottom = bottomRow(x);
    for (let r = 0; r < 16; r += 1) {
      const source = (partRow + r) * sheet.width + sheetX;
      const at = source * 4;
      const localX = x - firstX;
      const localY = bottom + 1 + r - minY;
      const to = (localY * width + localX) * 4;
      rgba[to] = sheet.data[at] ?? 0;
      rgba[to + 1] = sheet.data[at + 1] ?? 0;
      rgba[to + 2] = sheet.data[at + 2] ?? 0;
      rgba[to + 3] = sheet.data[at + 3] ?? 0;
    }
  }
  return { width, height, offsetX: firstX, offsetY: minY, rgba };
}

/**
 * Shears one 32x16 wall-sheet cell into the exact footprint of `wallMask(edge, 1)`. Every mask
 * column takes its whole 16-pixel sheet column, moved down from the wall's draw-height contour, so
 * no pixel is dropped. The cap cell is row 0 and the fill cell row 16 of the sheet. It mirrors
 * `shearSideCell` across the diamond.
 */
export function shearWallCell(
  sheet: SideSheet,
  edge: "n" | "w",
  part: "cap" | "fill",
  variant: 0 | 1 | 2 | 3,
): SideCell {
  const firstX = edge === "n" ? 32 : 0;
  const lastX = firstX + 31;
  let minY = Number.POSITIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  for (let x = firstX; x <= lastX; x += 1) {
    const top = topRow(x);
    minY = Math.min(minY, top);
    maxY = Math.max(maxY, top + 15);
  }
  const width = lastX - firstX + 1;
  const height = maxY - minY + 1;
  const rgba = new Uint8Array(width * height * 4);
  const partRow = part === "cap" ? 0 : 16;
  for (let x = firstX; x <= lastX; x += 1) {
    const sheetX = variant * 32 + (x - firstX);
    const top = topRow(x);
    for (let r = 0; r < 16; r += 1) {
      const source = (partRow + r) * sheet.width + sheetX;
      const at = source * 4;
      const localX = x - firstX;
      const localY = top + r - minY;
      const to = (localY * width + localX) * 4;
      rgba[to] = sheet.data[at] ?? 0;
      rgba[to + 1] = sheet.data[at + 1] ?? 0;
      rgba[to + 2] = sheet.data[at + 2] ?? 0;
      rgba[to + 3] = sheet.data[at + 3] ?? 0;
    }
  }
  return { width, height, offsetX: firstX, offsetY: minY, rgba };
}
