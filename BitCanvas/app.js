// Eight-step ramps, darkest to brightest: crevices, stems, then blade tips.
const palettes = {
  meadow: { ramp: [[8, 48, 18], [12, 70, 22], [18, 94, 28], [26, 120, 34], [38, 146, 40], [58, 172, 48], [92, 198, 62], [134, 222, 84]] },
  emerald: { ramp: [[9, 38, 35], [13, 62, 49], [18, 89, 63], [27, 117, 77], [46, 147, 91], [79, 178, 107], [128, 207, 128], [186, 232, 160]] },
  moss: { ramp: [[35, 39, 28], [51, 57, 36], [69, 77, 44], [89, 97, 54], [111, 117, 64], [137, 140, 78], [167, 164, 96], [205, 196, 124]] },
  autumn: { ramp: [[51, 41, 26], [77, 61, 32], [105, 85, 38], [135, 111, 44], [163, 137, 52], [191, 163, 66], [217, 190, 90], [240, 216, 130]] },
};

const materialPalettes = {
  planks: {
    meadow: { name: 'Oak', detail: 'Warm oak', ramp: [[48, 27, 15], [77, 43, 22], [108, 61, 29], [139, 81, 38], [169, 105, 54], [194, 133, 77], [218, 164, 106], [239, 198, 143]] },
    emerald: { name: 'Walnut', detail: 'Dark walnut', ramp: [[30, 19, 17], [52, 31, 25], [74, 42, 32], [98, 56, 38], [124, 74, 48], [151, 97, 63], [181, 126, 86], [211, 160, 115]] },
    moss: { name: 'Pine', detail: 'Weathered pine', ramp: [[36, 34, 23], [57, 51, 33], [79, 68, 42], [103, 86, 51], [127, 105, 62], [153, 128, 79], [181, 154, 103], [210, 184, 132]] },
    autumn: { name: 'Driftwood', detail: 'Washed wood', ramp: [[47, 40, 33], [69, 59, 48], [94, 79, 63], [120, 102, 83], [147, 127, 107], [175, 154, 132], [204, 184, 160], [230, 215, 193]] },
  },
  cobblestone: {
    meadow: { name: 'Slate', detail: 'Blue slate', ramp: [[29, 35, 42], [48, 56, 64], [68, 77, 85], [89, 99, 106], [111, 121, 126], [137, 146, 148], [166, 173, 171], [199, 203, 195]] },
    emerald: { name: 'Granite', detail: 'Cool granite', ramp: [[36, 37, 35], [56, 57, 54], [77, 78, 74], [99, 100, 95], [121, 123, 117], [148, 149, 141], [177, 177, 166], [207, 204, 190]] },
    moss: { name: 'Sandstone', detail: 'Light sandstone', ramp: [[55, 43, 32], [78, 62, 45], [101, 82, 59], [126, 103, 73], [151, 125, 91], [177, 150, 111], [202, 176, 136], [226, 204, 165]] },
    autumn: { name: 'Mossy', detail: 'Mossy stone', ramp: [[31, 39, 31], [48, 58, 44], [66, 77, 56], [85, 97, 69], [105, 117, 82], [130, 141, 99], [159, 168, 120], [190, 196, 147]] },
  },
  concrete: {
    meadow: { name: 'Concrete', detail: 'Natural concrete', ramp: [[38, 42, 43], [57, 62, 64], [77, 83, 85], [99, 106, 108], [122, 130, 131], [149, 157, 157], [178, 185, 183], [208, 213, 207]] },
    emerald: { name: 'Bluestone', detail: 'Blue-gray stone', ramp: [[31, 39, 48], [47, 58, 70], [65, 78, 92], [85, 100, 114], [107, 123, 138], [132, 149, 163], [161, 178, 191], [193, 208, 218]] },
    moss: { name: 'Sandstone', detail: 'Warm sand', ramp: [[49, 45, 37], [69, 64, 53], [91, 85, 71], [115, 108, 91], [140, 133, 113], [167, 159, 137], [195, 187, 162], [224, 215, 188]] },
    autumn: { name: 'Charcoal', detail: 'Dark charcoal', ramp: [[25, 29, 32], [40, 45, 49], [57, 63, 67], [76, 83, 87], [98, 105, 108], [122, 130, 132], [149, 157, 157], [179, 186, 184]] },
  },
  asphalt: {
    meadow: { name: 'Fresh', detail: 'New blacktop', ramp: [[18, 19, 22], [28, 30, 34], [40, 42, 47], [54, 57, 62], [70, 73, 79], [90, 93, 99], [116, 119, 124], [150, 152, 155]] },
    emerald: { name: 'Worn', detail: 'Faded gray', ramp: [[30, 31, 33], [44, 46, 49], [59, 61, 65], [75, 78, 82], [93, 96, 100], [114, 117, 120], [139, 141, 143], [168, 169, 169]] },
    moss: { name: 'Bleached', detail: 'Sun-baked', ramp: [[40, 38, 35], [56, 54, 50], [73, 71, 66], [91, 89, 84], [111, 108, 102], [133, 130, 123], [158, 155, 147], [186, 182, 172]] },
    autumn: { name: 'Wet', detail: 'Rain-dark', ramp: [[14, 16, 21], [22, 25, 32], [32, 36, 45], [44, 49, 59], [58, 64, 75], [76, 83, 95], [100, 108, 120], [132, 140, 150]] },
  },
  roofing: {
    meadow: { name: 'Membrane', detail: 'Bitumen felt', ramp: [[24, 25, 29], [36, 38, 43], [50, 52, 58], [63, 66, 73], [77, 80, 88], [95, 98, 106], [120, 123, 130], [154, 157, 162]] },
    emerald: { name: 'Slate', detail: 'Cool grey', ramp: [[26, 30, 36], [39, 45, 53], [54, 61, 71], [70, 79, 90], [87, 97, 109], [108, 118, 130], [134, 144, 155], [168, 176, 185]] },
    moss: { name: 'Gravel', detail: 'Warm grit', ramp: [[38, 35, 31], [54, 50, 45], [71, 66, 60], [89, 84, 77], [108, 102, 94], [130, 124, 115], [156, 150, 140], [186, 180, 170]] },
    autumn: { name: 'Felt', detail: 'Green felt', ramp: [[24, 32, 27], [35, 46, 39], [48, 61, 52], [62, 77, 66], [78, 94, 82], [98, 114, 101], [124, 139, 126], [158, 170, 158]] },
  },
  brick: {
    meadow: { name: 'Red', detail: 'Common red', ramp: [[52, 22, 17], [82, 34, 25], [112, 48, 34], [140, 64, 45], [166, 83, 61], [186, 105, 80], [204, 131, 104], [224, 163, 137]] },
    emerald: { name: 'Burnt', detail: 'Dark engineering', ramp: [[34, 18, 18], [55, 27, 26], [78, 37, 34], [101, 49, 43], [124, 62, 52], [146, 79, 66], [170, 101, 86], [198, 132, 115]] },
    moss: { name: 'Stock', detail: 'Yellow stock', ramp: [[60, 45, 26], [89, 68, 40], [119, 92, 55], [147, 116, 72], [172, 140, 91], [194, 163, 114], [214, 188, 142], [233, 214, 176]] },
    autumn: { name: 'Weathered', detail: 'Faded clay', ramp: [[54, 36, 31], [82, 55, 46], [110, 75, 62], [136, 95, 79], [160, 116, 98], [181, 139, 120], [201, 164, 146], [222, 194, 178]] },
  },
};

const GAME_SHEETS = {
  grass: { folder: 'grass', variants: 'grass_x4.png', sides: 'grass_side_x4.png' },
  planks: { folder: 'floor', variants: 'planks_x4.png', sides: 'planks_side_x4.png' },
  cobblestone: { folder: 'floor', variants: 'stone_x4.png', sides: 'stone_side_x4.png' },
  concrete: { folder: 'floor', variants: 'concrete_x4.png', sides: 'concrete_side_x4.png' },
  asphalt: { folder: 'floor', variants: 'asphalt_x4.png', sides: 'asphalt_side_x4.png' },
  roofing: { folder: 'floor', variants: 'roofing_x4.png', sides: 'roofing_side_x4.png' },
  brick: { folder: 'wall', variants: 'brick_x4.png', sides: 'brick_side_x4.png' },
};

const DIRT_COLORS = [[146, 108, 66], [116, 83, 50], [88, 62, 38], [62, 44, 28]];

const BAYER_4X4 = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

const TILE_WIDTH = 64;
const TILE_HEIGHT = 32;
const SIDE_REPEAT_HEIGHT = 16;
const PREVIEW_SCALE = 4;
const VARIATION_COUNT = 4;
const state = { material: 'grass', furniture: 'table', furnitureStyle: 'pixel', groundShadow: true, density: 6, brightness: 58, palette: 'meadow', customPalette: null, seed: 'A17F3C', dirt: false, view: 'tiles' };
const preview = document.querySelector('#texture-preview');
const previewContext = preview.getContext('2d');
const previewMat = document.querySelector('.preview-mat');
const seedInput = document.querySelector('#seed-input');
const densityInput = document.querySelector('#density');
const brightnessInput = document.querySelector('#brightness');
const statusFeed = document.querySelector('#toast');
let textureCanvas;
let textureSides;
let textureBlock;
let textureCanvases = [];
let sideCanvases = [];
let textureAtlas;
let sideAtlas;
let sideSheetTexture;
let sideBlockCanvases = [];

function seedNumber(value) {
  let hash = 2166136261;
  for (let i = 0; i < value.length; i += 1) {
    hash ^= value.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }
  return hash >>> 0;
}

function randomGenerator(seed) {
  let value = seed || 1;
  return () => {
    value += 0x6D2B79F5;
    let result = value;
    result = Math.imul(result ^ (result >>> 15), result | 1);
    result ^= result + Math.imul(result ^ (result >>> 7), result | 61);
    return ((result ^ (result >>> 14)) >>> 0) / 4294967296;
  };
}

