// --- Isometric furniture ----------------------------------------------------
// Props are built from voxel boxes and painted back to front so nearer voxels
// cover farther ones.

const VOXEL_W = TILE_WIDTH / 4;
const VOXEL_H = VOXEL_W / 2;
const VOXEL_HALF = VOXEL_W / 2;
const VOXEL_QUARTER = VOXEL_H / 2;

const IRON_RAMP = [[32, 35, 41], [49, 54, 62], [68, 75, 85], [90, 98, 109], [113, 122, 133], [139, 148, 158], [168, 176, 184], [201, 207, 212]];
const GLOW_RAMP = [[120, 62, 24], [163, 89, 30], [203, 121, 40], [231, 158, 55], [245, 193, 82], [251, 221, 122], [254, 240, 175], [255, 252, 224]];
const LEAF_RAMP = [[30, 64, 34], [41, 84, 42], [56, 106, 50], [75, 129, 60], [99, 152, 72], [128, 175, 90], [161, 197, 112], [198, 219, 142]];
const SOIL_RAMP = [[45, 32, 22], [65, 47, 32], [86, 64, 44], [108, 83, 58], [131, 104, 74], [156, 127, 95], [182, 153, 120], [208, 182, 149]];

function furnitureVoxelHash(x, y, z, seed) {
  let hash = Math.imul(x + 17, 374761393) ^ Math.imul(y + 31, 668265263) ^ Math.imul(z + 47, 1274126177) ^ seed;
  hash = Math.imul(hash ^ (hash >>> 13), 1274126177);
  return (hash ^ (hash >>> 16)) >>> 0;
}

function grainPoint(ix, iy, seed) {
  let hash = Math.imul(ix + seed, 374761393) + Math.imul(iy + seed, 668265263);
  hash = Math.imul(hash ^ (hash >>> 13), 1274126177);
  return ((hash ^ (hash >>> 16)) >>> 0) / 4294967295;
}

