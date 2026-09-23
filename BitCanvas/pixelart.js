// --- Pixel Art Furniture ---------------------------------------------------
// Unlike voxel art, each piece of furniture is a scene of simple solids
// (boxes, truncated cones, horizontal cylinders, and ellipsoids) traced with
// one ray per pixel. Then pixel art rules are applied: clean light bands,
// highlights on front edges, self-shadowing, selective colored outlines, and
// hue-shifted ramps.
//
// Pixel scale: 1 iso tile (64 × 32 px) covers 32 × 32 units; each height
// unit raises the image by 1 px. Projection: sx = x − y, sy = (x + y) / 2 − z.

const PX_FAR = 400;
const PX_EPS = 1e-4;
const PX_CONTOUR_DEPTH = 2;
const PX_VIEW = [-1, -1, -1];
const PX_AXES = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];

function pxDot(a, b) {
  return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

function pxNormalize(v) {
  const length = Math.hypot(v[0], v[1], v[2]) || 1;
  return [v[0] / length, v[1] / length, v[2] / length];
}

function pxCross(a, b) {
  return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}

// Light from the upper left illuminates the left (+y) face and shades the
// right (+x) face, as in most isometric pixel art.
const PX_LIGHT = pxNormalize([-0.35, 0.65, 1]);
const PX_HALF = pxNormalize([PX_LIGHT[0] + 0.577, PX_LIGHT[1] + 0.577, PX_LIGHT[2] + 0.577]);

function pxHash(a, b, c, seed) {
  let hash = Math.imul(a + 17, 374761393) ^ Math.imul(b + 31, 668265263) ^ Math.imul(c + 47, 1274126177) ^ seed;
  hash = Math.imul(hash ^ (hash >>> 13), 1274126177);
  return (hash ^ (hash >>> 16)) >>> 0;
}

function pxRandom(seed) {
  let value = seed || 1;
  return () => {
    value += 0x6D2B79F5;
    let result = value;
    result = Math.imul(result ^ (result >>> 15), result | 1);
    result ^= result + Math.imul(result ^ (result >>> 7), result | 61);
    return ((result ^ (result >>> 14)) >>> 0) / 4294967296;
  };
}

// --- Primitives -------------------------------------------------------------

function pxBox(from, to, role, extra = {}) {
  return { kind: 'box', from, to, role, ...extra };
}

// Vertical truncated cone: radius r0 at z0 and r1 at z1 (a cylinder if equal).
function pxCyl(cx, cy, z0, z1, r0, r1, role, extra = {}) {
  return { kind: 'cyl', cx, cy, z0, z1, r0, r1, role, ...extra };
}

// Horizontal cylinder along x (the chest lid).
function pxLog(cy, cz, r, x0, x1, role, extra = {}) {
  return { kind: 'log', cy, cz, r, x0, x1, role, ...extra };
}

// Ellipsoid with local axes, for cushions, leaves, and fruit.
function pxBlob(center, radii, role, extra = {}) {
  return { kind: 'blob', center, radii, axes: PX_AXES, role, ...extra };
}

function pxHitBox(prim, o, d) {
  let near = -Infinity;
  let far = Infinity;
  let axis = -1;
  let sign = 0;
  for (let i = 0; i < 3; i += 1) {
    if (d[i] === 0) {
      if (o[i] < prim.from[i] || o[i] > prim.to[i]) return null;
      continue;
    }
    const toFrom = (prim.from[i] - o[i]) / d[i];
    const toTo = (prim.to[i] - o[i]) / d[i];
    const entry = d[i] > 0 ? toFrom : toTo;
    if (entry > near) {
      near = entry;
      axis = i;
      sign = d[i] > 0 ? -1 : 1;
    }
    far = Math.min(far, d[i] > 0 ? toTo : toFrom);
  }
  if (near > far || near <= PX_EPS) return null;
  const n = [0, 0, 0];
  n[axis] = sign;
  return { t: near, n };
}

function pxHitCyl(prim, o, d) {
  const slope = (prim.r1 - prim.r0) / (prim.z1 - prim.z0);
  const ex = o[0] - prim.cx;
  const ey = o[1] - prim.cy;
  const radiusAtOrigin = prim.r0 + slope * (o[2] - prim.z0);
  let best = null;
  const consider = (t, n, cap) => {
    if (t > PX_EPS && (!best || t < best.t)) best = { t, n, cap };
  };
  const a = d[0] * d[0] + d[1] * d[1] - slope * slope * d[2] * d[2];
  const b = 2 * (ex * d[0] + ey * d[1] - radiusAtOrigin * slope * d[2]);
  const c = ex * ex + ey * ey - radiusAtOrigin * radiusAtOrigin;
  if (Math.abs(a) > 1e-9) {
    const disc = b * b - 4 * a * c;
    if (disc >= 0) {
      const root = Math.sqrt(disc);
      for (const t of [(-b - root) / (2 * a), (-b + root) / (2 * a)]) {
        const z = o[2] + t * d[2];
        if (z < prim.z0 || z > prim.z1) continue;
        const radius = prim.r0 + slope * (z - prim.z0);
        if (radius <= 0) continue;
        const n = pxNormalize([ex + t * d[0], ey + t * d[1], -radius * slope]);
        if (pxDot(n, d) < 0) consider(t, n, false);
      }
    }
  }
  if (d[2] !== 0) {
    for (const [z, radius, nz] of [[prim.z1, prim.r1, 1], [prim.z0, prim.r0, -1]]) {
      if (nz * d[2] >= 0) continue;
      const t = (z - o[2]) / d[2];
      const x = ex + t * d[0];
      const y = ey + t * d[1];
      if (x * x + y * y <= radius * radius) consider(t, [0, 0, nz], true);
    }
  }
  return best;
}

function pxHitLog(prim, o, d) {
  const ey = o[1] - prim.cy;
  const ez = o[2] - prim.cz;
  let best = null;
  const a = d[1] * d[1] + d[2] * d[2];
  if (a > 0) {
    const b = 2 * (ey * d[1] + ez * d[2]);
    const c = ey * ey + ez * ez - prim.r * prim.r;
    const disc = b * b - 4 * a * c;
    if (disc >= 0) {
      const t = (-b - Math.sqrt(disc)) / (2 * a);
      const x = o[0] + t * d[0];
      if (t > PX_EPS && x >= prim.x0 && x <= prim.x1) {
        best = { t, n: [0, (ey + t * d[1]) / prim.r, (ez + t * d[2]) / prim.r] };
      }
    }
  }
  if (d[0] !== 0) {
    for (const [x, nx] of [[prim.x1, 1], [prim.x0, -1]]) {
      if (nx * d[0] >= 0) continue;
      const t = (x - o[0]) / d[0];
      const y = ey + t * d[1];
      const z = ez + t * d[2];
      if (t > PX_EPS && y * y + z * z <= prim.r * prim.r && (!best || t < best.t)) best = { t, n: [nx, 0, 0], cap: true };
    }
  }
  return best;
}

function pxHitBlob(prim, o, d) {
  const rel = [o[0] - prim.center[0], o[1] - prim.center[1], o[2] - prim.center[2]];
  const ol = prim.axes.map((axis, i) => pxDot(rel, axis) / prim.radii[i]);
  const dl = prim.axes.map((axis, i) => pxDot(d, axis) / prim.radii[i]);
  const a = pxDot(dl, dl);
  const b = 2 * pxDot(ol, dl);
  const c = pxDot(ol, ol) - 1;
  const disc = b * b - 4 * a * c;
  if (disc < 0) return null;
  const t = (-b - Math.sqrt(disc)) / (2 * a);
  if (t <= PX_EPS) return null;
  const n = [0, 0, 0];
  for (let i = 0; i < 3; i += 1) {
    const local = (ol[i] + t * dl[i]) / prim.radii[i];
    n[0] += prim.axes[i][0] * local;
    n[1] += prim.axes[i][1] * local;
    n[2] += prim.axes[i][2] * local;
  }
  return { t, n: pxNormalize(n) };
}

const PX_HIT = { box: pxHitBox, cyl: pxHitCyl, log: pxHitLog, blob: pxHitBlob };

// Each solid's world-space bounding box and screen rectangle reject nearly all
// rays before exact intersection testing.
function pxPrepare(prim) {
  let from;
  let to;
  if (prim.kind === 'box') {
    ({ from, to } = prim);
  } else if (prim.kind === 'cyl') {
    const r = Math.max(prim.r0, prim.r1);
    from = [prim.cx - r, prim.cy - r, prim.z0];
    to = [prim.cx + r, prim.cy + r, prim.z1];
  } else if (prim.kind === 'log') {
    from = [prim.x0, prim.cy - prim.r, prim.cz - prim.r];
    to = [prim.x1, prim.cy + prim.r, prim.cz + prim.r];
  } else {
    const reach = [0, 1, 2].map((i) => prim.axes.reduce((sum, axis, j) => sum + Math.abs(axis[i]) * prim.radii[j], 0));
    from = prim.center.map((c, i) => c - reach[i]);
    to = prim.center.map((c, i) => c + reach[i]);
  }
  prim.screen = [from[0] - to[1], to[0] - from[1], (from[0] + from[1]) / 2 - to[2], (to[0] + to[1]) / 2 - from[2]];
  prim.rects = PX_SHADOW_DIRS.map((basis) => pxProjectBox(from, to, basis));
}

// For each shadow direction, project the box onto the perpendicular plane. A
// ray can hit it only if its origin lies inside the rectangle.
function pxBasis(d) {
  const e1 = pxNormalize(pxCross(d, Math.abs(d[2]) > 0.9 ? [1, 0, 0] : [0, 0, 1]));
  return [e1, pxCross(d, e1), d];
}

const PX_SHADOW_DIRS = [pxBasis(PX_LIGHT), pxBasis([0, 0, 1])];

function pxProjectBox(from, to, [e1, e2, d]) {
  const rect = [Infinity, -Infinity, Infinity, -Infinity, -Infinity];
  for (const x of [from[0], to[0]]) {
    for (const y of [from[1], to[1]]) {
      for (const z of [from[2], to[2]]) {
        const u = x * e1[0] + y * e1[1] + z * e1[2];
        const v = x * e2[0] + y * e2[1] + z * e2[2];
        rect[0] = Math.min(rect[0], u);
        rect[1] = Math.max(rect[1], u);
        rect[2] = Math.min(rect[2], v);
        rect[3] = Math.max(rect[3], v);
        rect[4] = Math.max(rect[4], x * d[0] + y * d[1] + z * d[2]);
      }
    }
  }
  return rect;
}

function pxTrace(prims, o, d, sx, sy) {
  let best = null;
  for (let index = 0; index < prims.length; index += 1) {
    const [left, right, top, bottom] = prims[index].screen;
    if (sx < left || sx > right || sy < top || sy > bottom) continue;
    const hit = PX_HIT[prims[index].kind](prims[index], o, d);
    if (hit && (!best || hit.t < best.t)) {
      best = hit;
      best.index = index;
    }
  }
  return best;
}

// dir: index in PX_SHADOW_DIRS (0 main light, 1 vertical ground shadow).
function pxOccluded(prims, o, dir, skip) {
  const [e1, e2, d] = PX_SHADOW_DIRS[dir];
  const u = o[0] * e1[0] + o[1] * e1[1] + o[2] * e1[2];
  const v = o[0] * e2[0] + o[1] * e2[1] + o[2] * e2[2];
  const along = o[0] * d[0] + o[1] * d[1] + o[2] * d[2];
  for (let index = 0; index < prims.length; index += 1) {
    if (index === skip) continue;
    const rect = prims[index].rects[dir];
    if (u < rect[0] || u > rect[1] || v < rect[2] || v > rect[3] || along > rect[4]) continue;
    if (PX_HIT[prims[index].kind](prims[index], o, d)) return true;
  }
  return false;
}

// --- Ramps ------------------------------------------------------------------

const PX_GOLD_RAMP = [[58, 32, 18], [96, 56, 22], [140, 90, 28], [180, 128, 36], [214, 166, 50], [236, 198, 78], [248, 224, 122], [255, 244, 184]];
const PX_CLAY_RAMP = [[62, 28, 24], [94, 42, 32], [128, 60, 40], [160, 82, 50], [188, 106, 64], [210, 134, 86], [228, 164, 114], [242, 196, 152]];
const PX_FRUIT_RAMP = [[52, 14, 24], [88, 20, 30], [128, 30, 34], [168, 44, 40], [204, 66, 50], [228, 98, 70], [244, 138, 102], [252, 184, 150]];
const PX_OUTLINE_TINT = [24, 16, 34];

function pxRgbToHsl([r, g, b]) {
  const red = r / 255;
  const green = g / 255;
  const blue = b / 255;
  const max = Math.max(red, green, blue);
  const min = Math.min(red, green, blue);
  const lightness = (max + min) / 2;
  const delta = max - min;
  if (delta === 0) return [0, 0, lightness];
  const saturation = delta / (1 - Math.abs(2 * lightness - 1));
  const hue = max === red ? ((green - blue) / delta) % 6 : max === green ? (blue - red) / delta + 2 : (red - green) / delta + 4;
  return [((hue * 60) % 360 + 360) % 360, saturation, lightness];
}

function pxHslToRgb(hue, saturation, lightness) {
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
  return channels.map((channel) => Math.round(Math.max(0, Math.min(1, channel + offset)) * 255));
}

function pxHueToward(hue, target, amount) {
  const delta = ((target - hue + 540) % 360) - 180;
  return (hue + Math.sign(delta) * Math.min(Math.abs(delta), amount) + 360) % 360;
}

// Shift shadows toward violet and highlights toward amber for a hand-painted
// look instead of simply darkening colors to gray.
function pxShiftRamp(ramp) {
  return ramp.map((rgb, index) => {
    const t = index / (ramp.length - 1);
    const [hue, saturation, lightness] = pxRgbToHsl(rgb);
    if (saturation < 0.05) return rgb;
    const shifted = t < 0.5
      ? pxHueToward(hue, 262, (0.5 - t) * 2 * 24)
      : pxHueToward(hue, 48, (t - 0.5) * 2 * 14);
    return pxHslToRgb(shifted, Math.min(1, saturation * (1.12 - t * 0.18)), lightness);
  });
}

function pxMix(a, b, amount) {
  return a.map((channel, i) => Math.round(channel + (b[i] - channel) * amount));
}

function pixelArtRamps(baseRamps) {
  const source = { ...baseRamps, gold: PX_GOLD_RAMP, clay: PX_CLAY_RAMP, fruit: PX_FRUIT_RAMP };
  const ramps = {};
  for (const [name, ramp] of Object.entries(source)) {
    const colors = pxShiftRamp(ramp);
    ramps[name] = { colors, outline: pxMix(colors[0], PX_OUTLINE_TINT, 0.55), rim: pxMix(colors[1], PX_OUTLINE_TINT, 0.2) };
  }
  return ramps;
}

// level: base ramp shade (0–7). contrast: how strongly light separates faces.
const PIXEL_ROLES = {
  wood: { ramp: 'wood', level: 4.4, contrast: 3.2 },
  woodDark: { ramp: 'wood', level: 3.1, contrast: 3.0 },
  woodLight: { ramp: 'wood', level: 5.3, contrast: 3.0 },
  metal: { ramp: 'iron', level: 4.2, contrast: 3.6, shine: true },
  gold: { ramp: 'gold', level: 4.4, contrast: 3.4, shine: true },
  fabric: { ramp: 'fabric', level: 4.5, contrast: 2.8 },
  sheet: { ramp: 'linen', level: 5.3, contrast: 2.6 },
  pillow: { ramp: 'linen', level: 6.0, contrast: 2.6 },
  blanket: { ramp: 'blanket', level: 4.5, contrast: 2.8 },
  blanketLight: { ramp: 'blanket', level: 5.6, contrast: 2.6 },
  glow: { ramp: 'glow', level: 5.1, contrast: 2.2, emissive: true },
  leaf: { ramp: 'leaf', level: 4.3, contrast: 3.4 },
  stem: { ramp: 'leaf', level: 2.6, contrast: 2.0 },
  soil: { ramp: 'soil', level: 2.0, contrast: 1.4 },
  clay: { ramp: 'clay', level: 4.2, contrast: 3.2 },
  ceramic: { ramp: 'linen', level: 5.5, contrast: 2.8, shine: true },
  fruit: { ramp: 'fruit', level: 4.3, contrast: 3.4, shine: true },
  hole: { ramp: 'wood', level: 0.4, contrast: 0.5 },
  book0: { ramp: 'book0', level: 4.4, contrast: 3.0 },
  book1: { ramp: 'book1', level: 4.4, contrast: 3.0 },
  book2: { ramp: 'book2', level: 4.4, contrast: 3.0 },
  book3: { ramp: 'book3', level: 4.4, contrast: 3.0 },
};

// --- Surface Patterns --------------------------------------------------------
// Return hue offsets as 1 px lines, never noise.

function pxMainAxis(n) {
  const ax = Math.abs(n[0]);
  const ay = Math.abs(n[1]);
  const az = Math.abs(n[2]);
  return az >= ax && az >= ay ? 2 : ax >= ay ? 0 : 1;
}

function pxPlankLines(across, along, width, key, seed) {
  const plank = Math.floor(across / width);
  const inside = across - plank * width;
  if (plank > 0 && inside < 1) return -1.3;
  const grainRow = 1 + (pxHash(plank, key, 3, seed) % Math.max(1, width - 1));
  if (Math.floor(inside) === grainRow && pxHash(plank, Math.floor(along / 3), key, seed) % 4 === 0) return -0.8;
  return 0;
}

function pxPattern(prim, p, n, seed) {
  switch (prim.pattern) {
    case 'planks': {
      const width = prim.width || 4;
      if (prim.kind === 'log') {
        if (Math.abs(n[0]) > 0.9) return 0;
        const across = (Math.atan2(p[2] - prim.cz, p[1] - prim.cy) + Math.PI) * prim.r;
        return pxPlankLines(across, p[0], width, 7, seed);
      }
      const face = pxMainAxis(n);
      if (face === prim.axis) return 0;
      const acrossAxis = 3 - prim.axis - face;
      return pxPlankLines(p[acrossAxis] - prim.from[acrossAxis], p[prim.axis], width, face, seed);
    }
    case 'staves': {
      const width = prim.width || 4;
      if (n[2] > 0.9) {
        const radius = Math.hypot(p[0] - prim.cx, p[1] - prim.cy);
        if (radius > prim.r1 - 1.5) return -1.1;
        return pxPlankLines(p[1] - prim.cy + prim.r1, p[0], width, 5, seed);
      }
      const across = (Math.atan2(p[1] - prim.cy, p[0] - prim.cx) + Math.PI) * Math.max(prim.r0, prim.r1);
      return pxPlankLines(across, p[2], width, 9, seed);
    }
    case 'pleats': {
      if (Math.abs(n[2]) > 0.9) return 0;
      const across = (Math.atan2(p[1] - prim.cy, p[0] - prim.cx) + Math.PI) * Math.max(prim.r0, prim.r1);
      return across % 4 < 1 ? -0.6 : 0;
    }
    case 'panel': {
      const face = pxMainAxis(n);
      const [a, b] = [0, 1, 2].filter((axis) => axis !== face);
      const inset = Math.min(p[a] - prim.from[a], prim.to[a] - p[a], p[b] - prim.from[b], prim.to[b] - p[b]);
      if (inset >= 2 && inset < 3) return -1.2;
      if (inset >= 3 && inset < 4) return 0.5;
      return 0;
    }
    case 'quilt': {
      const face = pxMainAxis(n);
      const [a, b] = [0, 1, 2].filter((axis) => axis !== face);
      const u = p[a] - prim.from[a];
      const v = p[b] - prim.from[b];
      const size = prim.width || 6;
      if (u % size < 1 || v % size < 1) return -0.9;
      return (Math.floor(u / size) + Math.floor(v / size)) % 2 === 0 ? 0.8 : 0;
    }
    case 'stripes': {
      const u = p[0] - prim.from[0];
      return Math.floor(u / (prim.width || 4)) % 2 === 0 ? 0.9 : 0;
    }
    case 'spine': {
      if (p[0] - prim.from[0] < 1) return -1.6;
      if (n[1] > 0.9) {
        const fromTop = prim.to[2] - p[2];
        if (fromTop >= 2 && fromTop < 3) return 1.4;
        if (prim.band && fromTop >= prim.band && fromTop < prim.band + 1) return 1.4;
      }
      return 0;
    }
    default:
      return 0;
  }
}

// --- Furniture ---------------------------------------------------------------

function pxTableItem(random) {
  const x = 11 + Math.round(random() * 9);
  const y = 11 + Math.round(random() * 9);
  const z = 19;
  switch (Math.floor(random() * 5)) {
    case 0:
      return [pxCyl(x, y, z, z + 5, 2.5, 2.5, 'ceramic'), pxBox([x + 2, y, z + 1], [x + 4, y + 1, z + 4], 'ceramic')];
    case 1:
      return [pxCyl(x, y, z, z + 1, 3, 3, 'gold'), pxCyl(x, y, z + 1, z + 7, 1.3, 1.3, 'sheet'), pxBlob([x, y, z + 8.3], [1.1, 1.1, 1.7], 'glow')];
    case 2:
      return [
        pxCyl(x, y, z, z + 6, 2, 3, 'clay'),
        pxCyl(x, y, z + 6, z + 9, 0.5, 0.5, 'stem'),
        pxBlob([x - 1.5, y + 1, z + 8], [2.2, 1, 0.8], 'leaf', { tone: 0.5 }),
        pxBlob([x, y, z + 10], [2, 2, 1.4], 'fruit'),
      ];
    case 3:
      return [pxCyl(x, y, z, z + 1, 5, 5, 'ceramic'), pxBlob([x, y, z + 3], [2.4, 2.4, 2.2], 'fruit')];
    default:
      return [
        pxBox([x - 3, y - 4, z], [x + 3, y + 4, z + 2], 'book1', { pattern: 'spine' }),
        pxBox([x - 2, y - 3, z + 2], [x + 3, y + 3, z + 4], 'book3', { pattern: 'spine' }),
      ];
  }
}

function pxBooks(random, zBase, zTop, prims) {
  const roles = ['book0', 'book1', 'book2', 'book3'];
  let x = 3;
  while (x < 29) {
    const room = 29 - x;
    if (random() < 0.1) {
      x += 1 + Math.floor(random() * 2);
      continue;
    }
    if (room >= 6 && random() < 0.1) {
      const plantPot = random() < 0.5;
      if (plantPot) {
        prims.push(pxCyl(x + 3, 9, zBase, zBase + 4, 2, 2.5, 'clay'));
        prims.push(pxBlob([x + 3, 9, zBase + 6], [2.6, 2.6, 2.2], 'leaf'));
      } else {
        prims.push(pxBlob([x + 3, 9, zBase + 3], [3, 3, 3], 'fruit', { tone: -0.3 }));
      }
      x += 6;
      continue;
    }
    const width = Math.min(room, 2 + Math.floor(random() * 2));
    const height = zTop - zBase - 1 - Math.floor(random() * 4);
    const front = 11 + Math.floor(random() * 2);
    prims.push(pxBox([x, 5, zBase], [x + width, front, zBase + height], roles[Math.floor(random() * 4)], {
      pattern: 'spine',
      band: random() < 0.5 ? height - 3 : 0,
      tone: (random() - 0.5) * 0.8,
    }));
    x += width;
  }
}

const PIXEL_FURNITURE = {
  table: {
    description: 'Pixel art table with a plank top, apron, and a seed-varied tabletop detail.',
    extent: { from: [0, 0, 0], to: [32, 32, 30] },
    build(random) {
      const prims = [];
      for (const [x, y] of [[4, 4], [25, 4], [4, 25], [25, 25]]) prims.push(pxBox([x, y, 0], [x + 3, y + 3, 16], 'woodDark'));
      prims.push(pxBox([5, 5, 12], [27, 27, 16], 'woodDark'));
      prims.push(pxBox([2, 2, 16], [30, 30, 19], 'wood', { pattern: 'planks', axis: random() < 0.5 ? 0 : 1 }));
      prims.push(...pxTableItem(random));
      return prims;
    },
  },
  chair: {
    description: 'Pixel art chair with a slatted backrest and a cushion in a varied color.',
    extent: { from: [5, 5, 0], to: [27, 27, 34] },
    build(random) {
      const prims = [];
      for (const [x, y] of [[7, 7], [22, 7], [7, 22], [22, 22]]) prims.push(pxBox([x, y, 0], [x + 3, y + 3, 12], 'woodDark'));
      prims.push(pxBox([6, 6, 12], [26, 26, 15], 'wood', { pattern: 'planks', axis: 0 }));
      prims.push(pxBox([6, 6, 15], [9, 9, 34], 'woodDark'));
      prims.push(pxBox([23, 6, 15], [26, 9, 34], 'woodDark'));
      prims.push(pxBox([9, 7, 28], [23, 9, 33], 'wood'));
      const slats = random() < 0.5 ? [[12, 14], [18, 20]] : [[11, 13], [15, 17], [19, 21]];
      for (const [x0, x1] of slats) prims.push(pxBox([x0, 7, 15], [x1, 8, 28], 'woodDark'));
      const cushion = ['fabric', 'blanket', 'book1', 'book3'][Math.floor(random() * 4)];
      prims.push(pxBlob([16, 17, 15.4], [8.4, 7.6, 2.6], cushion, { tone: 0.3 }));
      return prims;
    },
  },
  bed: {
    description: 'Pixel art bed with a paneled headboard, soft pillows, and a seed-patterned blanket.',
    extent: { from: [0, 0, 0], to: [64, 32, 24] },
    build(random) {
      const prims = [];
      prims.push(pxBox([0, 1, 0], [4, 31, 20], 'wood', { pattern: 'panel' }));
      prims.push(pxBox([0, 0, 20], [5, 32, 23], 'woodLight'));
      for (const [x, y] of [[4, 2], [57, 2], [4, 27], [57, 27]]) prims.push(pxBox([x, y, 0], [x + 3, y + 3, 2], 'woodDark'));
      prims.push(pxBox([4, 2, 2], [60, 30, 8], 'woodDark', { pattern: 'planks', axis: 0, width: 3 }));
      prims.push(pxBox([59, 1, 0], [63, 31, 12], 'wood', { pattern: 'panel' }));
      prims.push(pxBox([58, 0, 12], [64, 32, 14], 'woodLight'));
      prims.push(pxBox([4, 3, 8], [59, 29, 13], 'sheet'));
      if (random() < 0.5) {
        prims.push(pxBlob([10, 9.5, 14.6], [3.8, 5.6, 2.5], 'pillow'));
        prims.push(pxBlob([10, 22.5, 14.6], [3.8, 5.6, 2.5], 'pillow'));
      } else {
        prims.push(pxBlob([10, 16, 14.6], [3.8, 11.5, 2.6], 'pillow'));
      }
      const pattern = ['quilt', 'stripes', 'plain'][Math.floor(random() * 3)];
      prims.push(pxBox([24, 2, 8], [58, 30, 14], 'blanket', { pattern, width: pattern === 'quilt' ? 6 : 4 }));
      prims.push(pxBox([21, 2, 8], [26, 30, 15], 'blanketLight'));
      return prims;
    },
  },
  chest: {
    description: 'Cofre pixel art con tapa curva de tablones, herrajes y cerradura dorada.',
    extent: { from: [2, 5, 0], to: [30, 27, 32] },
    build(random) {
      const trim = random() < 0.5 ? 'metal' : 'gold';
      return [
        pxBox([3, 7, 0], [29, 25, 17.333333333333332], 'wood', { pattern: 'planks', axis: 0 }),
        pxLog(16, 18.666666666666668, 9, 3, 29, 'wood', { pattern: 'planks', width: 4 }),
        pxBox([2, 6, 16], [30, 26, 18.666666666666668], trim),
        pxBox([6, 6, 0], [8, 26, 16], trim),
        pxBox([24, 6, 0], [26, 26, 16], trim),
        pxLog(16, 18.666666666666668, 9.5, 6, 8, trim),
        pxLog(16, 18.666666666666668, 9.5, 24, 26, trim),
        pxBox([14, 25, 8], [18, 27, 17.333333333333332], 'gold'),
        pxBox([15, 26, 10.666666666666666], [16, 28, 13.333333333333334], 'hole'),
      ];
    },
  },
  shelf: {
    description: 'Pixel art shelf with shelves, varied book spines, and seed-generated ornaments.',
    extent: { from: [0, 2, 0], to: [32, 16, 48] },
    build(random) {
      const prims = [
        pxBox([1, 4, 0], [31, 14, 3], 'woodDark'),
        pxBox([1, 4, 0], [3, 14, 45], 'wood', { pattern: 'planks', axis: 2, width: 3 }),
        pxBox([29, 4, 0], [31, 14, 45], 'wood', { pattern: 'planks', axis: 2, width: 3 }),
        pxBox([3, 4, 3], [29, 5, 45], 'woodDark', { pattern: 'planks', axis: 2, width: 4 }),
        pxBox([3, 4, 15], [29, 14, 17], 'wood'),
        pxBox([3, 4, 30], [29, 14, 32], 'wood'),
        pxBox([0, 3, 45], [32, 15, 48], 'woodLight'),
      ];
      for (const [zBase, zTop] of [[3, 15], [17, 30], [32, 45]]) pxBooks(random, zBase, zTop, prims);
      return prims;
    },
  },
  lamp: {
    description: 'Pixel art floor lamp with a glowing shade, metal stem, and gold finial.',
    extent: { from: [4, 4, 0], to: [28, 28, 44] },
    build(random) {
      const [r0, r1] = [[10, 6], [9, 7.5], [10.5, 5]][Math.floor(random() * 3)];
      return [
        pxCyl(16, 16, 0, 2, 7, 6, 'woodDark'),
        pxCyl(16, 16, 2, 3, 3, 2.5, 'metal'),
        pxCyl(16, 16, 3, 30, 1, 1, 'metal'),
        pxCyl(16, 16, 28, 40, r0, r1, 'glow', { pattern: 'pleats' }),
        pxCyl(16, 16, 27.5, 29, r0 + 0.4, r0 + 0.3, 'gold'),
        pxCyl(16, 16, 39.5, 40.5, r1 + 0.5, r1 + 0.5, 'gold'),
        pxBlob([16, 16, 42], [1.4, 1.4, 1.4], 'gold'),
      ];
    },
  },
  plant: {
    description: 'Pixel art clay planter with leaves individually oriented from the seed.',
    extent: { from: [0, 0, 0], to: [32, 32, 44] },
    build(random) {
      const prims = [
        pxCyl(16, 16, 0, 12, 7, 9, 'clay'),
        pxCyl(16, 16, 11, 15, 10, 10, 'clay', { tone: 0.7 }),
        pxCyl(16, 16, 15, 15.5, 8.5, 8.5, 'soil'),
        pxCyl(16, 16, 15, 20, 0.9, 0.6, 'stem'),
      ];
      if (random() < 0.35) {
        const count = 6 + Math.floor(random() * 3);
        for (let index = 0; index < count; index += 1) {
          const angle = (index / count) * Math.PI * 2 + random();
          const reach = 4 + random() * 3.5;
          const radius = 4.2 + random() * 1.6;
          prims.push(pxBlob(
            [16 + Math.cos(angle) * reach, 16 + Math.sin(angle) * reach, 22 + random() * 10],
            [radius, radius, radius * 0.9],
            'leaf',
            { tone: (random() - 0.5) * 1.2 },
          ));
        }
        prims.push(pxBlob([16, 16, 34 + random() * 3], [4.8, 4.8, 4.4], 'leaf', { tone: 0.4 }));
        return prims;
      }
      const count = 8 + Math.floor(random() * 4);
      const turn = random() * Math.PI * 2;
      for (let index = 0; index < count; index += 1) {
        const angle = turn + (index / count) * Math.PI * 2 + (random() - 0.5) * 0.5;
        const upright = index % 3 === 0;
        const elevation = upright ? 1.05 + random() * 0.3 : 0.4 + random() * 0.5;
        const length = upright ? 14 + random() * 4 : 10 + random() * 2.5;
        const dir = [Math.cos(angle) * Math.cos(elevation), Math.sin(angle) * Math.cos(elevation), Math.sin(elevation)];
        const side = [-Math.sin(angle), Math.cos(angle), 0];
        const up = pxCross(dir, side);
        prims.push(pxBlob(
          [16 + dir[0] * (length / 2 + 1), 16 + dir[1] * (length / 2 + 1), 17 + dir[2] * (length / 2 + 1)],
          [length / 2, 2.6 + random() * 0.8, 0.9],
          'leaf',
          { axes: [dir, side, up], tone: (random() - 0.5) * 1.3 },
        ));
      }
      return prims;
    },
  },
  barrel: {
    description: 'Barril pixel art con duelas abombadas, flejes de hierro y tapa de tablones.',
    extent: { from: [5, 5, 0], to: [27, 29, 32] },
    build(random) {
      const narrow = 8.5;
      const wide = 10;
      const lower = (z) => narrow + ((wide - narrow) * z) / 11;
      const upper = (z) => wide - ((wide - narrow) * (z - 11)) / 11;
      const prims = [
        pxCyl(16, 16, 0, 14.666666666666666, narrow, wide, 'wood', { pattern: 'staves' }),
        pxCyl(16, 16, 14.666666666666666, 29.333333333333332, wide, narrow, 'wood', { pattern: 'staves' }),
        pxCyl(16, 16, 2.6666666666666665, 5.333333333333333, lower(2) + 0.6, lower(4) + 0.6, 'metal'),
        pxCyl(16, 16, 24, 26.666666666666668, upper(18) + 0.6, upper(20) + 0.6, 'metal'),
      ];
      if (random() < 0.5) {
        prims.push(pxCyl(16, 16, 12, 17.333333333333332, lower(9) + 0.6, upper(13) + 0.6, 'metal', { tone: -0.3 }));
      }
      if (random() < 0.5) {
        prims.push(pxBox([15, 25, 6.666666666666667], [17, 28, 9.333333333333334], 'metal'));
        prims.push(pxBox([15, 27, 4], [17, 28, 6.666666666666667], 'metal', { tone: -0.5 }));
      }
      return prims;
    },
  },
};

// Add a 1 px outline margin. Size sprites from the maximum furniture volume so
// all four variants share dimensions.
for (const piece of Object.values(PIXEL_FURNITURE)) {
  let minX = Infinity;
  let maxX = -Infinity;
  let minY = Infinity;
  let maxY = -Infinity;
  const { from, to } = piece.extent;
  for (const x of [from[0], to[0]]) {
    for (const y of [from[1], to[1]]) {
      for (const z of [from[2], to[2]]) {
        minX = Math.min(minX, x - y);
        maxX = Math.max(maxX, x - y);
        minY = Math.min(minY, (x + y) / 2 - z);
        maxY = Math.max(maxY, (x + y) / 2 - z);
      }
    }
  }
  piece.bounds = { minX: Math.floor(minX) - 1, minY: Math.floor(minY) - 1 };
  piece.size = { width: Math.ceil(maxX) + 1 - piece.bounds.minX, height: Math.ceil(maxY) + 1 - piece.bounds.minY };
}

// --- Rendering ----------------------------------------------------------------

const PX_SHADOW_COLOR = [22, 18, 34];
// Check the lower neighbor first to choose the outline shade above top faces.
const PX_NEIGHBOURS = [[0, 1], [1, 0], [-1, 0], [0, -1]];

function renderPixelFurniture(key, ramps, seed, options = {}) {
  const piece = PIXEL_FURNITURE[key];
  const prims = piece.build(pxRandom(seed), seed);
  prims.forEach(pxPrepare);
  const { width, height } = piece.size;
  const { minX, minY } = piece.bounds;
  const brightness = options.brightness || 0;
  const count = width * height;
  const hitIndex = new Int16Array(count).fill(-1);
  const depth = new Float32Array(count);
  const level = new Float32Array(count);
  const normalZ = new Float32Array(count);
  const face = new Uint8Array(count);
  const ground = new Uint8Array(count);

  for (let py = 0; py < height; py += 1) {
    for (let px = 0; px < width; px += 1) {
      const i = py * width + px;
      // Offset slightly from center so no edge lands exactly on a tie.
      const sx = minX + px + 0.5 + 0.013;
      const sy = minY + py + 0.5 + 0.007;
      const gx = sy + sx / 2;
      const gy = sy - sx / 2;
      const origin = [gx + PX_FAR, gy + PX_FAR, PX_FAR];
      const hit = pxTrace(prims, origin, PX_VIEW, sx, sy);
      if (!hit) {
        if (options.shadow && pxOccluded(prims, [gx, gy, 0.01], 1, -1)) ground[i] = 1;
        continue;
      }
      const prim = prims[hit.index];
      const role = PIXEL_ROLES[prim.role];
      const n = hit.n;
      const p = [origin[0] - hit.t, origin[1] - hit.t, origin[2] - hit.t];
      const lit = Math.max(0, pxDot(n, PX_LIGHT));
      let tone = role.level + (prim.tone || 0) + (lit - 0.6) * role.contrast + brightness;
      if (!role.emissive) {
        const lift = [p[0] + n[0] * 0.35, p[1] + n[1] * 0.35, p[2] + n[2] * 0.35];
        if (pxOccluded(prims, lift, 0, hit.index)) tone -= 1.5;
      }
      if (role.shine && pxDot(n, PX_HALF) > 0.97) tone += 1.6;
      tone += pxPattern(prim, p, n, seed);
      hitIndex[i] = hit.index;
      depth[i] = hit.t;
      level[i] = tone;
      normalZ[i] = n[2];
      face[i] = n[2] > 0.9 ? 1 : n[1] > 0.9 ? 2 : n[0] > 0.9 ? 3 : 0;
    }
  }

  const final = level.slice();
  const contour = new Uint8Array(count);
  for (let py = 0; py < height; py += 1) {
    for (let px = 0; px < width; px += 1) {
      const i = py * width + px;
      const index = hitIndex[i];
      if (index < 0) continue;
      // Edge highlight: the top row of each side face below the top, and the
      // vertical corner where the two visible faces meet.
      const below = py + 1 < height ? i + width : -1;
      if (below >= 0 && hitIndex[below] === index && normalZ[i] > 0.7 && normalZ[below] < 0.5) final[i] += 1.1;
      if (face[i] === 2 && px + 1 < width && hitIndex[i + 1] === index && face[i + 1] === 3) final[i] += 0.8;
      // Inner line: darken the object behind where another object covers it.
      for (const [dx, dy] of PX_NEIGHBOURS) {
        const nx = px + dx;
        const ny = py + dy;
        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
        const j = ny * width + nx;
        if (hitIndex[j] >= 0 && hitIndex[j] !== index && depth[j] < depth[i] - PX_CONTOUR_DEPTH) contour[i] = 1;
      }
    }
  }

  const data = new Uint8ClampedArray(count * 4);
  const paint = (i, color, alpha = 255) => {
    data[i * 4] = color[0];
    data[i * 4 + 1] = color[1];
    data[i * 4 + 2] = color[2];
    data[i * 4 + 3] = alpha;
  };
  for (let py = 0; py < height; py += 1) {
    for (let px = 0; px < width; px += 1) {
      const i = py * width + px;
      const index = hitIndex[i];
      if (index >= 0) {
        const ramp = ramps[PIXEL_ROLES[prims[index].role].ramp];
        let step = Math.max(0, Math.min(7, Math.round(final[i])));
        if (contour[i]) step = Math.max(0, Math.min(step - 3, 1));
        paint(i, ramp.colors[step]);
        continue;
      }
      // Colored outer outline: use the neighbor's darkest shade, or a slightly
      // lighter one above lit top faces (selective outlining).
      let outline = null;
      for (const [dx, dy] of PX_NEIGHBOURS) {
        const nx = px + dx;
        const ny = py + dy;
        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
        const j = ny * width + nx;
        if (hitIndex[j] < 0) continue;
        const ramp = ramps[PIXEL_ROLES[prims[hitIndex[j]].role].ramp];
        outline = dy === 1 && face[j] === 1 ? ramp.rim : ramp.outline;
        break;
      }
      if (outline) paint(i, outline);
      else if (ground[i]) paint(i, PX_SHADOW_COLOR, 110);
    }
  }
  // Soft shadow edge: add a second, fainter stepped shade.
  if (options.shadow) {
    for (let py = 0; py < height; py += 1) {
      for (let px = 0; px < width; px += 1) {
        const i = py * width + px;
        if (data[i * 4 + 3] !== 0) continue;
        const near = PX_NEIGHBOURS.some(([dx, dy]) => {
          const nx = px + dx;
          const ny = py + dy;
          return nx >= 0 && ny >= 0 && nx < width && ny < height && ground[ny * width + nx] && hitIndex[ny * width + nx] < 0;
        });
        if (near) paint(i, PX_SHADOW_COLOR, 55);
      }
    }
  }
  return { width, height, data };
}