function isoNoise(x, y, cellsU, cellsV, seed) {
  const tileU = x / TILE_WIDTH + y / TILE_HEIGHT;
  const tileV = x / TILE_WIDTH - y / TILE_HEIGHT;
  const gx = tileU * cellsU;
  const gy = tileV * cellsV;
  const x0 = Math.floor(gx);
  const y0 = Math.floor(gy);
  const fx = gx - x0;
  const fy = gy - y0;
  const smoothX = fx * fx * (3 - 2 * fx);
  const smoothY = fy * fy * (3 - 2 * fy);
  const sample = (ix, iy) => {
    const wrappedX = ((ix % cellsU) + cellsU) % cellsU;
    const wrappedY = ((iy % cellsV) + cellsV) % cellsV;
    let hash = Math.imul(wrappedX + seed, 374761393) + Math.imul(wrappedY + seed, 668265263);
    hash = Math.imul(hash ^ (hash >>> 13), 1274126177);
    return ((hash ^ (hash >>> 16)) >>> 0) / 4294967295;
  };
  const upper = sample(x0, y0) + (sample(x0 + 1, y0) - sample(x0, y0)) * smoothX;
  const lower = sample(x0, y0 + 1) + (sample(x0 + 1, y0 + 1) - sample(x0, y0 + 1)) * smoothX;
  return upper + (lower - upper) * smoothY;
}

function edgeDepth(x, y) {
  return 1
    - Math.abs((x + 0.5 - TILE_WIDTH / 2) / (TILE_WIDTH / 2))
    - Math.abs((y + 0.5 - TILE_HEIGHT / 2) / (TILE_HEIGHT / 2));
}

function isInsideDiamond(x, y) {
  return edgeDepth(x, y) >= 0;
}

function shade(ramp, level) {
  return ramp[Math.max(0, Math.min(ramp.length - 1, Math.round(level)))];
}

function meadowLight(x, y, seed) {
  const broad = isoNoise(x + 0.5, y + 0.5, 3, 3, seed);
  const small = isoNoise(x + 0.5, y + 0.5, 7, 7, seed ^ 0x5bd1e995);
  return (broad - 0.5) * 1.1 + (small - 0.5) * 0.6;
}

// A layer keeps, per pixel, how far above its tuft's root the painted pixel sits.
// Smaller values are closer to the viewer, so a blade only covers what lies behind
// it; this stays correct where the tile wraps, unlike sorting tufts by row.
const GROUND_DEPTH = 100;
const DIRT_DEPTH = -GROUND_DEPTH;

function createLayer() {
  return {
    pixels: new Uint8ClampedArray(TILE_WIDTH * TILE_HEIGHT * 4),
    depth: new Float32Array(TILE_WIDTH * TILE_HEIGHT).fill(GROUND_DEPTH),
  };
}

function setPixel(layer, x, y, color, depth = GROUND_DEPTH) {
  const index = y * TILE_WIDTH + x;
  if (depth > layer.depth[index]) return;
  layer.depth[index] = depth;
  layer.pixels[index * 4] = color[0];
  layer.pixels[index * 4 + 1] = color[1];
  layer.pixels[index * 4 + 2] = color[2];
  layer.pixels[index * 4 + 3] = 255;
}

// The isometric lattice repeats every (32, 16) inside the 64 × 32 canvas, so every
// pixel has exactly two copies there; painting both keeps all four edges seamless.
// Stamp entries are [dx, dy, color, depth?]; depth defaults to dy.
function paintWrapped(layer, x, y, stamp) {
  for (const [dx, dy, color, depth = dy] of stamp) {
    for (let copy = 0; copy < 2; copy += 1) {
      const px = (((x + dx + copy * TILE_WIDTH / 2) % TILE_WIDTH) + TILE_WIDTH) % TILE_WIDTH;
      const py = (((y + dy + copy * TILE_HEIGHT / 2) % TILE_HEIGHT) + TILE_HEIGHT) % TILE_HEIGHT;
      setPixel(layer, px, py, color, depth);
    }
  }
}

// Variant-only details stay away from the diamond edges so neighbours still match.
function paintInside(layer, x, y, stamp, margin) {
  const fits = stamp.every(([dx, dy]) => x + dx >= 0 && x + dx < TILE_WIDTH
    && y + dy >= 0 && y + dy < TILE_HEIGHT && edgeDepth(x + dx, y + dy) >= margin);
  if (!fits) return false;
  for (const [dx, dy, color, depth = dy] of stamp) setPixel(layer, x + dx, y + dy, color, depth);
  return true;
}

// A tuft is a fan of 3–5 long blades leaning outwards (a V shape): dark root,
// mid stem, bright tip. A shadow on each blade's right side and a crevice under
// the root keep overlapping tufts readable as separate clumps.
function grassTuft(random, level, ramp, maxHeight = 8) {
  const pixels = [];
  const crevice = shade(ramp, level - 5.5);
  pixels.push([-1, 1, crevice], [0, 1, crevice], [1, 1, crevice]);
  const blades = 3 + Math.floor(random() * 3);
  for (let blade = 0; blade < blades; blade += 1) {
    const spread = blade - (blades - 1) / 2;
    const rootX = Math.round(spread * 0.9);
    const lean = Math.sign(spread) || (random() < 0.5 ? -1 : 1);
    const slope = lean * (0.3 + random() * 0.45);
    const height = Math.min(maxHeight, 5 + Math.floor(random() * 2) + (Math.abs(spread) < 1 ? 2 : 0));
    const tipBoost = random() < 0.2 ? 1 : 0;
    const bladePixels = [];
    for (let step = 0; step < height; step += 1) {
      const t = step / (height - 1);
      const dx = rootX + Math.round(slope * step);
      if (t < 0.6) pixels.push([dx + 1, -step, shade(ramp, level - 4.5)]);
      const tone = level - 3.4 + t * 3.4 + (step === height - 1 ? tipBoost : 0);
      bladePixels.push([dx, -step, shade(ramp, tone)]);
    }
    pixels.push(...bladePixels);
  }
  return pixels;
}

function tuftCount() {
  return Math.round(8 + state.density * 2);
}

// Distance to the nearest repeat of (dx, dy) on the isometric lattice.
function latticeDistance(dx, dy) {
  let best = Infinity;
  for (let a = -2; a <= 2; a += 1) {
    for (let b = -2; b <= 2; b += 1) {
      const x = dx + (a + b) * TILE_WIDTH / 2;
      const y = dy + (a - b) * TILE_HEIGHT / 2;
      best = Math.min(best, x * x + y * y * 2.2);
    }
  }
  return best;
}

// Best-candidate sampling: even coverage without the lines a jittered grid shows.
// Only rows 0–15 are needed because each tuft's second copy lands 16 rows lower.
function spreadTufts(random, count) {
  const points = [];
  for (let index = 0; index < count; index += 1) {
    let chosen;
    let chosenDistance = -1;
    for (let candidate = 0; candidate < 8; candidate += 1) {
      const point = [Math.floor(random() * TILE_WIDTH), Math.floor(random() * TILE_HEIGHT / 2)];
      const distance = Math.min(Infinity, ...points.map(([x, y]) => latticeDistance(point[0] - x, point[1] - y)));
      if (distance > chosenDistance) {
        chosen = point;
        chosenDistance = distance;
      }
    }
    points.push(chosen);
  }
  return points;
}

function renderSharedLayer(seed, colors) {
  const layer = createLayer();
  const brightness = (state.brightness - 58) / 30;
  const ramp = colors.ramp;
  for (let y = 0; y < TILE_HEIGHT; y += 1) {
    for (let x = 0; x < TILE_WIDTH; x += 1) {
      const dither = (BAYER_4X4[(y % 4) * 4 + (x % 4)] + 0.5) / 16 - 0.5;
      setPixel(layer, x, y, shade(ramp, 1.3 + meadowLight(x, y, seed) * 1.2 + brightness * 0.5 + dither * 0.7));
    }
  }

  const random = randomGenerator(seed);
  for (const [x, y] of spreadTufts(random, tuftCount())) {
    const level = 5.3 + meadowLight(x, y, seed) * 1.6 + (random() - 0.5) * 1.6 + brightness;
    paintWrapped(layer, x, y, grassTuft(random, level, ramp));
  }
  return layer;
}

function dirtPatch(random, ramp) {
  const radiusX = 2.6 + random() * 2;
  const radiusY = 1.4 + random() * 0.8;
  const inside = new Set();
  for (let dy = -3; dy <= 3; dy += 1) {
    for (let dx = -6; dx <= 6; dx += 1) {
      const distance = (dx / radiusX) ** 2 + (dy / radiusY) ** 2 + (random() - 0.5) * 0.45;
      if (distance <= 1) inside.add(`${dx},${dy}`);
    }
  }
  const [light, base, dark, deep] = DIRT_COLORS;
  const pixels = [];
  for (const key of inside) {
    const [dx, dy] = key.split(',').map(Number);
    let color = random() < 0.22 ? light : base;
    if (!inside.has(`${dx},${dy + 1}`)) color = dark;
    if (!inside.has(`${dx},${dy - 1}`)) color = deep;
    pixels.push([dx, dy, color, DIRT_DEPTH]);
    if (!inside.has(`${dx},${dy + 1}`)) pixels.push([dx, dy + 1, ramp[0], DIRT_DEPTH]);
  }
  return { pixels, radiusX, radiusY };
}

function renderGrassTile(seed, sharedLayer, colors) {
  const layer = { pixels: sharedLayer.pixels.slice(), depth: sharedLayer.depth.slice() };
  const random = randomGenerator(seed);
  const ramp = colors.ramp;
  const brightness = (state.brightness - 58) / 30;
  const interior = [];
  for (let y = 0; y < TILE_HEIGHT; y += 1) {
    for (let x = 0; x < TILE_WIDTH; x += 1) {
      if (edgeDepth(x, y) >= 0.22) interior.push([x, y]);
    }
  }
  const pick = () => interior[Math.floor(random() * interior.length)];
  const tuftAt = (x, y, maxHeight) => {
    const level = 5.3 + meadowLight(x, y, seed) * 1.6 + (random() - 0.5) * 1.6 + brightness;
    return paintInside(layer, x, y, grassTuft(random, level, ramp, maxHeight), 0.04);
  };

  for (let tuft = 0; tuft < Math.round(tuftCount() * 0.3); tuft += 1) tuftAt(...pick());

  if (state.dirt && random() < 0.85) {
    const [x, y] = pick();
    const patch = dirtPatch(random, ramp);
    if (paintInside(layer, x, y, patch.pixels, 0.08)) {
      // The patch replaces the grass under it, then sits at ground level again.
      for (const [dx, dy] of patch.pixels) layer.depth[(y + dy) * TILE_WIDTH + x + dx] = GROUND_DEPTH;
      // Blades in front of the patch overlap its lower rim.
      for (let dx = -Math.floor(patch.radiusX); dx <= patch.radiusX; dx += 2 + Math.floor(random() * 2)) {
        tuftAt(x + dx, y + Math.round(patch.radiusY) + 2, 2);
      }
    }
  }

  for (let y = 0; y < TILE_HEIGHT; y += 1) {
    for (let x = 0; x < TILE_WIDTH; x += 1) {
      if (!isInsideDiamond(x, y)) layer.pixels[(y * TILE_WIDTH + x) * 4 + 3] = 0;
    }
  }
  return layer.pixels;
}

