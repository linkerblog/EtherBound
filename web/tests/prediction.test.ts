import assert from "node:assert/strict";
import test from "node:test";
import type { components } from "../src/net/schema";
import { ClientPrediction, loadMultiplier } from "../src/net/prediction";
import { ChunkStore } from "../src/world/ChunkStore";
import { NO_FLOOR } from "../src/world/rules";

type Chunk = components["schemas"]["ChunkResponse"];
type Level = components["schemas"]["ChunkLevelResponse"];
const CELL_COUNT = 32 * 32;
const BRICK = 2;

function storeWithWall(edge: "north" | "west", wallIndex: number): ChunkStore {
  const wallN = Array(CELL_COUNT).fill(0) as number[];
  const wallW = Array(CELL_COUNT).fill(0) as number[];
  (edge === "north" ? wallN : wallW)[wallIndex] = BRICK;
  const levels: Level[] = [{
    z: 0,
    floor_h: Array(CELL_COUNT).fill(NO_FLOOR) as number[],
    floor_mat: Array(CELL_COUNT).fill(1) as number[],
    wall_n: wallN,
    wall_w: wallW,
    edge_flags: Array(CELL_COUNT).fill(0) as number[],
    flags: Array(CELL_COUNT).fill(0) as number[],
  }];
  const chunk: Chunk = {
    cx: 0,
    cy: 0,
    revision: 1,
    ground_h: Array(CELL_COUNT).fill(0) as number[],
    surface_mat: Array(CELL_COUNT).fill(1) as number[],
    levels,
    objects: [],
  };
  const store = new ChunkStore();
  store.setMaterials(new Map([
    [1, { walkable: true, walk_cost: 1, solid: true }],
    [BRICK, { walkable: false, walk_cost: 1, solid: true }],
  ]));
  store.set(chunk);
  return store;
}

test("prediction keeps the 0.3 m body radius from blocked wall edges", () => {
  const cases = [
    { edge: "west" as const, index: 1, start: { x: 0.5, y: 0.5 }, direction: { x: 1, y: 0 }, axis: "x" as const, expected: 0.7 },
    { edge: "west" as const, index: 1, start: { x: 1.5, y: 0.5 }, direction: { x: -1, y: 0 }, axis: "x" as const, expected: 1.3 },
    { edge: "north" as const, index: 33, start: { x: 1.5, y: 0.5 }, direction: { x: 0, y: 1 }, axis: "y" as const, expected: 0.7 },
    { edge: "north" as const, index: 33, start: { x: 1.5, y: 1.5 }, direction: { x: 0, y: -1 }, axis: "y" as const, expected: 1.3 },
  ];

  for (const scenario of cases) {
    const prediction = new ClientPrediction({ ...scenario.start, z: 0, h: 0 });
    prediction.attachStore(storeWithWall(scenario.edge, scenario.index));
    const result = prediction.render(scenario.direction, 0.25);
    assert.ok(Math.abs(result[scenario.axis] - scenario.expected) < 1e-9);
  }
});

test("movement prediction applies the server load multiplier", () => {
  assert.equal(loadMultiplier(10), 1);
  assert.equal(loadMultiplier(25), 0.75);
  assert.equal(loadMultiplier(40), 0.5);
  const prediction = new ClientPrediction({ x: 0, y: 0, z: 0, h: 0 });
  prediction.setLoadKg(40);
  assert.deepEqual(prediction.render({ x: 1, y: 0 }, 0.25), { x: 0.5, y: 0, z: 0, h: 0 });
});
