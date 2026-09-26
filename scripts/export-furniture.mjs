#!/usr/bin/env node
// Dev-025 [Sec. 3.6]: reads BitCanvas/furnitureData.js (the source of truth for furniture shape)
// and writes one JSON voxel list per piece to game/assets/furniture/, so Godot can build real 3D
// meshes instead of a placeholder box. Ramps are pure math ported from BitCanvas/furniture.js;
// the base "wood" ramp is BitCanvas's own default palette (materialPalettes.planks.meadow).
import { readFileSync, writeFileSync, mkdirSync, rmSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import vm from "node:vm";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const OUT_DIR = join(ROOT, "game", "assets", "furniture");
// The same seed BitCanvas itself uses to measure a piece's sprite bounds (furniture.js).
const REFERENCE_SEED = 0x51eed;

const IRON_RAMP = [[32, 35, 41], [49, 54, 62], [68, 75, 85], [90, 98, 109], [113, 122, 133], [139, 148, 158], [168, 176, 184], [201, 207, 212]];
const GLOW_RAMP = [[120, 62, 24], [163, 89, 30], [203, 121, 40], [231, 158, 55], [245, 193, 82], [251, 221, 122], [254, 240, 175], [255, 252, 224]];
const LEAF_RAMP = [[30, 64, 34], [41, 84, 42], [56, 106, 50], [75, 129, 60], [99, 152, 72], [128, 175, 90], [161, 197, 112], [198, 219, 142]];
const SOIL_RAMP = [[45, 32, 22], [65, 47, 32], [86, 64, 44], [108, 83, 58], [131, 104, 74], [156, 127, 95], [182, 153, 120], [208, 182, 149]];
// BitCanvas's own default furniture palette (materialPalettes.planks.meadow, core.js): "Oak".
const WOOD_RAMP = [[48, 27, 15], [77, 43, 22], [108, 61, 29], [139, 81, 38], [169, 105, 54], [194, 133, 77], [218, 164, 106], [239, 198, 143]];

function furnitureVoxelHash(x, y, z, seed) {
  let hash = Math.imul(x + 17, 374761393) ^ Math.imul(y + 31, 668265263) ^ Math.imul(z + 47, 1274126177) ^ seed;
  hash = Math.imul(hash ^ (hash >>> 13), 1274126177);
  return (hash ^ (hash >>> 16)) >>> 0;
}

function rampHue(ramp) {
  const [r, g, b] = ramp[4].map((channel) => channel / 255);
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const delta = max - min;
  if (delta === 0) return 0;
  const hue = max === r ? ((g - b) / delta) % 6 : max === g ? (b - r) / delta + 2 : (r - g) / delta + 4;
  return ((hue * 60) % 360 + 360) % 360;
}

function hueRamp(hue, saturation, lightFrom, lightTo) {
  const ramp = [];
  for (let index = 0; index < 8; index += 1) {
    const lightness = lightFrom + (lightTo - lightFrom) * (index / 7);
    const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation;
    const segment = (hue / 60) % 6;
    const secondary = chroma * (1 - Math.abs((segment % 2) - 1));
    const channels = segment < 1 ? [chroma, secondary, 0]
      : segment < 2 ? [secondary, chroma, 0]
        : segment < 3 ? [0, chroma, secondary]
          : segment < 4 ? [0, secondary, chroma]
            : segment < 5 ? [secondary, 0, chroma]
              : [chroma, 0, secondary];
    const offset = lightness - chroma / 2;
    ramp.push(channels.map((channel) => Math.round(Math.max(0, Math.min(1, channel + offset)) * 255)));
  }
  return ramp;
}

function furnitureRamps() {
  const hue = rampHue(WOOD_RAMP);
  return {
    wood: WOOD_RAMP,
    iron: IRON_RAMP,
    glow: GLOW_RAMP,
    leaf: LEAF_RAMP,
    soil: SOIL_RAMP,
    fabric: hueRamp((hue + 158) % 360, 0.5, 0.26, 0.82),
    blanket: hueRamp((hue + 252) % 360, 0.42, 0.24, 0.78),
    linen: hueRamp((hue + 30) % 360, 0.12, 0.42, 0.98),
    book0: hueRamp((hue + 158) % 360, 0.58, 0.24, 0.8),
    book1: hueRamp((hue + 40) % 360, 0.6, 0.22, 0.78),
    book2: hueRamp((hue + 300) % 360, 0.44, 0.26, 0.76),
    book3: hueRamp((hue + 205) % 360, 0.5, 0.22, 0.78),
  };
}

function addFurnitureBoxes(voxels, boxes) {
  for (const box of boxes) {
    const [x0, y0, z0] = box.from;
    const [x1, y1, z1] = box.to;
    for (const [offsetX, offsetY] of box.at || [[0, 0]]) {
      for (let x = x0 + offsetX; x < x1 + offsetX; x += 1) {
        for (let y = y0 + offsetY; y < y1 + offsetY; y += 1) {
          if (box.chamfer && (x === x0 + offsetX || x === x1 + offsetX - 1) && (y === y0 + offsetY || y === y1 + offsetY - 1)) continue;
          for (let z = z0; z < z1; z += 1) voxels.set(`${x},${y},${z}`, { x, y, z, role: box.role });
        }
      }
    }
  }
}

function buildFurnitureVoxels(piece, seed) {
  const voxels = new Map();
  addFurnitureBoxes(voxels, piece.boxes);
  if (piece.decorate) piece.decorate(voxels, seed);
  return voxels;
}

// furnitureData.js has no DOM dependency; it only needs `furnitureVoxelHash` in scope for the
// pieces whose `decorate` calls it (shelf, plant).
const sandbox = { furnitureVoxelHash };
vm.createContext(sandbox);
vm.runInContext(readFileSync(join(ROOT, "BitCanvas", "furnitureData.js"), "utf8"), sandbox, { filename: "furnitureData.js" });
// `const` bindings from vm-executed code live in the context's lexical scope, not as own
// properties of the sandbox object, so they are read back with another `runInContext` call.
const FURNITURE = vm.runInContext("FURNITURE", sandbox);
const FURNITURE_ROLES = vm.runInContext("FURNITURE_ROLES", sandbox);
if (!FURNITURE || !FURNITURE_ROLES) throw new Error("furnitureData.js did not define FURNITURE/FURNITURE_ROLES");

const ramps = furnitureRamps();

function roleColor(roleName) {
  const role = FURNITURE_ROLES[roleName];
  const ramp = ramps[role.ramp];
  const index = Math.max(0, Math.min(7, Math.round(role.level)));
  return ramp[index];
}

rmSync(OUT_DIR, { recursive: true, force: true });
mkdirSync(OUT_DIR, { recursive: true });
const written = [];
for (const [key, piece] of Object.entries(FURNITURE)) {
  const voxels = [...buildFurnitureVoxels(piece, REFERENCE_SEED).values()]
    .sort((a, b) => a.z - b.z || a.y - b.y || a.x - b.x)
    // [x, y, z, r, g, b], one unit voxel per row; x/y/z are in quarter-metre grid units.
    .map((voxel) => JSON.stringify([voxel.x, voxel.y, voxel.z, ...roleColor(voxel.role)]));
  const path = join(OUT_DIR, `${key}.json`);
  writeFileSync(path, `{\n "key": ${JSON.stringify(key)},\n "name": ${JSON.stringify(piece.name)},\n "unit": 0.25,\n` +
    ` "voxels": [\n  ${voxels.join(",\n  ")}\n ]\n}\n`);
  written.push(`${key} (${voxels.length} voxels)`);
}
console.log(`export-furniture: wrote ${written.length} pieces to game/assets/furniture/`);
for (const line of written) console.log(`  ${line}`);