function createGrassTile(seed, sharedLayer, colors) {
  const canvas = document.createElement('canvas');
  canvas.width = TILE_WIDTH;
  canvas.height = TILE_HEIGHT;
  const context = canvas.getContext('2d');
  const pixels = context.createImageData(TILE_WIDTH, TILE_HEIGHT);
  pixels.data.set(renderGrassTile(seed, sharedLayer, colors));
  context.putImageData(pixels, 0, 0);
  return canvas;
}

function materialCellHash(x, y, seed) {
  let hash = Math.imul(x + seed, 374761393) ^ Math.imul(y + seed, 668265263);
  hash = Math.imul(hash ^ (hash >>> 13), 1274126177);
  return (hash ^ (hash >>> 16)) >>> 0;
}

function createMaterialTile(seed, colors, material) {
  const canvas = document.createElement('canvas');
  canvas.width = TILE_WIDTH;
  canvas.height = TILE_HEIGHT;
  const context = canvas.getContext('2d');
  const image = context.createImageData(TILE_WIDTH, TILE_HEIGHT);
  const ramp = colors.ramp;
  const brightness = (state.brightness - 58) / 30;

  for (let y = 0; y < TILE_HEIGHT; y += 1) {
    for (let x = 0; x < TILE_WIDTH; x += 1) {
      if (!isInsideDiamond(x, y)) continue;
      const level = materialLevel(material, x, y, seed, brightness);
      const color = shade(ramp, level + (BAYER_4X4[(y % 4) * 4 + (x % 4)] / 15 - 0.5) * 0.35);
      const offset = (y * TILE_WIDTH + x) * 4;
      image.data[offset] = color[0];
      image.data[offset + 1] = color[1];
      image.data[offset + 2] = color[2];
      image.data[offset + 3] = 255;
    }
  }

  context.putImageData(image, 0, 0);
  return canvas;
}

// The surface and side faces share this material ramp, so they match from every angle.
// `face` is set for side faces: roofing keeps its roll seam off the slab edges.
function materialLevel(material, x, y, seed, brightness, face = false) {
  const u = (x + 0.5) / 32 + (y + 0.5) / 16;
  const v = (x + 0.5) / 32 - (y + 0.5) / 16;
  let level;

  if (material === 'planks') {
    const board = Math.floor(v * 2);
    const across = v * 2 - board;
    const joint = ((u + (board % 2) * 0.5) % 2 + 2) % 2;
    const grain = isoNoise(x + 0.5, y + 0.5, 12, 4, seed);
    const streak = isoNoise(x + 0.5, y + 0.5, 22, 3, seed ^ 0x4f1bbcdc);
    level = 2.8 + (grain - 0.5) * 1.5 + (streak - 0.5) * 0.9 + brightness;
    if (joint < 0.11 || across < 0.035 || across > 0.965) level = 0.35 + brightness * 0.25;
    else if (across < 0.11) level += 0.8;
  } else if (material === 'concrete') {
    const broad = isoNoise(x + 0.5, y + 0.5, 7, 7, seed);
    const fine = isoNoise(x + 0.5, y + 0.5, 19, 15, seed ^ 0x27d4eb2d);
    const blockX = Math.floor((x % (TILE_WIDTH / 2)) / 2);
    const blockY = Math.floor((y % (TILE_HEIGHT / 2)) / 2);
    const aggregate = materialCellHash(blockX, blockY, seed);
    const localX = x % 2;
    const localY = y % 2;
    level = 3.3 + (broad - 0.5) * 0.9 + (fine - 0.5) * 0.7 + brightness;
    if (aggregate % 23 === 0 && (localX === ((aggregate >>> 5) & 1) || localY === ((aggregate >>> 6) & 1))) {
      level += (aggregate & 1) === 0 ? 1.5 : -1.1;
    }
  } else if (material === 'asphalt') {
    // Per-pixel speckle has no structure to break at a seam; only the noise needs to wrap.
    const worn = isoNoise(x + 0.5, y + 0.5, 5, 5, seed);
    const fine = isoNoise(x + 0.5, y + 0.5, 17, 13, seed ^ 0x165667b1);
    const grain = materialCellHash(x, y, seed ^ 0x3c6ef372) % 100;
    level = 3.5 + (worn - 0.5) * 0.8 + (fine - 0.5) * 0.5 + brightness;
    if (grain < 9) level += 1.6;
    else if (grain < 13) level -= 1.2;
    else if (grain === 99) level += 3;
  } else if (material === 'roofing') {
    // v has period 2 per tile; the seam runs through the centre line (v = 0), off the tile edges.
    const roll = (((v + 1) % 2) + 2) % 2;
    const broad = isoNoise(x + 0.5, y + 0.5, 4, 4, seed);
    const fine = isoNoise(x + 0.5, y + 0.5, 15, 15, seed ^ 0x2545f491);
    const grit = materialCellHash(x, y, seed ^ 0x6c8e9cf5) % 100;
    level = 3.9 + (broad - 0.5) * 0.7 + (fine - 0.5) * 0.4 + brightness;
    if (!face && roll >= 1 && roll < 1.06) level -= 1.3;
    else if (!face && roll >= 1.06 && roll < 1.16) level += 0.7;
    if (grit < 5) level += 1.2;
    else if (grit < 8) level -= 0.8;
  } else if (material === 'brick') {
    // Pavers: 4 courses across v, 2 bricks along u, odd courses offset half a brick.
    const course = Math.floor((v + 1) * 2);
    const across = (v + 1) * 2 - course;
    const along = ((u + (course % 2) * 0.5) % 1 + 1) % 1;
    const tone = materialCellHash(Math.floor(u + (course % 2) * 0.5) & 1, course & 3, seed) % 5;
    level = 3.9 + (tone - 2) * 0.35 + (isoNoise(x + 0.5, y + 0.5, 13, 13, seed) - 0.5) * 0.6 + brightness;
    if (across < 0.06 || along < 0.03) level = 6.2 + brightness * 0.25;
  } else {
    const gridU = u * 4;
    const gridV = v * 4;
    const cellU = Math.floor(gridU);
    const cellV = Math.floor(gridV);
    let nearest = Infinity;
    let nextNearest = Infinity;
    let stoneTone = 3;

    for (let offsetV = -1; offsetV <= 1; offsetV += 1) {
      for (let offsetU = -1; offsetU <= 1; offsetU += 1) {
        const candidateU = cellU + offsetU;
        const candidateV = cellV + offsetV;
        const wrappedU = ((candidateU % 8) + 8) % 8;
        const wrappedV = ((candidateV % 8) + 8) % 8;
        const hash = materialCellHash(wrappedU, wrappedV, seed);
        const pointU = candidateU + 0.2 + ((hash & 0xffff) / 0xffff) * 0.6;
        const pointV = candidateV + 0.2 + (((hash >>> 16) & 0xffff) / 0xffff) * 0.6;
        const deltaU = (gridU - pointU) / 4;
        const deltaV = (gridV - pointV) / 4;
        const distance = Math.hypot((deltaU + deltaV) * 32, (deltaU - deltaV) * 16);
        if (distance < nearest) {
          nextNearest = nearest;
          nearest = distance;
          stoneTone = 2 + ((hash >>> 8) % 3);
        } else if (distance < nextNearest) {
          nextNearest = distance;
        }
      }
    }

    const fleck = isoNoise(x + 0.5, y + 0.5, 11, 11, seed ^ 0x27d4eb2d);
    level = stoneTone + (fleck - 0.5) * 0.9 + brightness;
    if (nextNearest - nearest < 1.25) level = 0.35 + brightness * 0.25;
    else if (nearest < 1.3) level += 0.55;
  }

  return level;
}

function paintTexture() {
  const seed = seedNumber(state.seed);
  const colors = state.customPalette || activePaletteColors();
  textureCanvases = [];
  sideCanvases = [];
  if (state.material === 'furniture') {
    const ramps = furnitureRamps(colors);
    const spriteRamps = isPixelStyle() ? pixelArtRamps(ramps) : null;
    for (let index = 0; index < VARIATION_COUNT; index += 1) {
      const variantSeed = variantSeedFor(seed, index);
      textureCanvases.push(spriteRamps
        ? createPixelSprite(state.furniture, spriteRamps, variantSeed)
        : createFurnitureSprite(FURNITURE[state.furniture], ramps, variantSeed));
    }
    textureCanvas = textureCanvases[0];
    textureAtlas = createAtlas(textureCanvases);
    sideAtlas = null;
    sideSheetTexture = null;
    sideBlockCanvases = [];
    textureBlock = textureCanvas;
    drawPreview();
    updateReadouts();
    return;
  }
  const sharedLayer = state.material === 'grass'
    ? renderSharedLayer(seedNumber(state.seed + ':shared-edge'), colors)
    : null;
  for (let index = 0; index < VARIATION_COUNT; index += 1) {
    const variantSeed = variantSeedFor(seed, index);
    textureCanvases.push(state.material === 'grass'
      ? createGrassTile(variantSeed, sharedLayer, colors)
      : createMaterialTile(variantSeed, colors, state.material));
    sideCanvases.push(createSideTextures(colors, variantSeed));
  }
  textureCanvas = textureCanvases[0];
  textureSides = sideCanvases[0];
  textureAtlas = createAtlas(textureCanvases);
  sideAtlas = createAtlas(sideCanvases);
  textureBlock = createBlockTexture(textureCanvas, textureSides);
  const sideCells = textureCanvases.map((tile, variant) => {
    const variantSeed = variantSeedFor(seed, variant);
    const tilePixels = tile.getContext('2d').getImageData(0, 0, TILE_WIDTH, TILE_HEIGHT).data;
    return {
      tile,
      cap: createSideCell(colors, variantSeed, seed, state.material, tilePixels, 'cap'),
      fill: createSideCell(colors, variantSeed, seed, state.material, tilePixels, 'fill'),
    };
  });
  sideSheetTexture = createSideSheetTexture(sideCells);
  sideBlockCanvases = sideCells.map((cell) => createSideBlockTexture(cell.tile, cell.cap, cell.fill, 1));
  drawPreview();
  updateReadouts();
}

