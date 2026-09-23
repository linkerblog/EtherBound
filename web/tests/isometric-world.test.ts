import assert from "node:assert/strict";
import test from "node:test";
import type { components } from "../src/net/schema";
import { baseDepth, faceTile, keysToWorld, nikoDepth, rowDepth, screenToRay, toScreen } from "../src/game/iso";
import { ChunkStore } from "../src/world/ChunkStore";
import { cutoffH } from "../src/world/cutaway";
import { pickTile } from "../src/world/pick";
import { marchRay } from "../src/world/ray";
import { EDGE_N_DOORWAY, EDGE_N_WINDOW, LEVEL_VOID, NO_FLOOR } from "../src/world/rules";

type Chunk = components["schemas"]["ChunkResponse"];
type Level = components["schemas"]["ChunkLevelResponse"];
const SIZE = 32;
const CELL_COUNT = SIZE * SIZE;
const cellIndex = (x: number, y: number): number => y * SIZE + x;

function makeLevel(
  z: number,
  cells: Record<number, Partial<Pick<Level, "floor_h" | "wall_n" | "edge_flags" | "flags">>> = {},
): Level {
  const values = (key: "floor_h" | "wall_n" | "edge_flags" | "flags", fallback: number): number[] => {
    const result = Array(CELL_COUNT).fill(fallback) as number[];
    for (const [index, cell] of Object.entries(cells)) {
      result[Number(index)] = cell[key] ?? fallback;
    }
    return result;
  };
  return {
    z,
    floor_h: values("floor_h", NO_FLOOR),
    floor_mat: Array(CELL_COUNT).fill(1) as number[],
    wall_n: values("wall_n", 0),
    wall_w: Array(CELL_COUNT).fill(0) as number[],
    edge_flags: values("edge_flags", 0),
    flags: values("flags", 0),
  };
}

function makeStore(options: { groundH?: number; levels?: Level[] } = {}): ChunkStore {
  const store = new ChunkStore();
  const chunk: Chunk = {
    cx: 0,
    cy: 0,
    revision: 1,
    ground_h: Array(CELL_COUNT).fill(options.groundH ?? 0) as number[],
    surface_mat: Array(CELL_COUNT).fill(1) as number[],
    levels: options.levels ?? [],
  };
  store.set(chunk);
  return store;
}

test("isometric projection, ray coordinates, key vectors, and depths follow the grid", () => {
  assert.deepEqual(toScreen(2, 1, 3), { sx: 32, sy: 0 });
  assert.deepEqual(screenToRay(32, 16), { s: 1, t: 1 });
  assert.deepEqual(keysToWorld(0, 0), { x: 0, y: 0 });
  const screenUp = keysToWorld(0, -1);
  assert.ok(Math.abs(screenUp.x + Math.SQRT1_2) < 1e-12);
  assert.ok(Math.abs(screenUp.y + Math.SQRT1_2) < 1e-12);
  assert.deepEqual(keysToWorld(1, -1), { x: 0, y: -1 });
  assert.equal(baseDepth(3, 4), -999_993);
  assert.equal(rowDepth(7), 14);
  assert.equal(nikoDepth(2.8, 3.1), 11);
  assert.deepEqual(faceTile("s", 4, 7), { x: 4, y: 7 });
  assert.deepEqual(faceTile("e", 4, 7), { x: 4, y: 7 });
});

test("roof cutoff uses nearby slabs and preserves doorway versus window connectivity", () => {
  const ownCell = cellIndex(1, 1);
  const northCell = cellIndex(1, 0);
  const windowStore = makeStore({ levels: [makeLevel(1, {
    [ownCell]: { wall_n: 1, edge_flags: EDGE_N_WINDOW },
    [northCell]: { floor_h: 6 },
  })] });
  assert.equal(cutoffH(windowStore, 1.5, 1.5, 0), Infinity);

  const doorwayStore = makeStore({ levels: [makeLevel(1, {
    [ownCell]: { wall_n: 1, edge_flags: EDGE_N_DOORWAY },
    [northCell]: { floor_h: 6 },
  })] });
  assert.equal(cutoffH(doorwayStore, 1.5, 1.5, 0), 4);
});

