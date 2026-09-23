import assert from "node:assert/strict";
import test from "node:test";
import type { components } from "../src/net/schema";
import { ChunkStore } from "../src/world/ChunkStore";
import { pickTile } from "../src/world/pick";
import {
  EDGE_N_DOORWAY,
  EDGE_N_WINDOW,
  EDGE_W_DOORWAY,
  EDGE_W_WINDOW,
  LEVEL_VOID,
  NO_FLOOR,
  canEnter,
  wallBetween,
} from "../src/world/rules";

type Chunk = components["schemas"]["ChunkResponse"];
type Level = components["schemas"]["ChunkLevelResponse"];
const CELL_COUNT = 32 * 32;
const targetIndex = 1;

const materials = new Map([
  [1, { walkable: true, walk_cost: 1, solid: true }],
  [2, { walkable: false, walk_cost: 1, solid: false }],
  [3, { walkable: true, walk_cost: 1, solid: true }],
  [4, { walkable: true, walk_cost: 1, solid: false }],
  [5, { key: "wood", walkable: true, walk_cost: 1.2, solid: true }],
]);

const chestKind: components["schemas"]["ObjectKindResponse"] = {
  key: "chest", name: "Chest", material: "wood", mass: 15, bulk: 120, height: 2,
  solid: true, surface: true, openable: true, container_capacity: 100,
};

function makeLevel(
  z: number,
  cell: Partial<Pick<Level, "floor_h" | "floor_mat" | "wall_n" | "wall_w" | "edge_flags" | "flags">> & {
    floorHeight?: number;
    floorMaterial?: number;
    northWall?: number;
    westWall?: number;
    edgeFlags?: number;
    cellFlags?: number;
    index?: number;
  } = {},
): Level {
  const fillCell = (value: number | undefined, fallback: number): number[] => {
    const cells = Array(CELL_COUNT).fill(fallback) as number[];
    cells[cell.index ?? targetIndex] = value ?? fallback;
    return cells;
  };
  return {
    z,
    floor_h: fillCell(cell.floorHeight, NO_FLOOR),
    floor_mat: fillCell(cell.floorMaterial, 1),
    wall_n: fillCell(cell.northWall, 0),
    wall_w: fillCell(cell.westWall, 0),
    edge_flags: fillCell(cell.edgeFlags, 0),
    flags: fillCell(cell.cellFlags, 0),
  };
}

function makeChunk(options: { groundH?: number; targetMaterial?: number; levels?: Level[]; objects?: Chunk["objects"] } = {}): Chunk {
  const ground = Array(CELL_COUNT).fill(options.groundH ?? 0) as number[];
  const surface = Array(CELL_COUNT).fill(1) as number[];
  surface[targetIndex] = options.targetMaterial ?? 1;
  return {
    cx: 0,
    cy: 0,
    revision: 1,
    ground_h: ground,
    surface_mat: surface,
    levels: options.levels ?? [],
    objects: options.objects ?? [],
  };
}

function makeStore(options: Parameters<typeof makeChunk>[0] = {}): ChunkStore {
  const store = new ChunkStore();
  store.setMaterials(materials);
  store.setObjectKinds(new Map([[chestKind.key, chestKind]]));
  store.set(makeChunk(options));
  return store;
}

test("standing surfaces and headroom mirror the server grid", async (t) => {
  const cases = [
    { name: "walkable ground", options: {}, expected: true },
    { name: "deep water", options: { targetMaterial: 2 }, expected: false },
    {
      name: "ground void",
      options: { levels: [makeLevel(0, { cellFlags: LEVEL_VOID })] },
      expected: false,
    },
    {
      name: "solid slab at h+3 blocks headroom",
      options: { levels: [makeLevel(0, { floorHeight: 3, floorMaterial: 3 })] },
      expected: false,
    },
    {
      name: "solid slab at h+4 only touches the body",
      options: { levels: [makeLevel(0, { floorHeight: 4, floorMaterial: 3 })] },
      expected: true,
    },
    {
      name: "non-solid slab does not consume headroom",
      options: { levels: [makeLevel(0, { floorHeight: 3, floorMaterial: 4 })] },
      expected: true,
    },
    {
      name: "non-walkable stored floor",
      options: { levels: [makeLevel(1, { floorHeight: 6, floorMaterial: 2 })] },
      expected: false,
      height: 6,
    },
  ];

  for (const scenario of cases) {
    await t.test(scenario.name, () => {
      const store = makeStore(scenario.options);
      const height = scenario.height ?? 0;
      assert.equal(canEnter(store, 0.5, 0.5, 1.1, 0.5, height), scenario.expected);
    });
  }
});