function createAtlas(tiles) {
  const canvas = document.createElement('canvas');
  canvas.width = tiles[0].width * tiles.length;
  canvas.height = tiles[0].height;
  const context = canvas.getContext('2d');
  context.imageSmoothingEnabled = false;
  tiles.forEach((tile, index) => context.drawImage(tile, index * tile.width, 0));
  return canvas;
}

function createSideTextures(colors, seed) {
  const canvas = document.createElement('canvas');
  canvas.width = TILE_WIDTH;
  canvas.height = SIDE_REPEAT_HEIGHT;
  const context = canvas.getContext('2d');
  const pixels = context.createImageData(canvas.width, canvas.height);
  const faces = [
    { xOffset: 0, light: 0.82 },
    { xOffset: TILE_WIDTH / 2, light: 0.66 },
  ];

  for (const face of faces) {
    const base = colors.ramp[3].map((channel) => Math.round(channel * face.light));
    for (let y = 0; y < SIDE_REPEAT_HEIGHT; y += 1) {
      for (let x = 0; x < TILE_WIDTH / 2; x += 1) {
        const grain = ((Math.imul(x + 1, 374761393) ^ Math.imul(y + seed, 668265263)) >>> 0) / 4294967295;
        const color = base.map((channel) => Math.max(0, Math.min(255, Math.round(channel * (0.94 + grain * 0.12)))));
        const offset = y * canvas.width * 4 + (face.xOffset + x) * 4;
        pixels.data[offset] = color[0];
        pixels.data[offset + 1] = color[1];
        pixels.data[offset + 2] = color[2];
        pixels.data[offset + 3] = 255;
      }
    }
  }

  context.putImageData(pixels, 0, 0);
  return canvas;
}

// --- Game side sheet (128 × 32) --------------------------------------------
// Each material has four 32 px variants, each with a 32 × 16 cap and fill.
// The cap sits under the slab edge; the fill repeats down taller faces. The
// sheet stays flat and unlit: the game applies face lighting and mirrors it.

const SIDE_CELL_WIDTH = TILE_WIDTH / 2;
const SIDE_CELL_HEIGHT = SIDE_REPEAT_HEIGHT;
const SIDE_SHEET_WIDTH = SIDE_CELL_WIDTH * VARIATION_COUNT;
const SIDE_SHEET_HEIGHT = SIDE_CELL_HEIGHT * 2;

// Return the diamond's bottom row in column x so the cap meets the tile exactly.
function diamondBottomRow(x) {
  if (x < TILE_WIDTH / 2) {
    return x <= 0 ? TILE_HEIGHT / 2 - 1 : TILE_HEIGHT / 2 + Math.floor((x - 1) / 2);
  }
  return diamondBottomRow(TILE_WIDTH - 1 - x);
}

// Sample upward from the diamond edge so the cap continues the tile texture.
// Near corners, repeat the last tone with a subtle ramp dither.
function sideColumnSamples(colors, tilePixels, x) {
  const ramp = colors.ramp;
  const sourceX = x === 0 ? 1 : x;
  const edge = diamondBottomRow(sourceX);
  const samples = [];
  let last = ramp[3];
  for (let row = 0; row < TILE_HEIGHT; row += 1) {
    const y = ((edge - row) % TILE_HEIGHT + TILE_HEIGHT) % TILE_HEIGHT;
    const offset = (y * TILE_WIDTH + sourceX) * 4;
    if (tilePixels[offset + 3] > 0) {
      last = [tilePixels[offset], tilePixels[offset + 1], tilePixels[offset + 2]];
      samples.push({ color: last, real: true });
      continue;
    }
    const dither = BAYER_4X4[(row % 4) * 4 + (x % 4)] / 15 - 0.5;
    samples.push({ color: shade(ramp, rampLevelFor(ramp, last) + dither * 0.5), real: false });
  }
  return samples;
}

// Find the nearest ramp index to preserve tone when repeating samples.
function rampLevelFor(ramp, color) {
  let best = 0;
  let bestDistance = Infinity;
  ramp.forEach((entry, index) => {
    const distance = (entry[0] - color[0]) ** 2 + (entry[1] - color[1]) ** 2 + (entry[2] - color[2]) ** 2;
    if (distance < bestDistance) {
      bestDistance = distance;
      best = index;
    }
  });
  return best;
}

// Grass lip with broad clumps and hanging tips, tapered at the cell edges
// so neighbouring variants join cleanly.
function grassLipDepth(random, count) {
  const depth = [];
  for (let x = 0; x < count; x += 1) depth.push(1 + (random() < 0.5 ? 1 : 0));
  for (let clump = 0; clump < 2; clump += 1) {
    const center = random() * count;
    const span = 4 + random() * 6;
    const add = 1 + random() * 1.6;
    for (let x = 0; x < count; x += 1) {
      const distance = Math.min(Math.abs(x - center), count - Math.abs(x - center));
      if (distance < span) depth[x] += add * (1 - distance / span);
    }
  }
  for (let x = 0; x < count; x += 1) depth[x] = Math.round(depth[x]);
  for (let blade = 0; blade < Math.round(count * 0.22); blade += 1) {
    depth[Math.floor(random() * count)] += 1 + Math.floor(random() * 2);
  }
  for (let x = 0; x < count; x += 1) {
    depth[x] = Math.min(depth[x], 2 + Math.min(x, count - 1 - x));
  }
  return depth.map((value) => Math.max(0, Math.min(value, 8)));
}

// Soil below the lip reuses the ground-speck tones and varies by face row,
// keeping the cap and fill continuous.
function sideSoilColor(x, faceRow, seed) {
  const grain = materialCellHash(x, faceRow % 32, seed) / 4294967295;
  const blotch = isoNoise(x + 0.5, (faceRow % 32) + 0.5, 5, 4, seed ^ 0x27d4eb2d);
  const level = grain * 0.55 + blotch * 0.45;
  if (level < 0.22) return DIRT_COLORS[3];
  if (level < 0.45) return DIRT_COLORS[2];
  if (level < 0.78) return DIRT_COLORS[1];
  return DIRT_COLORS[0];
}

// Find where surface plank joints cross the edge using the same ramp formula.
function plankSideJointColumns() {
  const columns = [];
  for (let x = 0; x < SIDE_CELL_WIDTH; x += 1) {
    const edge = diamondBottomRow(x === 0 ? 1 : x);
    const u = (x + 0.5) / 32 + (edge + 0.5) / 16;
    const v = (x + 0.5) / 32 - (edge + 0.5) / 16;
    const board = Math.floor(v * 2);
    const joint = ((u + (board % 2) * 0.5) % 2 + 2) % 2;
    if (joint < 0.11) columns.push(x);
  }
  return columns;
}

// Plank edges use horizontal grain, 8 px joints, and vertical joint caps.
function plankSideColor(colors, samples, x, faceRow, seed, jointColumn, brightness) {
  const ramp = colors.ramp;
  const dither = BAYER_4X4[(faceRow % 4) * 4 + (x % 4)] / 15 - 0.5;
  const jointTone = 0.35 + brightness * 0.25 + dither * 0.3;
  if (jointColumn && faceRow > 0) {
    const broken = materialCellHash(x, faceRow, seed ^ 0x5bd1e995) % 100 < 22;
    if (!broken) return shade(ramp, jointTone);
  }
  if (faceRow === 0) return samples[0].color;
  if (faceRow % 8 === 7) return shade(ramp, jointTone);
  const grain = materialCellHash(x, faceRow % 32, seed) / 4294967295;
  return shade(ramp, 2.9 + (grain - 0.5) * 1.4 + brightness + dither * 0.35 + (grain > 0.86 ? 0.9 : 0));
}

// Fired bricks in running bond: 4 px courses (3 brick + 1 mortar), 8 px bricks (7 + 1
// mortar), odd courses offset 4 px. The cap's top course is a header course (4 px
// bricks). The brick straddling a cell edge uses the pattern seed so the halves of the
// two variants that meet keep the same tone. Every course repeats every 16 rows.
function brickSideColor(colors, x, faceRow, seed, patternSeed, part, brightness) {
  const ramp = colors.ramp;
  const course = Math.floor(faceRow / 4);
  const inCourse = faceRow % 4;
  const header = part === 'cap' && faceRow < 4;
  const offset = (course % 2) * 4;
  const step = header ? 4 : 8;
  const brick = Math.floor((x + offset) / step);
  const inBrick = (x + offset) % step;
  if (inCourse === 3 || inBrick === step - 1) return shade(ramp, 6.2 + brightness * 0.25);
  const straddles = !header && course % 2 === 1 && brick % 4 === 0;
  const toneSeed = straddles ? patternSeed : seed;
  const tone = materialCellHash(brick & 3, course, toneSeed) % 5;
  const speckle = (isoNoise(x + 0.5, faceRow + 0.5, 9, 7, toneSeed) - 0.5) * 0.5;
  let level = 3 + (tone - 2) * 0.35 + speckle + brightness;
  if (inCourse === 0) level += header ? 0.8 : 0.4;
  return shade(ramp, level);
}

function variantSeedFor(seed, index) {
  return (seed ^ Math.imul(index + 1, 0x9e3779b1)) >>> 0;
}

