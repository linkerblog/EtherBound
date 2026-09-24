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
