import concrete from "../../../src/sprites/floor/stone_x4.png";
import stoneSide from "../../../src/sprites/floor/stone_side_x4.png";
import woodFloor from "../../../src/sprites/floor/planks_x4.png";
import planksSide from "../../../src/sprites/floor/planks_side_x4.png";
import grass from "../../../src/sprites/grass/grass_x4.png";
import grassSide from "../../../src/sprites/grass/grass_side_x4.png";

// Vite serves imported assets through its allow list; resolving a file outside `web/` through
// `import.meta.url` is refused, so every sheet must be a static import here. Node tests read this
// file as text and check one import per entry in `TERRAIN_SPRITE_FILES` and `TERRAIN_SIDE_FILES`.
export const TERRAIN_SHEETS: Record<string, string> = {
  grass,
  wood_floor: woodFloor,
  concrete,
};

export const TERRAIN_SIDE_SHEETS: Record<string, string> = {
  grass: grassSide,
  wood_floor: planksSide,
  concrete: stoneSide,
};