// Build a 32 × 16 cap or fill. The fill continues beneath any cap, while the
// material pattern seed keeps stone texture continuous across variants.
function createSideCell(colors, seed, patternSeed, material, tilePixels, part) {
  const data = new Uint8ClampedArray(SIDE_CELL_WIDTH * SIDE_CELL_HEIGHT * 4);
  const brightness = (state.brightness - 58) / 30;
  const random = randomGenerator((seed ^ (part === 'cap' ? 0x9e3779b1 : 0x85ebca6b)) >>> 0);
  const faceBase = part === 'cap' ? 0 : SIDE_CELL_HEIGHT;
  const lip = material === 'grass' && part === 'cap' ? grassLipDepth(random, SIDE_CELL_WIDTH) : null;
  const joints = material === 'planks' ? plankSideJointColumns() : [];
  const phase = seed % 4;

  for (let x = 0; x < SIDE_CELL_WIDTH; x += 1) {
    const samples = sideColumnSamples(colors, tilePixels, x);
    for (let row = 0; row < SIDE_CELL_HEIGHT; row += 1) {
      const faceRow = faceBase + row;
      let color;
      if (material === 'grass') {
        if (lip && row <= lip[x]) {
          color = samples[row].color;
        } else {
          color = sideSoilColor(x, faceRow, seed);
        }
      } else if (material === 'planks') {
        color = plankSideColor(colors, samples, x, faceRow, seed, joints.includes(x), brightness);
      } else if (material === 'brick') {
        color = brickSideColor(colors, x, faceRow, seed, patternSeed, part, brightness);
      } else {
        // Reuse the surface material formula along the face, aligned to the
        // diamond edge. Cobblestone swaps axes so stones do not break at seams.
        const surfaceX = material === 'cobblestone' ? faceRow : x;
        const surfaceY = material === 'cobblestone' ? x : faceRow;
        const dither = BAYER_4X4[((faceRow + phase) % 4) * 4 + ((x + phase) % 4)] / 15 - 0.5;
        color = faceRow === 0 && samples[0].real
          ? samples[0].color
          : shade(colors.ramp, materialLevel(material, surfaceX, surfaceY, patternSeed, brightness, true) + dither * 0.35);
      }
      const offset = (row * SIDE_CELL_WIDTH + x) * 4;
      data[offset] = color[0];
      data[offset + 1] = color[1];
      data[offset + 2] = color[2];
      data[offset + 3] = 255;
    }
  }
  return new ImageData(data, SIDE_CELL_WIDTH, SIDE_CELL_HEIGHT);
}

function createSideSheetTexture(cells) {
  const canvas = document.createElement('canvas');
  canvas.width = SIDE_SHEET_WIDTH;
  canvas.height = SIDE_SHEET_HEIGHT;
  const context = canvas.getContext('2d');
  context.imageSmoothingEnabled = false;
  cells.forEach((cell, variant) => {
    context.putImageData(cell.cap, variant * SIDE_CELL_WIDTH, 0);
    context.putImageData(cell.fill, variant * SIDE_CELL_WIDTH, SIDE_CELL_HEIGHT);
  });
  return canvas;
}

// Compose the tile, mirrored south/east caps, and fill as the game renders them.
// Face lighting belongs to composition, never to the texture sheet.
function createSideBlockTexture(tileCanvas, capCell, fillCell, fills) {
  const width = TILE_WIDTH;
  const height = TILE_HEIGHT + SIDE_CELL_HEIGHT * (1 + fills);
  const data = new Uint8ClampedArray(width * height * 4);
  const tile = tileCanvas.getContext('2d').getImageData(0, 0, TILE_WIDTH, TILE_HEIGHT).data;
  for (let y = 0; y < TILE_HEIGHT; y += 1) {
    for (let x = 0; x < TILE_WIDTH; x += 1) {
      const offset = (y * TILE_WIDTH + x) * 4;
      data[offset] = tile[offset];
      data[offset + 1] = tile[offset + 1];
      data[offset + 2] = tile[offset + 2];
      data[offset + 3] = tile[offset + 3];
    }
  }
  const put = (x, y, color, light) => {
    const offset = (y * width + x) * 4;
    data[offset] = Math.round(color[0] * light);
    data[offset + 1] = Math.round(color[1] * light);
    data[offset + 2] = Math.round(color[2] * light);
    data[offset + 3] = 255;
  };
  const cellColor = (cell, column, row) => {
    const offset = (row * SIDE_CELL_WIDTH + column) * 4;
    return [cell.data[offset], cell.data[offset + 1], cell.data[offset + 2]];
  };
  for (let x = 0; x < width; x += 1) {
    const south = x < TILE_WIDTH / 2;
    const column = south ? x : width - 1 - x;
    const light = south ? 0.82 : 0.66;
    const top = diamondBottomRow(x) + 1;
    for (let row = 0; row < SIDE_CELL_HEIGHT; row += 1) {
      put(x, top + row, cellColor(capCell, column, row), light);
      for (let fill = 0; fill < fills; fill += 1) {
        put(x, top + SIDE_CELL_HEIGHT * (1 + fill) + row, cellColor(fillCell, column, row), light);
      }
    }
  }
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  context.imageSmoothingEnabled = false;
  const image = context.createImageData(width, height);
  image.data.set(data);
  context.putImageData(image, 0, 0);
  return canvas;
}

function createBlockTexture(surface, sides) {
  const canvas = document.createElement('canvas');
  canvas.width = TILE_WIDTH;
  canvas.height = TILE_HEIGHT + SIDE_REPEAT_HEIGHT;
  const context = canvas.getContext('2d');
  const pixels = context.createImageData(canvas.width, canvas.height);
  const sidePixels = sides.getContext('2d').getImageData(0, 0, TILE_WIDTH, SIDE_REPEAT_HEIGHT).data;

  for (let y = TILE_HEIGHT / 2; y < canvas.height; y += 1) {
    const depth = y - TILE_HEIGHT / 2;
    for (let x = 0; x < TILE_WIDTH; x += 1) {
      let sideX = -1;
      if (y < TILE_HEIGHT) {
        const width = depth * 2 + 1;
        if (x < width) sideX = x;
        if (x >= TILE_WIDTH - width) sideX = TILE_WIDTH / 2 + x % (TILE_WIDTH / 2);
      } else if (x < TILE_WIDTH / 2) {
        sideX = x;
      } else {
        sideX = TILE_WIDTH / 2 + x % (TILE_WIDTH / 2);
      }
      if (sideX < 0) continue;

      const offset = (y * TILE_WIDTH + x) * 4;
      const sideOffset = ((y % SIDE_REPEAT_HEIGHT) * TILE_WIDTH + sideX) * 4;
      pixels.data[offset] = sidePixels[sideOffset];
      pixels.data[offset + 1] = sidePixels[sideOffset + 1];
      pixels.data[offset + 2] = sidePixels[sideOffset + 2];
      pixels.data[offset + 3] = 255;
    }
  }
  context.putImageData(pixels, 0, 0);
  context.drawImage(surface, 0, 0);
  return canvas;
}

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

const FURNITURE_ROLES = {
  wood: { ramp: 'wood', level: 4.2, grain: 0.85, cells: [1.7, 5.2], face: [0.5, -1.15, -2.3] },
  woodDark: { ramp: 'wood', level: 2.5, grain: 0.7, cells: [1.9, 5.4], face: [0.5, -1.15, -2.3] },
  woodLight: { ramp: 'wood', level: 5.8, grain: 0.85, cells: [1.7, 5.2], face: [0.45, -1.2, -2.4] },
  metal: { ramp: 'iron', level: 4.3, grain: 0.4, cells: [2.4, 2.4], face: [0.7, -1.5, -2.8] },
  fabric: { ramp: 'fabric', level: 4.5, grain: 0.45, cells: [4.4, 4.4], face: [0.5, -1.0, -2.1] },
  sheet: { ramp: 'linen', level: 4.8, grain: 0.35, cells: [4.4, 4.4], face: [0.4, -0.9, -1.9] },
  pillow: { ramp: 'linen', level: 6.4, grain: 0.3, cells: [4.0, 4.0], face: [0.4, -0.9, -1.9] },
  blanket: { ramp: 'blanket', level: 4.7, grain: 0.45, cells: [4.2, 4.2], face: [0.5, -1.0, -2.1] },
  blanket2: { ramp: 'blanket', level: 5.5, grain: 0.45, cells: [4.2, 4.2], face: [0.5, -1.0, -2.1] },
  glow: { ramp: 'glow', level: 6.1, grain: 0.25, cells: [3.0, 3.0], face: [0.35, 0.05, -0.5] },
  leaf: { ramp: 'leaf', level: 4.5, grain: 1.15, cells: [3.8, 3.8], face: [0.6, -1.3, -2.6] },
  soil: { ramp: 'soil', level: 1.8, grain: 0.7, cells: [3.4, 3.4], face: [0.5, -1.0, -2.0] },
  shadow: { ramp: 'wood', level: 0.6, grain: 0.35, cells: [2.6, 2.6], face: [0.4, -0.8, -1.6] },
  book0: { ramp: 'book0', level: 5.1, grain: 0.5, cells: [4.2, 4.2], face: [0.5, -1.2, -2.3] },
  book1: { ramp: 'book1', level: 5.1, grain: 0.5, cells: [4.2, 4.2], face: [0.5, -1.2, -2.3] },
  book2: { ramp: 'book2', level: 5.1, grain: 0.5, cells: [4.2, 4.2], face: [0.5, -1.2, -2.3] },
  book3: { ramp: 'book3', level: 5.1, grain: 0.5, cells: [4.2, 4.2], face: [0.5, -1.2, -2.3] },
};