test("roof cutoff recognizes underground ground and does not filter void-flagged slabs", () => {
  const underground = makeStore({ groundH: 2 });
  assert.equal(cutoffH(underground, 1.5, 1.5, 0), 4);

  const voidFloor = makeStore({ levels: [makeLevel(1, {
    [cellIndex(1, 1)]: { floor_h: 6, flags: LEVEL_VOID },
  })] });
  assert.equal(cutoffH(voidFloor, 1.5, 1.5, 0), 4);
});

test("a directly overhead roof triggers cutaway and hides its higher floors", () => {
  const roofed = makeStore({ levels: [makeLevel(2, { [cellIndex(1, 1)]: { floor_h: 12 } })] });
  const cutoff = cutoffH(roofed, 1.5, 1.5, 0);
  assert.equal(cutoff, 4);
  assert.equal(pickTile(roofed, 0, -10, 0, () => cutoff), null);
});

test("height-aware picking selects ground and visible floors, but skips cutaway floors", () => {
  const groundStore = makeStore();
  assert.deepEqual(pickTile(groundStore, 0, 3, 0, () => Infinity), { x: 1, y: 1, z: 0 });
  assert.deepEqual(marchRay(groundStore, 0, 3, 40, -40, () => Infinity), {
    x: 1, y: 1, h: 0, kind: "ground", z: 0,
  });

  const floorStore = makeStore({ levels: [makeLevel(1, {
    [cellIndex(1, 1)]: { floor_h: 6 },
  })] });
  assert.deepEqual(pickTile(floorStore, 0, -3, 0, () => Infinity), { x: 1, y: 1, z: 1 });
  assert.deepEqual(marchRay(floorStore, 0, -3, 40, -40, () => Infinity), {
    x: 1, y: 1, h: 6, kind: "floor", z: 1,
  });
  assert.equal(pickTile(floorStore, 0, -3, 0, () => 4), null);
});

test("height-aware picking includes VOID floors and skips VOID ground to find the basement", () => {
  const voidFloor = makeStore({ levels: [makeLevel(1, {
    [cellIndex(1, 1)]: { floor_h: 6, flags: LEVEL_VOID },
  })] });
  assert.deepEqual(pickTile(voidFloor, 0, -3, 0, () => Infinity), { x: 1, y: 1, z: 1 });

  const voidBand = makeLevel(2);
  voidBand.flags.fill(LEVEL_VOID);
  const voidGround = makeStore({ groundH: 12, levels: [
    makeLevel(1, { [cellIndex(1, 1)]: { floor_h: 6 } }),
    voidBand,
  ] });
  assert.equal(voidGround.isVoid(1, 1, 12), true);
  assert.deepEqual(pickTile(voidGround, 0, -3, 0, () => Infinity), { x: 1, y: 1, z: 1 });
});

test("solid top height falls to the bottom of contiguous void bands under the ground", () => {
  assert.equal(makeStore({ groundH: 7 }).solidTopH(1, 1), 7);

  const z1 = makeLevel(1);
  const z2 = makeLevel(2);
  z1.flags.fill(LEVEL_VOID);
  z2.flags.fill(LEVEL_VOID);
  assert.equal(makeStore({ groundH: 12, levels: [z1, z2] }).solidTopH(1, 1), 6);

  const onlyZ2 = makeLevel(2);
  onlyZ2.flags.fill(LEVEL_VOID);
  assert.equal(makeStore({ groundH: 12, levels: [onlyZ2] }).solidTopH(1, 1), 12);

  assert.equal(makeStore().solidTopH(100, 100), undefined);
});