test("solid object volumes block the ground while their tops remain standable", () => {
  const store = makeStore({ objects: [{ id: 1, kind: "chest", x: 1, y: 0, h: 0, quantity: 1, open: null }] });
  assert.equal(store.isObjectSolidAt(1, 0, 1), true);
  assert.equal(store.isObjectSolidAt(1, 0, 2), true);
  assert.equal(store.isObjectSolidAt(1, 0, 3), false);
  assert.equal(store.hasHeadroom(1, 0, 0), false);
  assert.equal(store.standingH(1, 0, 0), undefined);
  assert.equal(store.standingH(1, 0, 2), 2);
  assert.equal(store.walkCost(1, 0, 2), 1.2);
  assert.equal(canEnter(store, 0.5, 0.5, 1.1, 0.5, 0), false);
});

test("pickTile hits a table tile at the solid object's top", () => {
  const table: components["schemas"]["ObjectKindResponse"] = { ...chestKind, key: "table", name: "Table" };
  const store = makeStore({ objects: [{ id: 1, kind: "table", x: 1, y: 1, h: 0, quantity: 1, open: null }] });
  store.setObjectKinds(new Map([[table.key, table]]));
  assert.deepEqual(pickTile(store, 0, 1, 2, () => Number.POSITIVE_INFINITY), { x: 1, y: 1, z: 0 });
});

test("wall spans, floorless supports, and openings mirror the server grid", async (t) => {
  const cases = [
    {
      name: "floorless wall uses the supporting ground height",
      groundH: 2,
      level: makeLevel(1, { northWall: 3 }),
      h: 1,
      expected: true,
      direction: "north" as const,
    },
    {
      name: "floorless wall uses the nearest lower slab",
      groundH: 0,
      level: makeLevel(1, { northWall: 3 }),
      lowerLevel: makeLevel(0, { floorHeight: 8 }),
      h: 4,
      expected: false,
      direction: "north" as const,
    },
    {
      name: "wall span outside the body height does not block",
      groundH: 0,
      level: makeLevel(1, { floorHeight: 12, northWall: 3 }),
      h: 0,
      expected: false,
      direction: "north" as const,
    },
    {
      name: "north doorway permits passage",
      groundH: 0,
      level: makeLevel(0, { floorHeight: 0, northWall: 3, edgeFlags: EDGE_N_DOORWAY }),
      h: 0,
      expected: false,
      direction: "north" as const,
    },
    {
      name: "north window blocks passage",
      groundH: 0,
      level: makeLevel(0, { floorHeight: 0, northWall: 3, edgeFlags: EDGE_N_WINDOW }),
      h: 0,
      expected: true,
      direction: "north" as const,
    },
    {
      name: "west doorway permits passage",
      groundH: 0,
      level: makeLevel(0, { floorHeight: 0, westWall: 3, edgeFlags: EDGE_W_DOORWAY }),
      h: 0,
      expected: false,
      direction: "west" as const,
    },
    {
      name: "west window blocks passage",
      groundH: 0,
      level: makeLevel(0, { floorHeight: 0, westWall: 3, edgeFlags: EDGE_W_WINDOW }),
      h: 0,
      expected: true,
      direction: "west" as const,
    },
  ];

  for (const scenario of cases) {
    await t.test(scenario.name, () => {
      const levels = [scenario.level, ...(scenario.lowerLevel ? [scenario.lowerLevel] : [])];
      const store = makeStore({ groundH: scenario.groundH, levels });
      const blocked = scenario.direction === "north"
        ? wallBetween(store, 1, 0, 1, -1, scenario.h)
        : wallBetween(store, 1, 0, 0, 0, scenario.h);
      assert.equal(blocked, scenario.expected);
    });
  }
});
