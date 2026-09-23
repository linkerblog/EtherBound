import assert from "node:assert/strict";
import test from "node:test";
import { diamondMask, edgeLineMask, faceMask, maskHasPixel, shearSideCell, wallMask } from "../src/game/tileMasks";

function placedPixels(mask: ReturnType<typeof diamondMask>, x = 0, y = 0): Set<string> {
  const pixels = new Set<string>();
  for (let py = 0; py < mask.height; py += 1) {
    for (let px = 0; px < mask.width; px += 1) {
      if (mask.alpha[py * mask.width + px]) pixels.add(`${x + mask.offsetX + px},${y + mask.offsetY + py}`);
    }
  }
  return pixels;
}

type SideCell = ReturnType<typeof shearSideCell>;

function cellPixels(cell: SideCell): Set<string> {
  const pixels = new Set<string>();
  for (let py = 0; py < cell.height; py += 1) {
    for (let px = 0; px < cell.width; px += 1) {
      if (cell.rgba[(py * cell.width + px) * 4 + 3]) {
        pixels.add(`${cell.offsetX + px},${cell.offsetY + py}`);
      }
    }
  }
  return pixels;
}

// A 128x32 sheet whose every pixel encodes its own (sx, sy) in red and green.
function encodedSheet(): { width: number; height: number; data: Uint8ClampedArray } {
  const data = new Uint8ClampedArray(128 * 32 * 4);
  for (let y = 0; y < 32; y += 1) {
    for (let x = 0; x < 128; x += 1) {
      const at = (y * 128 + x) * 4;
      data[at] = x;
      data[at + 1] = y;
      data[at + 2] = 7;
      data[at + 3] = 255;
    }
  }
  return { width: 128, height: 32, data };
}

test("a 4 by 4 field of diamond masks tessellates without gaps or overlaps", () => {
  const mask = diamondMask();
  const coverage = new Map<string, number>();
  for (let x = 0; x < 4; x += 1) {
    for (let y = 0; y < 4; y += 1) {
      const sx = (x - y) * 32;
      const sy = (x + y) * 16;
      for (const pixel of placedPixels(mask, sx - 32, sy)) {
        coverage.set(pixel, (coverage.get(pixel) ?? 0) + 1);
      }
    }
  }

  for (const count of coverage.values()) assert.equal(count, 1);
  for (let py = -1; py <= 128; py += 1) {
    for (let px = -160; px <= 160; px += 1) {
      const inside = Array.from({ length: 4 }, (_, x) => Array.from({ length: 4 }, (_, y) => {
        const sx = (x - y) * 32;
        const sy = (x + y) * 16;
        const localX = px + 0.5 - sx;
        const localY = py + 0.5 - sy;
        return Math.abs(localX) <= 2 * Math.min(localY, 32 - localY);
      })).flat().some(Boolean);
      if (inside) assert.equal(coverage.get(`${px},${py}`), 1);
    }
  }
});

test("four one-unit south faces exactly compose a four-unit face", () => {
  const four = placedPixels(faceMask("s", 4));
  const stacked = new Set<string>();
  for (let lower = 0; lower < 4; lower += 1) {
    for (const pixel of placedPixels(faceMask("s", 1), 0, (3 - lower) * 16)) stacked.add(pixel);
  }
  assert.deepEqual(stacked, four);
});

