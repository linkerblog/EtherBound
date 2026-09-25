import type { components } from "../../src/net/schema";
import { ChunkStore } from "../../src/world/ChunkStore";
import { NO_FLOOR } from "../../src/world/rules";

type Chunk = components["schemas"]["ChunkResponse"];
type Level = components["schemas"]["ChunkLevelResponse"];
const SIZE = 32;

export function labBuilding(): ChunkStore {
  const ground = Array(SIZE * SIZE).fill(2) as number[];
  const levels: Level[] = [0, 1, 2].map((z) => ({
    z,
    floor_h: Array(SIZE * SIZE).fill(NO_FLOOR) as number[],
    floor_mat: Array(SIZE * SIZE).fill(1) as number[],
    wall_n: Array(SIZE * SIZE).fill(0) as number[],
    wall_w: Array(SIZE * SIZE).fill(0) as number[],
    edge_flags: Array(SIZE * SIZE).fill(0) as number[],
    flags: Array(SIZE * SIZE).fill(0) as number[],
  }));
  const index = (x: number, y: number): number => (y - SIZE) * SIZE + x;
  const setFloor = (x: number, y: number, h: number): void => {
    const level = levels.find((entry) => entry.z === Math.floor(h / 6))!;
    level.floor_h[index(x, y)] = h;
  };
  const clearFloor = (x: number, y: number, z: number): void => {
    levels.find((entry) => entry.z === z)!.floor_h[index(x, y)] = NO_FLOOR;
  };
  for (const [z, floorH] of [[0, 2], [1, 8], [2, 14]]) {
    const level = levels[z]!;
    for (let y = 46; y < 52; y += 1) {
      for (let x = 18; x < 26; x += 1) level.floor_h[index(x, y)] = floorH;
    }
  }
  for (const level of levels) {
    for (let y = 46; y < 52; y += 1) {
      level.wall_w[index(18, y)] = 1;
      level.wall_w[index(26, y)] = 1;
    }
    for (let x = 18; x < 26; x += 1) {
      level.wall_n[index(x, 46)] = 1;
      level.wall_n[index(x, 52)] = 1;
    }
  }
  for (let step = 0; step < 6; step += 1) {
    const x = 18 + step;
    const floorH = 3 + step;
    if (step < 3) clearFloor(x, 46, 1);
    setFloor(x, 46, floorH);
  }
  for (let step = 0; step < 6; step += 1) {
    const x = 18 + step;
    const floorH = 9 + step;
    if (step < 3) clearFloor(x, 47, 2);
    setFloor(x, 47, floorH);
  }

  const store = new ChunkStore();
  const chunk: Chunk = {
    cx: 0,
    cy: 1,
    revision: 1,
    ground_h: ground,
    surface_mat: Array(SIZE * SIZE).fill(1) as number[],
    levels,
  };
  store.set(chunk);
  return store;
}