function grainNoise(u, v, cellsU, cellsV, seed) {
  const gx = u * cellsU;
  const gy = v * cellsV;
  const x0 = Math.floor(gx);
  const y0 = Math.floor(gy);
  const fx = gx - x0;
  const fy = gy - y0;
  const smoothX = fx * fx * (3 - 2 * fx);
  const smoothY = fy * fy * (3 - 2 * fy);
  const upper = grainPoint(x0, y0, seed) + (grainPoint(x0 + 1, y0, seed) - grainPoint(x0, y0, seed)) * smoothX;
  const lower = grainPoint(x0, y0 + 1, seed) + (grainPoint(x0 + 1, y0 + 1, seed) - grainPoint(x0, y0 + 1, seed)) * smoothX;
  return upper + (lower - upper) * smoothY;
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

function furnitureRamps(colors) {
  const hue = rampHue(colors.ramp);
  return {
    wood: colors.ramp,
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

function furnitureBounds(voxels) {
  let minX = Infinity;
  let maxX = -Infinity;
  let minY = Infinity;
  let maxY = -Infinity;
  let spanX = 0;
  let spanY = 0;
  for (const voxel of voxels.values()) {
    const centerX = (voxel.x - voxel.y) * VOXEL_HALF;
    const centerY = (voxel.x + voxel.y + 1) * VOXEL_QUARTER - (voxel.z + 1) * VOXEL_H;
    minX = Math.min(minX, centerX - VOXEL_HALF);
    maxX = Math.max(maxX, centerX + VOXEL_HALF);
    minY = Math.min(minY, centerY - VOXEL_QUARTER);
    maxY = Math.max(maxY, centerY + VOXEL_H + VOXEL_QUARTER);
    spanX = Math.max(spanX, voxel.x + 1);
    spanY = Math.max(spanY, voxel.y + 1);
  }
  return { minX, minY, width: maxX - minX, height: maxY - minY, spanX, spanY };
}

function drawFurnitureVoxel(image, voxel, shiftX, shiftY, ramps, seed, brightness) {
  const role = FURNITURE_ROLES[voxel.role];
  const ramp = ramps[role.ramp];
  const centerX = (voxel.x - voxel.y) * VOXEL_HALF;
  const centerY = (voxel.x + voxel.y + 1) * VOXEL_QUARTER - (voxel.z + 1) * VOXEL_H;
  const wear = (furnitureVoxelHash(voxel.x, voxel.y, voxel.z, seed) % 1024) / 1024 - 0.5;
  for (let y = centerY - VOXEL_QUARTER; y < centerY + VOXEL_H + VOXEL_QUARTER; y += 1) {
    for (let x = centerX - VOXEL_HALF; x < centerX + VOXEL_HALF; x += 1) {
      const dx = x + 0.5 - centerX;
      const dy = y + 0.5 - centerY;
      const reach = Math.abs(dx);
      const edge = VOXEL_QUARTER - reach / 2;
      let face;
      let u;
      let v;
      if (reach / VOXEL_HALF + Math.abs(dy) / VOXEL_QUARTER <= 1) {
        face = 0;
        u = voxel.x + 0.5 + dx / VOXEL_W + dy / VOXEL_H;
        v = voxel.y + 0.5 + dy / VOXEL_H - dx / VOXEL_W;
      } else if (dy >= edge && dy < edge + VOXEL_H) {
        if (dx >= 0) {
          face = 1;
          u = voxel.x + 1 - (x + 0.5) / VOXEL_HALF;
          v = ((voxel.x + 1 + u) * VOXEL_QUARTER - (y + 0.5)) / VOXEL_H;
        } else {
          face = 2;
          u = voxel.y + 1 + (x + 0.5) / VOXEL_HALF;
          v = ((u + voxel.y + 1) * VOXEL_QUARTER - (y + 0.5)) / VOXEL_H;
        }
      } else {
        continue;
      }

      const grain = grainNoise(u, v, role.cells[0], role.cells[1], seed ^ Math.imul(face + 1, 0x85ebca6b)) - 0.5;
      let level = role.level
        + grain * role.grain * 2
        + wear * 0.55
        + role.face[face]
        + brightness
        + (BAYER_4X4[(((y % 4) + 4) % 4) * 4 + (((x % 4) + 4) % 4)] / 15 - 0.5) * 0.4;
      if (face > 0 && dy > edge + VOXEL_H - 2.5) level -= 0.55;
      const color = shade(ramp, level);
      const offset = ((y + shiftY) * image.width + x + shiftX) * 4;
      image.data[offset] = color[0];
      image.data[offset + 1] = color[1];
      image.data[offset + 2] = color[2];
      image.data[offset + 3] = 255;
    }
  }
}

function createFurnitureSprite(piece, ramps, seed) {
  const canvas = document.createElement('canvas');
  canvas.width = piece.bounds.width;
  canvas.height = piece.bounds.height;
  const context = canvas.getContext('2d');
  const image = context.createImageData(canvas.width, canvas.height);
  const voxels = [...buildFurnitureVoxels(piece, seed).values()];
  voxels.sort((a, b) => (a.x + a.y) - (b.x + b.y) || a.z - b.z || a.x - b.x);
  const brightness = (state.brightness - 58) / 30;
  for (const voxel of voxels) {
    drawFurnitureVoxel(image, voxel, -piece.bounds.minX, -piece.bounds.minY, ramps, seed, brightness);
  }
  context.putImageData(image, 0, 0);
  return canvas;
}

function isPixelStyle() {
  return state.material === 'furniture' && state.furnitureStyle === 'pixel';
}

// Pixel art rendering lives in pixelart.js; this function copies it to a canvas.
function createPixelSprite(key, ramps, seed) {
  const sprite = renderPixelFurniture(key, ramps, seed, {
    brightness: (state.brightness - 58) / 30,
    shadow: state.groundShadow,
  });
  const canvas = document.createElement('canvas');
  canvas.width = sprite.width;
  canvas.height = sprite.height;
  const context = canvas.getContext('2d');
  const image = context.createImageData(sprite.width, sprite.height);
  image.data.set(sprite.data);
  context.putImageData(image, 0, 0);
  return canvas;
}

let shadowCanvas;
function groundShadow() {
  if (!shadowCanvas) {
    shadowCanvas = document.createElement('canvas');
    shadowCanvas.width = TILE_WIDTH;
    shadowCanvas.height = TILE_HEIGHT;
    const context = shadowCanvas.getContext('2d');
    const image = context.createImageData(TILE_WIDTH, TILE_HEIGHT);
    for (let y = 0; y < TILE_HEIGHT; y += 1) {
      for (let x = 0; x < TILE_WIDTH; x += 1) {
        const depth = edgeDepth(x, y);
        if (depth < 0.02) continue;
        const alpha = Math.round((Math.min(1, depth / 0.5) * 0.55) * 6) / 6;
        const offset = (y * TILE_WIDTH + x) * 4;
        image.data[offset] = 5;
        image.data[offset + 1] = 11;
        image.data[offset + 2] = 8;
        image.data[offset + 3] = Math.round(alpha * 255);
      }
    }
    context.putImageData(image, 0, 0);
  }
  return shadowCanvas;
}

function currentSize() {
  if (state.material !== 'furniture') return { width: TILE_WIDTH, height: TILE_HEIGHT };
  return (isPixelStyle() ? PIXEL_FURNITURE : FURNITURE)[state.furniture].size;
}

function activePaletteColors() {
  if (state.material === 'grass') return palettes[state.palette];
  if (state.material === 'furniture') return materialPalettes.planks[state.palette];
  return materialPalettes[state.material][state.palette];
}

function furnitureInfo() {
  const piece = FURNITURE[state.furniture];
  const index = Object.keys(FURNITURE).indexOf(state.furniture) + 1;
  const pixel = isPixelStyle();
  return {
    title: `${piece.name} ${pixel ? 'sprite' : 'prop'}`,
    subtitle: pixel ? 'Isometric pixel art sprite' : piece.subtitle,
    corner: piece.corner,
    cornerTag: `${pixel ? 'SPRITE' : 'PROP'} / ${String(index).padStart(2, '0')}`,
    breadcrumb: piece.name.toUpperCase(),
    description: pixel ? PIXEL_FURNITURE[state.furniture].description : piece.description,
  };
}

for (const piece of Object.values(FURNITURE)) {
  const measured = buildFurnitureVoxels(piece, 0x51eed);
  addFurnitureBoxes(measured, piece.measure || []);
  piece.bounds = furnitureBounds(measured);
  piece.size = { width: piece.bounds.width, height: piece.bounds.height };
}