test("raised 2 by 2 plateaus retain covered tops and exposed south/east sides", () => {
  for (const height of [1, 2, 3]) {
    const platformTops = new Set<string>();
    const platformFaces = new Set<string>();
    const lower = new Set<string>();
    const topMask = diamondMask();
    const plateau = new Set(["1,1", "2,1", "1,2", "2,2"]);
    for (let x = 0; x < 4; x += 1) {
      for (let y = 0; y < 4; y += 1) {
        const sx = (x - y) * 32;
        const sy = (x + y) * 16;
        const originX = sx - 32;
        const originY = sy - height * 16;
        const key = `${x},${y}`;
        const isRaised = plateau.has(key);
        const top = placedPixels(topMask, originX, isRaised ? originY : sy);
        for (const pixel of top) (isRaised ? platformTops : lower).add(pixel);
        if (!isRaised) continue;
        if (!plateau.has(`${x},${y + 1}`)) {
          for (const pixel of placedPixels(faceMask("s", height), originX, originY)) platformFaces.add(pixel);
        }
        if (!plateau.has(`${x + 1},${y}`)) {
          for (const pixel of placedPixels(faceMask("e", height), originX, originY)) platformFaces.add(pixel);
        }
      }
    }

    for (const [x, y] of [[1, 1], [2, 1], [1, 2], [2, 2]]) {
      const sx = (x - y) * 32;
      const sy = (x + y) * 16 - height * 16;
      assert.ok(platformTops.has(`${sx},${sy + 16}`));
    }
    for (const pixel of platformFaces) assert.equal(platformTops.has(pixel), false);
    assert.ok(lower.size > 0);
    for (let x = 0; x < 4; x += 1) {
      for (let y = 0; y < 4; y += 1) {
        if (plateau.has(`${x},${y}`)) continue;
        const sx = (x - y) * 32;
        const sy = (x + y) * 16;
        assert.ok(lower.has(`${sx},${sy + 16}`));
      }
    }
    assert.ok(platformFaces.size > 0);
  }
});

test("wall top lines touch the wall masks without a gap or overlap", () => {
  for (const edge of ["n", "w"] as const) {
    const wall = wallMask(edge, 1);
    const line = edgeLineMask(edge);
    for (let x = 0; x < 64; x += 1) {
      for (let lineY = -16; lineY < 32; lineY += 1) {
        if (!maskHasPixel(line, x, lineY)) continue;
        assert.equal(maskHasPixel(wall, x, lineY), false);
        assert.equal(maskHasPixel(wall, x, lineY - 1), true);
      }
    }
  }
});

test("every sheared side cell has exactly the footprint of a one-unit face", () => {
  const sheet = encodedSheet();
  for (const side of ["s", "e"] as const) {
    const face = faceMask(side, 1);
    const expected = placedPixels(face);
    for (const part of ["cap", "fill"] as const) {
      for (const variant of [0, 1, 2, 3] as const) {
        const cell = shearSideCell(sheet, side, part, variant);
        assert.equal(cell.width, face.width, `${side}/${part}/${variant} width`);
        assert.equal(cell.height, face.height, `${side}/${part}/${variant} height`);
        assert.equal(cell.offsetX, face.offsetX, `${side}/${part}/${variant} offsetX`);
        assert.equal(cell.offsetY, face.offsetY, `${side}/${part}/${variant} offsetY`);
        assert.deepEqual(cellPixels(cell), expected, `${side}/${part}/${variant} pixels`);
      }
    }
  }
});

test("sheared side cells sample the sheet column by column, cap and fill", () => {
  const sheet = encodedSheet();
  for (const side of ["s", "e"] as const) {
    const firstX = side === "s" ? 0 : 32;
    const face = faceMask(side, 1);
    const columnTop = new Map<number, number>();
    for (let py = 0; py < face.height; py += 1) {
      for (let px = 0; px < face.width; px += 1) {
        if (!face.alpha[py * face.width + px]) continue;
        const x = face.offsetX + px;
        const y = face.offsetY + py;
        columnTop.set(x, Math.min(columnTop.get(x) ?? y, y));
      }
    }
    for (const part of ["cap", "fill"] as const) {
      const partRow = part === "cap" ? 0 : 16;
      for (const variant of [0, 3] as const) {
        const cell = shearSideCell(sheet, side, part, variant);
        for (const [x, top] of columnTop) {
          for (let y = top; y < top + 16; y += 1) {
            const cellAt = ((y - cell.offsetY) * cell.width + (x - cell.offsetX)) * 4;
            const sheetAt = ((partRow + y - top) * sheet.width + variant * 32 + x - firstX) * 4;
            assert.equal(cell.rgba[cellAt], sheet.data[sheetAt], `${side}/${part}/${variant} r at ${x},${y}`);
            assert.equal(cell.rgba[cellAt + 1], sheet.data[sheetAt + 1], `${side}/${part}/${variant} g at ${x},${y}`);
            assert.equal(cell.rgba[cellAt + 3], 255, `${side}/${part}/${variant} alpha at ${x},${y}`);
          }
        }
      }
    }
  }
});
