import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { TERRAIN_SIDE_FILES, TERRAIN_SPRITE_FILES, sideTint, sideVariant, spriteTint } from "../src/game/terrainSprites";

const spriteRoot = new URL("../../src/sprites/", import.meta.url);
const sheetsModule = new URL("../src/game/terrainSheets.ts", import.meta.url);

test("terrain sprite keys refer to registered world materials", () => {
  const materials = readFileSync(new URL("../../server/src/etherbound/world/materials.toml", import.meta.url), "utf8");
  for (const key of Object.keys(TERRAIN_SPRITE_FILES)) {
    assert.match(materials, new RegExp(`key\\s*=\\s*"${key}"`));
  }
  for (const key of Object.keys(TERRAIN_SIDE_FILES)) {
    assert.match(materials, new RegExp(`key\\s*=\\s*"${key}"`));
  }
});

test("every terrain sprite file exists under the root sprites and is a 256x32 sheet", () => {
  for (const file of Object.values(TERRAIN_SPRITE_FILES)) {
    const png = readFileSync(new URL(file, spriteRoot));
    assert.equal(png.readUInt32BE(16), 256, `${file} width`);
    assert.equal(png.readUInt32BE(20), 32, `${file} height`);
  }
});

test("every terrain side sheet file exists under the root sprites and is a 128x32 sheet", () => {
  for (const file of Object.values(TERRAIN_SIDE_FILES)) {
    const png = readFileSync(new URL(file, spriteRoot));
    assert.equal(png.readUInt32BE(16), 128, `${file} width`);
    assert.equal(png.readUInt32BE(20), 32, `${file} height`);
  }
});

test("terrainSheets statically imports every sheet and never resolves a URL", () => {
  const source = readFileSync(sheetsModule, "utf8");
  assert.doesNotMatch(source, /new URL\(/);
  for (const file of [...Object.values(TERRAIN_SPRITE_FILES), ...Object.values(TERRAIN_SIDE_FILES)]) {
    assert.ok(source.includes(`from "../../../src/sprites/${file}"`), `${file} must be a static import`);
  }
});

test("sprite tint preserves base colours at height zero and handles zero channels", () => {
  for (const color of [0x6a9f4b, 0xb6814f, 0xa1a5a3]) assert.equal(spriteTint(color, 0), 0xffffff);
  assert.equal(spriteTint(0x001000, 0), 0xffffff);
});

test("side tint is the sprite tint scaled per channel by the side light", () => {
  for (const color of [0x6a9f4b, 0xb6814f, 0xa1a5a3]) assert.equal(sideTint(color, 0, 1), spriteTint(color, 0));
  const full = sideTint(0x6a9f4b, 3, 1);
  const dim = sideTint(0x6a9f4b, 3, 0.5);
  for (const shift of [16, 8, 0]) {
    const channel = (full >> shift) & 0xff;
    assert.equal((dim >> shift) & 0xff, Math.round(channel * 0.5));
  }
});

test("side variant is deterministic and uses all four values over a 16x16 area", () => {
  const seen = new Set<number>();
  for (let x = 0; x < 16; x += 1) {
    for (let y = 0; y < 16; y += 1) {
      const value = sideVariant(x, y, "s", 12);
      assert.equal(value, sideVariant(x, y, "s", 12));
      seen.add(value);
    }
  }
  assert.deepEqual([...seen].sort(), [0, 1, 2, 3]);
});

test("side variant keeps its south and east values on a fixed sample", () => {
  assert.equal(sideVariant(5, 7, "s", 18), 1);
  assert.equal(sideVariant(5, 7, "e", 18), 0);
  assert.equal(sideVariant(12, 3, "s", 11), 1);
  assert.equal(sideVariant(12, 3, "e", 11), 1);
  assert.equal(sideVariant(-3, 9, "s", 24), 3);
  assert.equal(sideVariant(-3, 9, "e", 24), 2);
});

test("north and west wall variants are deterministic and cover all four values", () => {
  const seen = new Set<number>();
  for (let x = 0; x < 16; x += 1) {
    for (let y = 0; y < 16; y += 1) {
      for (const side of ["n", "w"] as const) {
        const value = sideVariant(x, y, side, 12);
        assert.ok(value >= 0 && value <= 3);
        assert.equal(value, sideVariant(x, y, side, 12));
        seen.add(value);
      }
    }
  }
  assert.deepEqual([...seen].sort(), [0, 1, 2, 3]);
  assert.notEqual(sideVariant(0, 0, "n", 12), sideVariant(0, 0, "s", 12));
  assert.notEqual(sideVariant(0, 0, "w", 12), sideVariant(0, 0, "s", 12));
});