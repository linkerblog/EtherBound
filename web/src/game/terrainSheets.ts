import asphalt from "../../../src/sprites/floor/asphalt_x4.png";
import asphaltSide from "../../../src/sprites/floor/asphalt_side_x4.png";
import brick from "../../../src/sprites/wall/brick_x4.png";
import brickSide from "../../../src/sprites/wall/brick_side_x4.png";
import concrete from "../../../src/sprites/floor/concrete_x4.png";
import concreteSide from "../../../src/sprites/floor/concrete_side_x4.png";
import woodFloor from "../../../src/sprites/floor/planks_x4.png";
import planksSide from "../../../src/sprites/floor/planks_side_x4.png";
import roofing from "../../../src/sprites/floor/roofing_x4.png";
import roofingSide from "../../../src/sprites/floor/roofing_side_x4.png";
import grass from "../../../src/sprites/grass/grass_x4.png";
import grassSide from "../../../src/sprites/grass/grass_side_x4.png";

// Vite serves imported assets through its allow list; resolving a file outside `web/` through
// `import.meta.url` is refused, so every sheet must be a static import here. Node tests read this
// file as text and check one import per entry in `TERRAIN_SPRITE_FILES` and `TERRAIN_SIDE_FILES`.
export const TERRAIN_SHEETS: Record<string, string> = {
  grass,
  wood_floor: woodFloor,
  concrete,
  asphalt,
  roofing,
  brick,
};

export const TERRAIN_SIDE_SHEETS: Record<string, string> = {
  grass: grassSide,
  wood_floor: planksSide,
  concrete: concreteSide,
  asphalt: asphaltSide,
  roofing: roofingSide,
  brick: brickSide,
};