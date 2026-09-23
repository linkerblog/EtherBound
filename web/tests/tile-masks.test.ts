import assert from "node:assert/strict";
import test from "node:test";
import { diamondMask, edgeLineMask, faceMask, maskHasPixel, wallMask } from "../src/game/tileMasks";

function placedPixels(mask: ReturnType<typeof diamondMask>, x = 0, y = 0): Set<string> {
  const pixels = new Set<string>();
  for (let py = 0; py < mask.height; py += 1) {
    for (let px = 0; px < mask.width; px += 1) {
      if (mask.alpha[py * mask.width + px]) pixels.add(`${x + mask.offsetX + px},${y + mask.offsetY + py}`);
    }
  }
  return pixels;
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
