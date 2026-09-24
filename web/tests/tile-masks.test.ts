import assert from "node:assert/strict";
import test from "node:test";
import * as masks from "../src/game/tileMasks";
import { diamondMask, faceMask, maskHasPixel, shearSideCell, shearWallCell, wallEndMask, wallMask, wallPostMask, wallTopMask, WALL_T_PX } from "../src/game/tileMasks";

function topRow(x: number): number {
  for (let y = 0; y < 32; y += 1) {
    if (Math.abs(x - 31.5) <= 2 * Math.min(y, 31 - y) + 0.5) return y;
  }
  return 15;
}

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

test("the old edge line mask is gone", () => {
  assert.equal("edgeLineMask" in masks, false);
});

test("wall top strips are four-pixel columns on the wall's top line", () => {
  for (const edge of ["n", "w"] as const) {
    const mask = wallTopMask(edge);
    const columns = new Map<number, number[]>();
    for (let py = 0; py < mask.height; py += 1) {
      for (let px = 0; px < mask.width; px += 1) {
        if (!mask.alpha[py * mask.width + px]) continue;
        const x = mask.offsetX + px;
        columns.set(x, [...(columns.get(x) ?? []), mask.offsetY + py]);
      }
    }
    let count = 0;
    for (const rows of columns.values()) count += rows.length;
    assert.equal(count, 128, `${edge} pixel count`);
    assert.equal(columns.size, 32, `${edge} column count`);
    for (const [x, rows] of columns) {
      assert.equal(rows.length, WALL_T_PX, `${edge} column ${x} height`);
      assert.equal(Math.max(...rows), topRow(x), `${edge} column ${x} bottom`);
    }
  }
});

test("the north and west top strips are mirror images", () => {
  const north = placedPixels(wallTopMask("n"));
  const west = placedPixels(wallTopMask("w"));
  assert.equal(north.size, 128);
  assert.equal(west.size, 128);
  for (const pixel of north) {
    const [x, y] = pixel.split(",").map(Number);
    assert.ok(west.has(`${63 - x},${y}`), `mirror of ${pixel}`);
  }
});

test("wall end masks are four pixels wide and 16 x units tall", () => {
  for (const face of ["e", "s"] as const) {
    for (const units of [1, 2, 4] as const) {
      const mask = wallEndMask(face, units);
      const pixels = placedPixels(mask);
      const columns = new Set([...pixels].map((pixel) => pixel.split(",")[0]));
      assert.equal(pixels.size, 64 * units, `${face}/${units} pixel count`);
      assert.equal(mask.width, WALL_T_PX, `${face}/${units} width`);
      assert.equal(columns.size, WALL_T_PX, `${face}/${units} column count`);
    }
  }
});

test("the corner post is 16 px above the top vertex, touching both strips", () => {
  const post = placedPixels(wallPostMask());
  assert.equal(post.size, 16);
  const diamond = diamondMask();
  for (const pixel of post) {
    const [x, y] = pixel.split(",").map(Number);
    assert.equal(maskHasPixel(diamond, x, y), false, `post overlaps the diamond at ${pixel}`);
  }
  for (const strip of [placedPixels(wallTopMask("n")), placedPixels(wallTopMask("w"))]) {
    const touching = [...post].some((pixel) => {
      const [x, y] = pixel.split(",").map(Number);
      return strip.has(`${x},${y - 1}`) || strip.has(`${x},${y + 1}`) ||
        strip.has(`${x - 1},${y}`) || strip.has(`${x + 1},${y}`);
    });
    assert.ok(touching, "post must touch a strip");
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

test("every sheared wall cell has exactly the footprint of a one-unit wall", () => {
  const sheet = encodedSheet();
  for (const edge of ["n", "w"] as const) {
    const wall = wallMask(edge, 1);
    const expected = placedPixels(wall);
    for (const part of ["cap", "fill"] as const) {
      for (const variant of [0, 1, 2, 3] as const) {
        const cell = shearWallCell(sheet, edge, part, variant);
        assert.equal(cell.width, wall.width, `${edge}/${part}/${variant} width`);
        assert.equal(cell.height, wall.height, `${edge}/${part}/${variant} height`);
        assert.equal(cell.offsetX, wall.offsetX, `${edge}/${part}/${variant} offsetX`);
        assert.equal(cell.offsetY, wall.offsetY, `${edge}/${part}/${variant} offsetY`);
        assert.deepEqual(cellPixels(cell), expected, `${edge}/${part}/${variant} pixels`);
      }
    }
  }
});

test("sheared wall cells sample the sheet column by column, cap and fill", () => {
  const sheet = encodedSheet();
  for (const edge of ["n", "w"] as const) {
    const firstX = edge === "n" ? 32 : 0;
    const wall = wallMask(edge, 1);
    const columnTop = new Map<number, number>();
    for (let py = 0; py < wall.height; py += 1) {
      for (let px = 0; px < wall.width; px += 1) {
        if (!wall.alpha[py * wall.width + px]) continue;
        const x = wall.offsetX + px;
        const y = wall.offsetY + py;
        columnTop.set(x, Math.min(columnTop.get(x) ?? y, y));
      }
    }
    for (const part of ["cap", "fill"] as const) {
      const partRow = part === "cap" ? 0 : 16;
      for (const variant of [0, 3] as const) {
        const cell = shearWallCell(sheet, edge, part, variant);
        for (const [x, top] of columnTop) {
          for (let y = top; y < top + 16; y += 1) {
            const cellAt = ((y - cell.offsetY) * cell.width + (x - cell.offsetX)) * 4;
            const sheetAt = ((partRow + y - top) * sheet.width + variant * 32 + x - firstX) * 4;
            assert.equal(cell.rgba[cellAt], sheet.data[sheetAt], `${edge}/${part}/${variant} r at ${x},${y}`);
            assert.equal(cell.rgba[cellAt + 1], sheet.data[sheetAt + 1], `${edge}/${part}/${variant} g at ${x},${y}`);
            assert.equal(cell.rgba[cellAt + 3], 255, `${edge}/${part}/${variant} alpha at ${x},${y}`);
          }
        }
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
