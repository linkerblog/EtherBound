export type ObjectSprite = { file: string; anchorX: number; anchorY: number };

export const OBJECT_SPRITES: Record<string, ObjectSprite> = {
  table: { file: "table.png", anchorX: 33, anchorY: 47 },
  chair: { file: "chair.png", anchorX: 23, anchorY: 44 },
  chest: { file: "chest.png", anchorX: 26, anchorY: 46 },
  shelf: { file: "shelf.png", anchorX: 17, anchorY: 64 },
  barrel: { file: "barrel.png", anchorX: 25, anchorY: 44 },
};
