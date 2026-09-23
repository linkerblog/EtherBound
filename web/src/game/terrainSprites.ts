export const TERRAIN_SPRITE_FILES: Record<string, string> = {
  grass: "grass/grass_x4.png",
  wood_floor: "floor/planks_x4.png",
  concrete: "floor/concrete_x4.png",
  asphalt: "floor/asphalt_x4.png",
  roofing: "floor/roofing_x4.png",
  brick: "wall/brick_x4.png",
};

export const TERRAIN_SIDE_FILES: Record<string, string> = {
  grass: "grass/grass_side_x4.png",
  wood_floor: "floor/planks_side_x4.png",
  concrete: "floor/concrete_side_x4.png",
  asphalt: "floor/asphalt_side_x4.png",
  roofing: "floor/roofing_side_x4.png",
  brick: "wall/brick_side_x4.png",
};

export function shadeColor(color: number, h: number): number {
  const offset = Math.max(-20, Math.min(48, -h * 3));
  const red = Math.max(0, Math.min(255, (color >> 16) + offset));
  const green = Math.max(0, Math.min(255, ((color >> 8) & 0xff) + offset));
  const blue = Math.max(0, Math.min(255, (color & 0xff) + offset));
  return (red << 16) | (green << 8) | blue;
}

export function spriteTint(color: number, h: number): number {
  const shaded = shadeColor(color, h);
  const ratio = (channel: number, base: number): number =>
    base === 0 ? 255 : Math.min(255, Math.round(channel * 255 / base));
  return (ratio((shaded >> 16) & 0xff, (color >> 16) & 0xff) << 16) |
    (ratio((shaded >> 8) & 0xff, (color >> 8) & 0xff) << 8) |
    ratio(shaded & 0xff, color & 0xff);
}

/** The face tint: the same height shading as the top above it, scaled by the side light. */
export function sideTint(color: number, h: number, light: number): number {
  const tint = spriteTint(color, h);
  const scale = (channel: number): number => Math.max(0, Math.min(255, Math.round(channel * light)));
  return (scale((tint >> 16) & 0xff) << 16) | (scale((tint >> 8) & 0xff) << 8) | scale(tint & 0xff);
}

/** Per-unit side variant, hashed from the owner tile, side and unit top. */
export function sideVariant(x: number, y: number, side: "s" | "e" | "n" | "w", h: number): 0 | 1 | 2 | 3 {
  let hash = Math.imul(x, 0x45d9f3b) ^ Math.imul(y, 0x119de1f3) ^ Math.imul(h, 0x27d4eb2f);
  if (side === "e") hash ^= 0x9e3779b9;
  else if (side === "n") hash ^= 0x7f4a7c15;
  else if (side === "w") hash ^= 0x94d049bb;
  hash = Math.imul(hash ^ (hash >>> 16), 0x45d9f3b);
  return ((hash ^ (hash >>> 16)) & 3) as 0 | 1 | 2 | 3;
}
