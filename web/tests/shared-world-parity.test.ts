import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import type { components } from "../src/net/schema";
import { ChunkStore } from "../src/world/ChunkStore";
import { NO_FLOOR, wallBetween } from "../src/world/rules";

type Chunk = components["schemas"]["ChunkResponse"];
type Level = components["schemas"]["ChunkLevelResponse"];
type LevelSpec = {
  z: number;
  floor_h?: number;
  floor_material?: string;
  wall_n?: string;
  wall_w?: string;
  edge_flags?: number;
  flags?: number;
};
type StandingCase = {
  name: string;
  ground_h: number;
  ground_material: string;
  actor_h: number;
  expected_h: number | null;
  levels: LevelSpec[];
};
type WallCase = {
  name: string;
  ground_h: number;
  actor_h: number;
  direction: "north" | "west";
  expected_blocked: boolean;
  levels: LevelSpec[];
};

const CELL_COUNT = 32 * 32;
const INDEX = 33;
const materialInfo = {
  grass: { walkable: true, walk_cost: 1, solid: true },
  water_deep: { walkable: false, walk_cost: 1, solid: false },
  concrete: { walkable: true, walk_cost: 1, solid: true },
  water_shallow: { walkable: true, walk_cost: 1.6, solid: false },
  brick: { walkable: false, walk_cost: 1, solid: true },
} as const;
const materialKeys = Object.keys(materialInfo) as (keyof typeof materialInfo)[];
const materialIds = new Map(materialKeys.map((key, index) => [key, index + 1]));
const materials = new Map(
  materialKeys.map((key) => [materialIds.get(key)!, materialInfo[key]]),
);
const parity = JSON.parse(
  readFileSync(new URL("../../server/tests/world-parity.json", import.meta.url), "utf8"),
) as { standing: StandingCase[]; walls: WallCase[] };

function makeLevel(spec: LevelSpec): Level {
  const floorH = Array(CELL_COUNT).fill(NO_FLOOR) as number[];
  const floorMat = Array(CELL_COUNT).fill(materialIds.get("grass")!) as number[];
  const wallN = Array(CELL_COUNT).fill(0) as number[];
  const wallW = Array(CELL_COUNT).fill(0) as number[];
  const edgeFlags = Array(CELL_COUNT).fill(0) as number[];
  const flags = Array(CELL_COUNT).fill(0) as number[];
  if (spec.floor_h !== undefined) {
    floorH[INDEX] = spec.floor_h;
    floorMat[INDEX] = materialIds.get(spec.floor_material ?? "grass")!;
  }
  if (spec.wall_n) wallN[INDEX] = materialIds.get(spec.wall_n)!;
  if (spec.wall_w) wallW[INDEX] = materialIds.get(spec.wall_w)!;
  edgeFlags[INDEX] = spec.edge_flags ?? 0;
  flags[INDEX] = spec.flags ?? 0;
  return {
    z: spec.z,
    floor_h: floorH,
    floor_mat: floorMat,
    wall_n: wallN,
    wall_w: wallW,
    edge_flags: edgeFlags,
    flags,
  };
}

function makeStore(groundH: number, groundMaterial: string, levels: LevelSpec[]): ChunkStore {
  const chunk: Chunk = {
    cx: 0,
    cy: 0,
    revision: 1,
    ground_h: Array(CELL_COUNT).fill(groundH) as number[],
    surface_mat: Array(CELL_COUNT).fill(materialIds.get(groundMaterial as keyof typeof materialInfo)!) as number[],
    levels: levels.map(makeLevel),
  };
  const store = new ChunkStore();
  store.setMaterials(materials);
  store.set(chunk);
  return store;
}

test("client standing rules match the shared server parity table", () => {
  for (const scenario of parity.standing) {
    const store = makeStore(scenario.ground_h, scenario.ground_material, scenario.levels);
    assert.equal(
      store.standingH(1, 1, scenario.actor_h) ?? null,
      scenario.expected_h,
      scenario.name,
    );
  }
});

test("client wall rules match the shared server parity table", () => {
  for (const scenario of parity.walls) {
    const store = makeStore(scenario.ground_h, "grass", scenario.levels);
    const blocked = scenario.direction === "north"
      ? wallBetween(store, 1, 1, 1, 0, scenario.actor_h)
      : wallBetween(store, 1, 1, 0, 1, scenario.actor_h);
    assert.equal(blocked, scenario.expected_blocked, scenario.name);
  }
});

test("snapshot clearing accepts reset chunk revisions for a regenerated world", () => {
  const store = makeStore(0, "grass", []);
  const original = store.get(0, 0)!;
  store.set({ ...original, revision: 9 });
  store.clear();
  assert.equal(store.set({ ...original, revision: 1 }), true);
  assert.equal(store.get(0, 0)?.revision, 1);
});