const FURNITURE = {
  table: {
    name: 'Table',
    subtitle: 'Isometric furniture',
    corner: 'PIXEL TABLE',
    description: 'Voxel table with a 4 × 4 top, apron, and four legs.',
    boxes: [
      { from: [0, 0, 0], to: [1, 1, 3], role: 'wood', at: [[0, 0], [3, 0], [0, 3], [3, 3]] },
      { from: [0, 0, 3], to: [4, 4, 4], role: 'wood' },
      { from: [0, 0, 3], to: [4, 1, 4], role: 'woodLight' },
      { from: [0, 3, 3], to: [4, 4, 4], role: 'woodLight' },
      { from: [0, 1, 3], to: [1, 3, 4], role: 'woodLight' },
      { from: [3, 1, 3], to: [4, 3, 4], role: 'woodLight' },
      { from: [0, 0, 2], to: [4, 1, 3], role: 'woodDark' },
      { from: [0, 3, 2], to: [4, 4, 3], role: 'woodDark' },
      { from: [0, 1, 2], to: [1, 3, 3], role: 'woodDark' },
      { from: [3, 1, 2], to: [4, 3, 3], role: 'woodDark' },
    ],
  },
  chair: {
    name: 'Chair',
    subtitle: 'Isometric furniture',
    corner: 'PIXEL CHAIR',
    description: 'Voxel chair with a light seat, four legs, and open backrest.',
    boxes: [
      { from: [0, 0, 0], to: [1, 1, 2], role: 'wood', at: [[0, 0], [2, 0], [0, 1], [2, 1]] },
      { from: [0, 0, 2], to: [3, 2, 3], role: 'woodLight' },
      { from: [0, 0, 3], to: [1, 1, 5], role: 'woodDark' },
      { from: [2, 0, 3], to: [3, 1, 5], role: 'woodDark' },
      { from: [0, 0, 4], to: [3, 1, 5], role: 'woodDark' },
    ],
  },
  bed: {
    name: 'Bed',
    subtitle: 'Isometric furniture',
    corner: 'PIXEL BED',
    description: 'Voxel bed with a headboard, light mattress, and two-tone folded blanket.',
    boxes: [
      { from: [0, 0, 0], to: [1, 2, 4], role: 'woodDark' },
      { from: [1, 0, 0], to: [4, 2, 1], role: 'wood' },
      { from: [1, 0, 1], to: [4, 2, 2], role: 'sheet' },
      { from: [1, 0, 2], to: [2, 2, 3], role: 'pillow' },
      { from: [2, 0, 2], to: [4, 2, 3], role: 'blanket' },
    ],
    decorate(voxels) {
      for (let y = 0; y < 2; y += 1) {
        const stripe = voxels.get(`3,${y},2`);
        if (stripe) stripe.role = 'blanket2';
      }
    },
  },
  chest: {
    name: 'Chest',
    subtitle: 'Isometric furniture',
    corner: 'PIXEL CHEST',
    description: 'Voxel chest with metal braces, a fitted lid, and a front lock.',
    boxes: [
      { from: [0, 0, 0], to: [3, 2, 2], role: 'wood' },
      { from: [0, 0, 2], to: [3, 2, 3], role: 'woodDark' },
      { from: [0, 1, 0], to: [1, 2, 1], role: 'metal' },
      { from: [2, 1, 0], to: [3, 2, 1], role: 'metal' },
      { from: [1, 0, 2], to: [2, 2, 3], role: 'metal' },
      { from: [1, 1, 1], to: [2, 2, 3], role: 'metal' },
    ],
  },
  shelf: {
    name: 'Shelf',
    subtitle: 'Isometric furniture',
    corner: 'PIXEL SHELF',
    description: 'Voxel shelf with shelves and seed-varied colored books.',
    boxes: [
      { from: [0, 0, 0], to: [4, 2, 1], role: 'woodDark' },
      { from: [0, 0, 4], to: [4, 2, 5], role: 'woodDark' },
      { from: [0, 0, 0], to: [1, 2, 5], role: 'woodDark' },
      { from: [3, 0, 0], to: [4, 2, 5], role: 'woodDark' },
      { from: [1, 0, 2], to: [3, 2, 3], role: 'woodDark' },
      { from: [1, 0, 1], to: [3, 1, 2], role: 'shadow' },
      { from: [1, 0, 3], to: [3, 1, 4], role: 'shadow' },
    ],
    decorate(voxels, seed) {
      for (let x = 1; x < 3; x += 1) {
        for (const z of [1, 3]) {
          const hash = furnitureVoxelHash(x, z, 9, seed);
          if (hash % 100 >= 86) continue;
          voxels.set(`${x},1,${z}`, { x, y: 1, z, role: `book${(hash >>> 8) % 4}` });
        }
      }
    },
  },
  lamp: {
    name: 'Lamp',
    subtitle: 'Isometric furniture',
    corner: 'PIXEL LAMP',
    description: 'Voxel lamp with a base, central stem, and warm glowing shade.',
    boxes: [
      { from: [0, 0, 0], to: [2, 2, 1], role: 'woodDark' },
      { from: [0, 0, 1], to: [1, 1, 3], role: 'wood' },
      { from: [0, 0, 3], to: [2, 2, 4], role: 'glow' },
      { from: [0, 0, 4], to: [2, 2, 5], role: 'woodDark' },
    ],
  },
  plant: {
    name: 'Plant',
    subtitle: 'Isometric furniture',
    corner: 'PIXEL PLANT',
    description: 'Voxel planter with soil and seed-generated foliage.',
    boxes: [
      { from: [0, 0, 0], to: [3, 3, 1], role: 'woodDark', chamfer: true },
      { from: [0, 0, 1], to: [3, 3, 2], role: 'woodDark' },
      { from: [0, 0, 2], to: [3, 3, 3], role: 'soil', chamfer: true },
    ],
    measure: [{ from: [0, 0, 3], to: [3, 3, 5] }],
    decorate(voxels, seed) {
      for (let x = 0; x < 3; x += 1) {
        for (let y = 0; y < 3; y += 1) {
          if (furnitureVoxelHash(x, y, 3, seed) % 100 < 64) voxels.set(`${x},${y},3`, { x, y, z: 3, role: 'leaf' });
          if (furnitureVoxelHash(x, y, 5, seed) % 100 < 38) voxels.set(`${x},${y},4`, { x, y, z: 4, role: 'leaf' });
        }
      }
      voxels.set('1,1,4', { x: 1, y: 1, z: 4, role: 'leaf' });
    },
  },
  barrel: {
    name: 'Barrel',
    subtitle: 'Isometric furniture',
    corner: 'PIXEL BARREL',
    description: 'Voxel barrel with beveled staves and a central metal band.',
    boxes: [
      { from: [0, 0, 0], to: [3, 3, 3], role: 'wood' },
      { from: [0, 0, 0], to: [1, 1, 4], role: 'woodDark' },
      { from: [2, 0, 0], to: [3, 1, 4], role: 'woodDark' },
      { from: [0, 2, 0], to: [1, 3, 4], role: 'woodDark' },
      { from: [2, 2, 0], to: [3, 3, 4], role: 'woodDark' },
      { from: [0, 0, 1], to: [3, 3, 2], role: 'metal' },
      { from: [0, 0, 3], to: [3, 3, 4], role: 'woodDark' },
      { from: [1, 1, 3], to: [2, 2, 4], role: 'wood' },
    ],
  },
};

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

function updateSpecText(isFurniture) {
  const size = currentSize();
  document.querySelector('#tile-spec-size').textContent = `${size.width} × ${size.height} px`;
  document.querySelector('#tile-spec-caption').textContent = isFurniture
    ? `${FURNITURE[state.furniture].name} · 4 variants`
    : '1 tile · 2:1 ratio';
  const pixel = isPixelStyle();
  document.querySelector('#face-a-label').textContent = pixel ? 'OUTLINE' : isFurniture ? 'TOP' : 'SOUTH';
  document.querySelector('#face-a-value').textContent = pixel ? '1 PX' : isFurniture ? '100%' : '82%';
  document.querySelector('#face-b-label').textContent = pixel ? 'RAMP' : isFurniture ? 'SIDES' : 'EAST';
  document.querySelector('#face-b-value').textContent = pixel ? '8 TONES' : isFurniture ? '78%' : '66%';
  document.querySelector('#face-note').textContent = pixel
    ? 'Top-left light · cast shadow · hue-shifted tones'
    : isFurniture
      ? '16 × 8 px voxel · top, front, and side faces'
      : '32 × 16 px faces · 4× sheet with edge caps and repeating fill';
  document.querySelector('#stage-mode').innerHTML = pixel
    ? '<i></i> PIXEL ART SPRITE <b>WITH ALPHA</b>'
    : isFurniture
      ? '<i></i> ISOMETRIC PROP <b>WITH ALPHA</b>'
      : state.view === 'sides'
        ? '<i></i> SIDES 128 × 32 <b>CAP + FILL</b>'
        : '<i></i> ISOMETRIC TILING <b>SEAMLESS</b>';
  document.querySelector('#export-sides').hidden = isFurniture;
  document.querySelector('#export-sides-sheet').hidden = isFurniture;
}

for (const piece of Object.values(FURNITURE)) {
  const measured = buildFurnitureVoxels(piece, 0x51eed);
  addFurnitureBoxes(measured, piece.measure || []);
  piece.bounds = furnitureBounds(measured);
  piece.size = { width: piece.bounds.width, height: piece.bounds.height };
}

function drawPreview() {
  const edge = preview.width;
  previewContext.clearRect(0, 0, edge, edge);
  previewContext.imageSmoothingEnabled = false;
  const view = state.material === 'furniture' ? 'block' : state.view;
  const tiled = view === 'tiles';
  if (tiled) {
    const stepX = TILE_WIDTH * PREVIEW_SCALE / 2;
    const stepY = TILE_HEIGHT * PREVIEW_SCALE / 2;
    const variantsAt = new Map();
    const seed = seedNumber(state.seed);
    for (let row = -7; row <= 7; row += 1) {
      for (let column = -7; column <= 7; column += 1) {
        if ((column - row) % 2 !== 0) continue;
        const neighbors = new Set([
          variantsAt.get((column - 1) + ',' + (row - 1)),
          variantsAt.get((column + 1) + ',' + (row - 1)),
        ]);
        let hash = Math.imul(column + 31, 374761393) ^ Math.imul(row + 37, 668265263) ^ seed;
        hash = Math.imul(hash ^ (hash >>> 13), 1274126177) >>> 0;
        let variant = hash % VARIATION_COUNT;
        for (let attempt = 0; attempt < VARIATION_COUNT && neighbors.has(variant); attempt += 1) {
          variant = (variant + 1) % VARIATION_COUNT;
        }
        variantsAt.set(column + ',' + row, variant);
        previewContext.drawImage(
          textureCanvases[variant],
          edge / 2 - TILE_WIDTH * PREVIEW_SCALE / 2 + column * stepX,
          edge / 2 - TILE_HEIGHT * PREVIEW_SCALE / 2 + row * stepY,
          TILE_WIDTH * PREVIEW_SCALE,
          TILE_HEIGHT * PREVIEW_SCALE,
        );
      }
    }
  } else if (view === 'sides' && sideBlockCanvases.length) {
    // Show all four variants with their cap and fill as the lit game block.
    const blockWidth = TILE_WIDTH * PREVIEW_SCALE;
    const blockHeight = (TILE_HEIGHT + SIDE_CELL_HEIGHT * 2) * PREVIEW_SCALE;
    const gap = 24;
    const startX = Math.round((edge - blockWidth * 2 - gap) / 2);
    const startY = Math.round((edge - blockHeight * 2 - gap) / 2);
    sideBlockCanvases.forEach((block, variant) => {
      previewContext.drawImage(
        block,
        startX + (variant % 2) * (blockWidth + gap),
        startY + Math.floor(variant / 2) * (blockHeight + gap),
        blockWidth,
        blockHeight,
      );
    });
  } else if (isPixelStyle()) {
    // Pixel art sprites include their own shadow; scale them to fit the preview.
    const width = textureBlock.width;
    const height = textureBlock.height;
    const scale = Math.max(PREVIEW_SCALE, Math.min(8, Math.floor((edge * 0.62) / Math.max(width, height))));
    previewContext.drawImage(textureBlock, Math.round((edge - width * scale) / 2), Math.round((edge - height * scale) / 2), width * scale, height * scale);
  } else {
    const width = textureBlock.width;
    const height = textureBlock.height;
    const left = Math.round((edge - width * PREVIEW_SCALE) / 2);
    const top = Math.round((edge - height * PREVIEW_SCALE) / 2);
    if (state.material === 'furniture') {
      const piece = FURNITURE[state.furniture];
      const span = piece.bounds.spanX + piece.bounds.spanY;
      const shadowWidth = span * VOXEL_HALF * PREVIEW_SCALE;
      const shadowHeight = span * VOXEL_QUARTER * PREVIEW_SCALE;
      const shadowX = left + ((piece.bounds.spanX - piece.bounds.spanY) * VOXEL_QUARTER - piece.bounds.minX) * PREVIEW_SCALE;
      const shadowY = top + ((span * VOXEL_QUARTER) / 2 - piece.bounds.minY) * PREVIEW_SCALE;
      previewContext.drawImage(groundShadow(), Math.round(shadowX - shadowWidth / 2), Math.round(shadowY - shadowHeight / 2), shadowWidth, shadowHeight);
    }
    previewContext.drawImage(textureBlock, left, top, width * PREVIEW_SCALE, height * PREVIEW_SCALE);
  }
  previewMat.classList.toggle('is-original', !tiled);
  preview.setAttribute('aria-label', state.material === 'furniture'
    ? `${isPixelStyle() ? 'Pixel art' : 'Voxel'} isometric ${FURNITURE[state.furniture].name.toLowerCase()} sprite with four variants and a transparent background`
    : view === 'sides'
      ? `${state.material} block preview with the 128 by 32 side sheet: edge cap and repeating fill`
      : tiled
        ? `Seamless isometric ${state.material} tile preview, 64 by 32, with four variants`
        : `Isometric ${state.material} block preview, 64 by 32, with side faces`);
}

