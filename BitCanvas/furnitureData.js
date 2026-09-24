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
