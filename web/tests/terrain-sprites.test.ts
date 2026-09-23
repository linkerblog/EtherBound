import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { TERRAIN_SPRITE_FILES, spriteTint } from "../src/game/terrainSprites";

const spriteRoot = new URL("../../src/sprites/", import.meta.url);
const sheetsModule = new URL("../src/game/terrainSheets.ts", import.meta.url);

test("terrain sprite keys refer to registered world materials", () => {
  const materials = readFileSync(new URL("../../server/src/etherbound/world/materials.toml", import.meta.url), "utf8");
  for (const key of Object.keys(TERRAIN_SPRITE_FILES)) {
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

test("terrainSheets statically imports every sheet and never resolves a URL", () => {
  const source = readFileSync(sheetsModule, "utf8");
  assert.doesNotMatch(source, /new URL\(/);
  for (const file of Object.values(TERRAIN_SPRITE_FILES)) {
    assert.ok(source.includes(`from "../../../src/sprites/${file}"`), `${file} must be a static import`);
  }
});

test("sprite tint preserves base colours at height zero and handles zero channels", () => {
  for (const color of [0x6a9f4b, 0xb6814f, 0xa1a5a3]) assert.equal(spriteTint(color, 0), 0xffffff);
  assert.equal(spriteTint(0x001000, 0), 0xffffff);
});
