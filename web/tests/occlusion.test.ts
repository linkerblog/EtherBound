import assert from "node:assert/strict";
import test from "node:test";
import type { components } from "../src/net/schema";
import { isCovered, occludingStructures } from "../src/world/occlusion";
import { ChunkStore } from "../src/world/ChunkStore";
import { NO_FLOOR } from "../src/world/rules";

type Chunk = components["schemas"]["ChunkResponse"];
type Level = components["schemas"]["ChunkLevelResponse"];
const SIZE = 32;
const tileIndex = (x: number, y: number): number => y * SIZE + x;

function makeStore(options: { buildingAt?: number; floors?: number[]; hill?: boolean } = {}): ChunkStore {
  const ground = Array(SIZE * SIZE).fill(0) as number[];
  if (options.hill) ground[tileIndex(10, 10)] = 12;
  const levels: Level[] = (options.floors ?? []).map((floorH) => {
    const floor = Array(SIZE * SIZE).fill(NO_FLOOR) as number[];
    const at = options.buildingAt ?? 9;
    for (let y = at; y < at + 4; y += 1) {
      for (let x = at; x < at + 4; x += 1) floor[tileIndex(x, y)] = floorH;
    }
    return {
      z: Math.floor(floorH / 6),
      floor_h: floor,
      floor_mat: Array(SIZE * SIZE).fill(1) as number[],
      wall_n: Array(SIZE * SIZE).fill(0) as number[],
      wall_w: Array(SIZE * SIZE).fill(0) as number[],
      edge_flags: Array(SIZE * SIZE).fill(0) as number[],
      flags: Array(SIZE * SIZE).fill(0) as number[],
    };
  });
  const chunk: Chunk = {
    cx: 0,
    cy: 0,
    revision: 1,
    ground_h: ground,
    surface_mat: Array(SIZE * SIZE).fill(1) as number[],
    levels,
  };
  const store = new ChunkStore();
  store.set(chunk);
  return store;
}

test("a structure in front of Niko is found as one connected group with a storey cutoff", () => {
  const store = makeStore({ floors: [-6, 6, 12] });
  const structures = occludingStructures(store, 4, 4, 0, Infinity);
  assert.equal(structures.length, 1);
  assert.equal(structures[0]?.tiles.size, 16);
  assert.deepEqual(structures[0]?.bounds, { minX: 9, minY: 9, maxX: 13, maxY: 13 });
  assert.equal(structures[0]?.cutoff, 4);
});

test("a structure behind Niko is not selected and the cutoff keeps the highest nearby floor", () => {
  const store = makeStore({ buildingAt: 8, floors: [6, 12] });
  assert.deepEqual(occludingStructures(store, 16, 16, 4, Infinity), []);
  const structures = occludingStructures(store, 4, 4, 4, Infinity);
  assert.equal(structures.length, 1);
  assert.equal(structures[0]?.cutoff, 10);
});

test("structure discovery uses the uncut world while visibility follows the structure cutoff", () => {
  const store = makeStore({ floors: [-6, 6, 12] });
  assert.equal(isCovered(store, 4, 4, 0, () => Infinity), true);
  const cut = occludingStructures(store, 4, 4, 0, Infinity);
  assert.equal(cut.length, 1);
  assert.equal(occludingStructures(store, 4, 4, 0, Infinity).length, 1);
  assert.equal(isCovered(store, 4, 4, 0, () => cut[0]!.cutoff), false);
});

test("a hill can cover Niko without being misidentified as a structure", () => {
  const store = makeStore({ hill: true });
  assert.deepEqual(occludingStructures(store, 4, 4, 0, Infinity), []);
  assert.equal(isCovered(store, 4, 4, 0, () => Infinity), true);
});
