export const TERRAIN_SPRITE_FILES: Record<string, string> = {
  grass: "grass/grass_x4.png",
  wood_floor: "floor/planks_x4.png",
  concrete: "floor/stone_x4.png",
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
