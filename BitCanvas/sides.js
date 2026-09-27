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

function createCliffSideCell(colors, seed, patternSeed, tilePixels, part) {
  const cell = createSideCell(colors, seed, patternSeed, 'grass', tilePixels, part);
  const random = randomGenerator((seed ^ (part === 'cap' ? 0x9e3779b1 : 0x85ebca6b)) >>> 0);
  const lip = part === 'cap' ? grassLipDepth(random, SIDE_CELL_WIDTH) : null;
  const faceBase = part === 'cap' ? 0 : SIDE_CELL_HEIGHT;
  const phase = patternSeed % SIDE_CELL_HEIGHT;

  for (let x = 0; x < SIDE_CELL_WIDTH; x += 1) {
    const segment = Math.floor(x / 4);
    const wobble = materialCellHash(segment, 0, patternSeed) % 3 - 1;
    const band = 7 + wobble;
    for (let row = 0; row < SIDE_CELL_HEIGHT; row += 1) {
      if (lip && row <= lip[x]) continue;

      const faceRow = faceBase + row;
      const patternRow = (row + phase) % SIDE_CELL_HEIGHT;
      const hash = materialCellHash(x, faceRow, seed ^ patternSeed);
      let color;
      if (lip && row <= lip[x] + 2) {
        color = row === lip[x] + 1 ? DIRT_COLORS[0] : DIRT_COLORS[1];
      } else if (patternRow === band) {
        color = DIRT_COLORS[0];
      } else if (patternRow === band + 1 && hash % 100 < 76) {
        color = DIRT_COLORS[2];
      } else if (hash % 127 === 0) {
        color = DIRT_COLORS[0];
      } else {
        continue;
      }

      const offset = (row * SIDE_CELL_WIDTH + x) * 4;
      cell.data[offset] = color[0];
      cell.data[offset + 1] = color[1];
      cell.data[offset + 2] = color[2];
    }
  }

  return cell;
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
