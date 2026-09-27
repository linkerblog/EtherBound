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
    cliffSideSheetTexture = null;
    sideBlockCanvases = [];
    cliffSideBlockCanvases = [];
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
  cliffSideSheetTexture = null;
  cliffSideBlockCanvases = [];
  if (state.material === 'grass') {
    const cliffCells = textureCanvases.map((tile, variant) => {
      const variantSeed = variantSeedFor(seed, variant);
      const tilePixels = tile.getContext('2d').getImageData(0, 0, TILE_WIDTH, TILE_HEIGHT).data;
      return {
        tile,
        cap: createCliffSideCell(colors, variantSeed, seed, tilePixels, 'cap'),
        fill: createCliffSideCell(colors, variantSeed, seed, tilePixels, 'fill'),
      };
    });
    cliffSideSheetTexture = createSideSheetTexture(cliffCells);
    cliffSideBlockCanvases = cliffCells.map((cell) => createSideBlockTexture(cell.tile, cell.cap, cell.fill, 1));
  }
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