function updateMaterialControls() {
  const materials = {
    grass: { title: 'Grass texture', subtitle: 'Isometric surface', corner: 'GRASS FIELD', description: 'Seeded grass with variation, clean edges, and seamless tiling.' },
    planks: { title: 'Planks texture', subtitle: 'Wooden boards', corner: 'WOODEN DECK', description: 'Grained planks with staggered joints and wood palettes.' },
    cobblestone: { title: 'Cobblestone texture', subtitle: 'Stone paving', corner: 'STONE PATH', description: 'Irregular stones with mortar joints and tonal variation.' },
    concrete: { title: 'Concrete texture', subtitle: 'Smoothed concrete', corner: 'CAST CONCRETE', description: 'Pixel-textured concrete with fine aggregate and mineral variation.' },
    asphalt: { title: 'Asphalt texture', subtitle: 'Road surface', corner: 'ROAD ASPHALT', description: 'Dark bitumen with fine aggregate speckle and worn patches.' },
    roofing: { title: 'Roofing texture', subtitle: 'Flat roof membrane', corner: 'ROOF DECK', description: 'Bitumen membrane with overlapped roll seams and fine grit.' },
    brick: { title: 'Brick texture', subtitle: 'Running bond', corner: 'BRICK WALL', description: 'Fired bricks in running bond with light mortar and a header course.' },
  };
  const isFurniture = state.material === 'furniture';
  const selected = isFurniture ? furnitureInfo() : materials[state.material];
  document.querySelector('#generator-title').textContent = selected.title;
  document.querySelector('#breadcrumb-material').textContent = isFurniture ? selected.breadcrumb : state.material.toUpperCase();
  document.querySelectorAll('.grass-only').forEach((control) => { control.hidden = state.material !== 'grass'; });
  document.querySelectorAll('.furniture-only').forEach((control) => { control.hidden = !isFurniture; });
  document.querySelectorAll('.pixel-only').forEach((control) => { control.hidden = !isPixelStyle(); });
  document.querySelectorAll('.style-option').forEach((button) => {
    const active = button.dataset.style === state.furnitureStyle;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
  document.querySelectorAll('.material-option').forEach((button) => {
    const active = isFurniture
      ? button.dataset.material === 'furniture' && button.dataset.piece === state.furniture
      : button.dataset.material === state.material;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
  document.querySelector('.preview-actions').hidden = isFurniture;
  document.querySelectorAll('.view-button').forEach((button) => {
    const active = button.dataset.view === state.view;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
  updateSpecText(isFurniture);

  const paletteSet = state.material === 'grass' ? null : (isFurniture ? materialPalettes.planks : materialPalettes[state.material]);
  const activeColors = state.customPalette || (paletteSet ? paletteSet[state.palette] : palettes[state.palette]);
  document.querySelectorAll('.palette-option').forEach((button) => {
    const active = button.dataset.palette === state.palette && !state.customPalette;
    button.classList.toggle('selected', active);
    button.setAttribute('aria-pressed', String(active));
    const palette = paletteSet?.[button.dataset.palette];
    const name = button.querySelector('b');
    const detail = button.querySelector('small');
    name.textContent = palette?.name || ({ meadow: 'Meadow', emerald: 'Emerald', moss: 'Moss', autumn: 'Autumn' }[button.dataset.palette]);
    detail.textContent = palette?.detail || ({ meadow: 'Soft spring', emerald: 'Vibrant forest', moss: 'Muted moss', autumn: 'Golden grassland' }[button.dataset.palette]);
    button.querySelectorAll('.swatches i').forEach((swatch, index) => {
      const ramp = palette?.ramp || palettes[button.dataset.palette].ramp;
      swatch.style.backgroundColor = `rgb(${ramp[[0, 2, 5, 7][index]].join(',')})`;
    });
  });
  document.querySelectorAll('.variation-swatches i').forEach((swatch, index) => {
    swatch.style.backgroundColor = `rgb(${activeColors.ramp[[1, 3, 5, 7][index]].join(',')})`;
  });
  updateRandomPaletteSwatches(activeColors.ramp);
  document.querySelector('.south-swatch').style.backgroundColor = `rgb(${activeColors.ramp[3].map((channel) => Math.round(channel * 0.82)).join(',')})`;
  document.querySelector('.east-swatch').style.backgroundColor = `rgb(${activeColors.ramp[3].map((channel) => Math.round(channel * 0.66)).join(',')})`;
  const paletteLabel = isFurniture ? 'furniture' : state.material;
  document.querySelector('#random-palette').setAttribute('aria-label', `Generate a random ${paletteLabel} palette`);
  document.querySelector('.variation-swatches').setAttribute('aria-label', `Four ${paletteLabel} variations`);
  document.querySelector('#send-to-game').hidden = isFurniture || !BitCanvasGameSync.supported;
  document.querySelector('#change-folder').hidden = isFurniture || !BitCanvasGameSync.supported;
  document.querySelector('#game-destination').hidden = isFurniture || !BitCanvasGameSync.supported;
  const gameSheet = GAME_SHEETS[state.material];
  document.querySelector('#game-destination').textContent = gameSheet
    ? `→ sprites/${gameSheet.folder}/ ${gameSheet.variants} + ${gameSheet.sides}`
    : '';
  document.querySelector('#seed-readout').textContent = state.seed.toUpperCase();
}

function updateReadouts() {
  const size = currentSize();
  const sides = state.material !== 'furniture' && state.view === 'sides';
  document.querySelector('#stage-dimensions').textContent = sides ? '128 × 32 PX' : `${size.width} × ${size.height} PX`;
  document.querySelector('#density-value').textContent = state.density;
  document.querySelector('#brightness-value').textContent = state.brightness;
  document.querySelector('#seed-readout').textContent = state.seed.toUpperCase();
}

function randomSeed() {
  return Math.floor(Math.random() * 0xffffff).toString(16).padStart(6, '0').toUpperCase();
}

function randomMaterialPalette() {
  const woodTones = state.material === 'planks' || state.material === 'furniture';
  const hueRange = woodTones ? [18, 42]
    : state.material === 'cobblestone' ? [185, 245]
      : state.material === 'concrete' ? [175, 215]
        : state.material === 'asphalt' ? [195, 235]
          : state.material === 'roofing' ? [200, 230]
            : state.material === 'brick' ? [5, 25]
              : [72, 147];
  const saturationRange = woodTones ? [30, 56]
    : state.material === 'cobblestone' ? [5, 20]
      : state.material === 'concrete' ? [2, 12]
        : state.material === 'asphalt' ? [2, 10]
          : state.material === 'roofing' ? [3, 12]
            : state.material === 'brick' ? [40, 65]
              : [42, 67];
  const lightnessSpan = state.material === 'asphalt' ? [8, 46]
    : state.material === 'roofing' ? [10, 50]
      : [13, 58];
  const hue = hueRange[0] + Math.random() * (hueRange[1] - hueRange[0]);
  const saturation = saturationRange[0] + Math.random() * (saturationRange[1] - saturationRange[0]);
  const ramp = [];
  for (let index = 0; index < 8; index += 1) {
    const step = index / 7;
    const colorHue = (hue + (Math.random() - 0.5) * 8) / 360;
    const colorSaturation = (saturation - step * 8 + (Math.random() - 0.5) * 6) / 100;
    const lightness = (lightnessSpan[0] + step * lightnessSpan[1] + (Math.random() - 0.5) * 3) / 100;
    const chroma = (1 - Math.abs(2 * lightness - 1)) * colorSaturation;
    const segment = colorHue * 6;
    const secondary = chroma * (1 - Math.abs((segment % 2) - 1));
    const channels = segment < 1 ? [chroma, secondary, 0]
      : segment < 2 ? [secondary, chroma, 0]
        : segment < 3 ? [0, chroma, secondary]
          : segment < 4 ? [0, secondary, chroma]
            : segment < 5 ? [secondary, 0, chroma]
              : [chroma, 0, secondary];
    const offset = lightness - chroma / 2;
    ramp.push(channels.map((channel) => Math.round((channel + offset) * 255)));
  }
  return { ramp };
}

function updateRandomPaletteSwatches(ramp) {
  const stops = [0, 2, 4, 7];
  document.querySelectorAll('.random-palette-swatches i').forEach((swatch, index) => {
    swatch.style.backgroundColor = `rgb(${ramp[stops[index]].join(',')})`;
  });
}

function showStatus(message, kind = 'act') {
  const item = document.createElement('div');
  item.className = `status-item ${kind}`;
  item.textContent = message;
  statusFeed.append(item);
  while (statusFeed.children.length > 3) statusFeed.firstElementChild.remove();
}

function updateSliderProgress(input) {
  const progress = ((Number(input.value) - Number(input.min)) / (Number(input.max) - Number(input.min))) * 100;
  input.style.setProperty('--range-progress', `${progress}%`);
}

document.querySelectorAll('.material-option[data-material]').forEach((button) => {
  button.addEventListener('click', () => {
    state.material = button.dataset.material;
    if (button.dataset.piece) state.furniture = button.dataset.piece;
    state.customPalette = null;
    document.querySelector('#random-palette').classList.remove('active');
    document.querySelector('#random-palette').setAttribute('aria-pressed', 'false');
    updateMaterialControls();
    paintTexture();
  });
});

document.querySelectorAll('.palette-option').forEach((button) => {
  button.addEventListener('click', () => {
    state.palette = button.dataset.palette;
    state.customPalette = null;
    const randomPaletteButton = document.querySelector('#random-palette');
    randomPaletteButton.classList.remove('active');
    randomPaletteButton.setAttribute('aria-pressed', 'false');
    document.querySelectorAll('.palette-option').forEach((option) => {
      const selected = option === button;
      option.classList.toggle('selected', selected);
      option.setAttribute('aria-pressed', String(selected));
    });
    updateMaterialControls();
    paintTexture();
  });
});

document.querySelector('#random-palette').addEventListener('click', (event) => {
  state.customPalette = randomMaterialPalette();
  document.querySelectorAll('.palette-option').forEach((option) => {
    option.classList.remove('selected');
    option.setAttribute('aria-pressed', 'false');
  });
  event.currentTarget.classList.add('active');
  event.currentTarget.setAttribute('aria-pressed', 'true');
  updateRandomPaletteSwatches(state.customPalette.ramp);
  updateMaterialControls();
  paintTexture();
  showStatus(`Generated a new ${state.material} palette.`);
});

densityInput.addEventListener('input', () => {
  state.density = Number(densityInput.value);
  updateSliderProgress(densityInput);
  paintTexture();
});

brightnessInput.addEventListener('input', () => {
  state.brightness = Number(brightnessInput.value);
  updateSliderProgress(brightnessInput);
  paintTexture();
});

document.querySelector('#dirt-toggle').addEventListener('change', (event) => {
  state.dirt = event.target.checked;
  paintTexture();
});

document.querySelectorAll('.style-option').forEach((button) => {
  button.addEventListener('click', () => {
    state.furnitureStyle = button.dataset.style;
    updateMaterialControls();
    paintTexture();
  });
});

document.querySelector('#shadow-toggle').addEventListener('change', (event) => {
  state.groundShadow = event.target.checked;
  paintTexture();
});

seedInput.addEventListener('input', () => {
  state.seed = seedInput.value || '0';
  paintTexture();
});

document.querySelector('#random-seed').addEventListener('click', () => {
  state.seed = randomSeed();
  seedInput.value = state.seed;
  paintTexture();
  showStatus('New seed ready.');
});

function setView(view) {
  state.view = view;
  document.querySelectorAll('.view-button').forEach((button) => {
    const active = button.dataset.view === view;
    button.classList.toggle('active', active);
    button.setAttribute('aria-pressed', String(active));
  });
  updateSpecText(state.material === 'furniture');
  updateReadouts();
  drawPreview();
}

document.querySelectorAll('.view-button').forEach((button) => {
  button.addEventListener('click', () => setView(button.dataset.view));
});

function downloadTexture(canvas, filename, successMessage) {
  canvas.toBlob((blob) => {
    if (!blob) {
    showStatus('Could not prepare the PNG.', 'fail');
      return;
    }
    const link = document.createElement('a');
    link.download = filename;
    link.href = URL.createObjectURL(blob);
    link.click();
    window.setTimeout(() => URL.revokeObjectURL(link.href), 1000);
    showStatus(successMessage);
  }, 'image/png');
}

document.querySelector('#export-button').addEventListener('click', () => {
  const safeSeed = state.seed.replace(/[^a-z0-9_-]/gi, '-').slice(0, 12) || state.material;
  if (state.material === 'furniture') {
    const { name } = FURNITURE[state.furniture];
    const size = currentSize();
    downloadTexture(textureCanvas, `bitcanvas-furniture-${state.furniture}-${state.furnitureStyle}-${size.width}x${size.height}-${safeSeed}.png`, `${name} sprite (${size.width} × ${size.height}) exported.`);
    return;
  }
  downloadTexture(textureCanvas, `bitcanvas-${state.material}-top-64x32-${safeSeed}.png`, 'Isometric 64 × 32 tile exported.');
});

document.querySelector('#export-variants').addEventListener('click', () => {
  const sheet = GAME_SHEETS[state.material];
  if (sheet) {
    downloadTexture(textureAtlas, sheet.variants, `Saved ${sheet.variants}.`);
    return;
  }
  const safeSeed = state.seed.replace(/[^a-z0-9_-]/gi, '-').slice(0, 12) || state.material;
  downloadTexture(textureAtlas, `bitcanvas-${state.material}-variants-${textureAtlas.width}x${textureAtlas.height}-${safeSeed}.png`, 'Four-variant spritesheet exported.');
});

document.querySelector('#export-sides').addEventListener('click', () => {
  if (!sideAtlas) {
    showStatus('Side faces are not available for furniture.', 'warn');
    return;
  }
  const safeSeed = state.seed.replace(/[^a-z0-9_-]/gi, '-').slice(0, 12) || state.material;
  downloadTexture(sideAtlas, `bitcanvas-${state.material}-sides-256x16-${safeSeed}.png`, 'Four side pairs exported; each block repeats every 16 px.');
});

document.querySelector('#export-sides-sheet').addEventListener('click', () => {
  if (!sideSheetTexture) {
    showStatus('The side sheet is not available for furniture.', 'warn');
    return;
  }
  const sheet = GAME_SHEETS[state.material];
  if (sheet) {
    downloadTexture(sideSheetTexture, sheet.sides, `Saved ${sheet.sides}.`);
    return;
  }
  const safeSeed = state.seed.replace(/[^a-z0-9_-]/gi, '-').slice(0, 12) || state.material;
  downloadTexture(sideSheetTexture, `bitcanvas-${state.material}-side-128x32-${safeSeed}.png`, '128 × 32 side sheet exported: edge cap and repeating fill.');
});

const helpButton = document.querySelector('#help-button');
const helpPopover = document.querySelector('#help-popover');
helpButton.addEventListener('click', () => { helpPopover.hidden = !helpPopover.hidden; });

document.querySelectorAll('[data-material-tab]').forEach((button) => {
  button.addEventListener('click', () => setMaterialTab(button.dataset.materialTab));
});

function setMaterialTab(tab) {
  document.querySelectorAll('[data-material-tab]').forEach((button) => {
    const active = button.dataset.materialTab === tab;
    button.classList.toggle('active', active);
    button.setAttribute('aria-selected', String(active));
  });
  document.querySelectorAll('.material-option').forEach((button) => {
    button.hidden = (button.dataset.material === 'furniture') !== (tab === 'furniture');
  });
  const visible = [...document.querySelectorAll('.material-option:not([hidden])')];
  if (!visible.some((button) => button.classList.contains('active'))) visible[0]?.click();
}

document.querySelector('#send-to-game').addEventListener('click', async () => {
  const sheets = GAME_SHEETS[state.material];
  if (!sheets || !BitCanvasGameSync.supported) return;
  const result = await BitCanvasGameSync.send(sheets.folder, textureAtlas, sideSheetTexture, [sheets.variants, sheets.sides]);
  const message = result.kind === 'warn' ? `CAN'T SEND: ${result.message}`
    : result.kind === 'fail' ? `FAILED: ${result.message}` : result.message;
  showStatus(message, result.kind);
  if (result.kind === 'act') document.querySelector('#folder-name').textContent = 'FOLDER sprites';
});

document.querySelector('#change-folder').addEventListener('click', async () => {
  const result = await BitCanvasGameSync.chooseFolder();
  if (result.handle) {
    document.querySelector('#folder-name').textContent = `FOLDER ${result.handle.name}`;
    showStatus(`Using ${result.handle.name}/ as the sprites folder.`);
  } else {
    const refused = result.error.includes('Pick EtherBound');
    showStatus(`${refused ? "CAN'T SEND" : 'FAILED'}: ${result.error}`, refused ? 'warn' : 'fail');
  }
});

document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape') {
    helpPopover.hidden = true;
    return;
  }
  if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement || event.target.isContentEditable) return;
  const key = event.key.toLowerCase();
  if (key === '?') {
    helpPopover.hidden = !helpPopover.hidden;
  } else if (key === 't') {
    setMaterialTab('terrain');
  } else if (key === 'f') {
    setMaterialTab('furniture');
  } else if (key === 'v' && state.material !== 'furniture') {
    const views = ['tiles', 'block', 'sides'];
    setView(views[(views.indexOf(state.view) + 1) % views.length]);
  } else if (key === 'r') {
    document.querySelector('#random-seed').click();
  } else if (key === 'p') {
    document.querySelector('#random-palette').click();
  } else if (key === 'arrowup' || key === 'arrowdown') {
    event.preventDefault();
    const options = [...document.querySelectorAll('.material-option:not([hidden])')];
    const selected = options.findIndex((button) => button.classList.contains('active'));
    const step = key === 'arrowdown' ? 1 : -1;
    options[(selected + step + options.length) % options.length]?.click();
  }
});

preview.width = 768;
preview.height = 768;
if (!BitCanvasGameSync.supported) {
  document.querySelector('#send-to-game').hidden = true;
  document.querySelector('#change-folder').hidden = true;
  document.querySelector('#folder-name').hidden = true;
}
setMaterialTab('terrain');
updateMaterialControls();
BitCanvasGameSync.storedFolderName().then((name) => {
  if (name) document.querySelector('#folder-name').textContent = `FOLDER ${name}`;
});
updateSliderProgress(densityInput);
updateSliderProgress(brightnessInput);
paintTexture();
