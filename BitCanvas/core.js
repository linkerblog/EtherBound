// Eight-step ramps, darkest to brightest: crevices, stems, then blade tips.
const palettes = {
  meadow: { ramp: [[8, 48, 18], [12, 70, 22], [18, 94, 28], [26, 120, 34], [38, 146, 40], [58, 172, 48], [92, 198, 62], [134, 222, 84]] },
  emerald: { ramp: [[9, 38, 35], [13, 62, 49], [18, 89, 63], [27, 117, 77], [46, 147, 91], [79, 178, 107], [128, 207, 128], [186, 232, 160]] },
  moss: { ramp: [[35, 39, 28], [51, 57, 36], [69, 77, 44], [89, 97, 54], [111, 117, 64], [137, 140, 78], [167, 164, 96], [205, 196, 124]] },
  autumn: { ramp: [[51, 41, 26], [77, 61, 32], [105, 85, 38], [135, 111, 44], [163, 137, 52], [191, 163, 66], [217, 190, 90], [240, 216, 130]] },
};

const materialPalettes = {
  dirt: {
    meadow: { name: 'Loam', detail: 'Packed garden soil', ramp: [[56, 40, 26], [70, 50, 31], [85, 62, 38], [101, 73, 44], [118, 85, 52], [136, 100, 62], [152, 113, 70], [170, 128, 82]] },
    emerald: { name: 'Peat', detail: 'Wet dark soil', ramp: [[30, 24, 18], [40, 32, 24], [52, 42, 31], [66, 54, 40], [82, 68, 50], [100, 84, 62], [118, 100, 76], [138, 118, 92]] },
    moss: { name: 'Clay', detail: 'Warm red clay', ramp: [[74, 36, 24], [96, 47, 31], [118, 60, 40], [138, 75, 51], [155, 92, 64], [172, 110, 80], [188, 128, 97], [204, 148, 115]] },
    autumn: { name: 'Dust', detail: 'Pale dry earth', ramp: [[104, 84, 58], [122, 100, 70], [140, 117, 84], [157, 134, 98], [173, 150, 112], [188, 166, 126], [203, 182, 141], [218, 198, 157]] },
  },
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
  grass: { folder: 'grass', variants: 'grass_x4.png', sides: 'grass_side_x4.png', cliffSides: 'grass_cliff_side_x4.png' },
  dirt: { folder: 'floor', variants: 'dirt_x4.png', sides: 'dirt_side_x4.png' },
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
let cliffSideSheetTexture;
let textureCanvases = [];
let sideCanvases = [];
let textureAtlas;
let sideAtlas;
let sideSheetTexture;
let sideBlockCanvases = [];
let cliffSideBlockCanvases = [];

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
