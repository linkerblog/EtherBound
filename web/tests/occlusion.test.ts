import assert from "node:assert/strict";
import test from "node:test";
import type { components } from "../src/net/schema";
import { bodyHits, isCovered, occludingStructures, StructureTracker } from "../src/world/occlusion";
import { ChunkStore } from "../src/world/ChunkStore";
import { NO_FLOOR } from "../src/world/rules";
import { labBuilding } from "./fixtures/labBuilding";

const SIZE = 32;
type Chunk = components["schemas"]["ChunkResponse"];
type Level = components["schemas"]["ChunkLevelResponse"];
const tileIndex = (x: number, y: number): number => y * SIZE + x;

function makeStore(options: { buildingAt?: number; floors?: number[]; hill?: boolean } = {}): ChunkStore {
  const ground = Array(SIZE * SIZE).fill(0) as number[];
  if (options.hill) {
    for (let y = 6; y < 10; y += 1) {
      for (let x = 6; x < 10; x += 1) ground[tileIndex(x, y)] = 12;
    }
  }
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

test("a structure behind Niko is not selected and a high-only structure keeps its lowest floor", () => {
  const store = makeStore({ buildingAt: 7, floors: [6, 12] });
  assert.deepEqual(occludingStructures(store, 16, 16, 4, Infinity), []);
  const structures = occludingStructures(store, 4, 4, 4, Infinity);
  assert.equal(structures.length, 1);
  assert.equal(structures[0]?.cutoff, 8);

  const fallback = occludingStructures(makeStore({ floors: [10, 16] }), 4.5, 4.5, 0, Infinity);
  assert.equal(fallback[0]?.cutoff, 14);
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

test("body probes see the lab slab face at the reported position", () => {
  const store = labBuilding();
  const position = { x: 25, y: 45.2, h: 2 };
  const hits = bodyHits(store, position.x, position.y, position.h, () => Infinity);
  assert.ok(hits.some((hit) => hit.x === 25 && hit.y === 46 && hit.h === 8));
  assert.equal(isCovered(store, position.x, position.y, position.h, () => Infinity), true);

  const tracker = new StructureTracker();
  const bounds = tracker.update(store, position.x, position.y, position.h, Infinity);
  assert.equal(bounds.length, 1);
  assert.equal(tracker.cutoffAt(18, 46, Infinity), 6);
  assert.equal(isCovered(store, position.x, position.y, position.h, (x, y) => tracker.cutoffAt(x, y, Infinity)), false);
});

function addFloor(store: ChunkStore, x: number, y: number, z: number, floorH: number): void {
  const previous = store.get(0, 1)!;
  const floorIndex = (y - SIZE) * SIZE + x;
  const levels = previous.levels.map((level) => {
    if (level.z !== z) return level;
    const floor_h = [...level.floor_h];
    floor_h[floorIndex] = floorH;
    return { ...level, floor_h };
  });
  store.set({ ...previous, revision: previous.revision + 1, levels });
}

test("a joined slab closes its east face and the ray continues into the neighbour", () => {
  const exposed = labBuilding();
  const position = { x: 24.890625, y: 45.059375 };
  const exposedHits = bodyHits(exposed, position.x, position.y, 2, () => Infinity);
  const exposedTileHits = exposedHits.filter((hit) => hit.x === 25 && hit.y === 46 && hit.h === 8);
  assert.ok(exposedTileHits.length > 0);

  const joined = labBuilding();
  addFloor(joined, 26, 46, 1, 8);
  const joinedHits = bodyHits(joined, position.x, position.y, 2, () => Infinity);
  const joinedTileHits = joinedHits.filter((hit) => hit.x === 25 && hit.y === 46 && hit.h === 8);
  assert.ok(joinedTileHits.length < exposedTileHits.length);
  assert.ok(joinedHits.some((hit) => hit.x === 26 && hit.y === 46 && hit.h === 8));
});

type OracleSlab = {
  x: number;
  y: number;
  floorH: number;
  eastFloor: number | undefined;
  southFloor: number | undefined;
};

function oracleSlabs(store: ChunkStore): Map<number, OracleSlab[]> {
  const byDiagonal = new Map<number, OracleSlab[]>();
  for (const chunk of store.values()) {
    for (const level of chunk.levels) {
      for (let ly = 0; ly < SIZE; ly += 1) {
        for (let lx = 0; lx < SIZE; lx += 1) {
          const floorH = level.floor_h[ly * SIZE + lx]!;
          if (floorH === NO_FLOOR) continue;
          const x = chunk.cx * SIZE + lx;
          const y = chunk.cy * SIZE + ly;
          const diagonal = x - y;
          const entries = byDiagonal.get(diagonal) ?? [];
          entries.push({
            x,
            y,
            floorH,
            eastFloor: store.levelCell(x + 1, y, level.z)?.floor_h,
            southFloor: store.levelCell(x, y + 1, level.z)?.floor_h,
          });
          byDiagonal.set(diagonal, entries);
        }
      }
    }
  }
  return byDiagonal;
}

function coveredPixels(
  slabsByDiagonal: Map<number, OracleSlab[]>,
  x: number,
  y: number,
  viewerH: number,
  cutoffAt: (x: number, y: number) => number,
): number {
  let covered = 0;
  for (let heightIndex = 2; heightIndex <= 60; heightIndex += 1) {
    const probe = heightIndex / 16;
    for (let column = -9; column <= 9; column += 1) {
      const ds = column / 32;
      const x0 = x + ds / 2;
      const y0 = y - ds / 2;
      const line = x0 - y0;
      const firstDiagonal = Math.ceil(line - 1 - 1e-9);
      const lastDiagonal = Math.floor(line + 1 + 1e-9);
      const h0 = viewerH + probe;
      let hit = false;
      for (let diagonal = firstDiagonal; diagonal <= lastDiagonal && !hit; diagonal += 1) {
        for (const slab of slabsByDiagonal.get(diagonal) ?? []) {
          const { x: tileX, y: tileY, floorH } = slab;
          if (floorH <= viewerH + 4 || floorH > cutoffAt(tileX, tileY) || tileX + 1 <= x0 || tileY + 1 <= y0) continue;

          const enterX = tileX - x0;
          const enterY = tileY - y0;
          const enterBottom = (floorH - 1 - h0) / 2;
          const top = (floorH - h0) / 2;
          const east = tileX + 1 - x0;
          const south = tileY + 1 - y0;
          const enter = Math.max(0, enterX, enterY, enterBottom);
          const leave = Math.min(top, east, south);
          if (enter >= leave) continue;
          hit = leave === top || (leave === east && slab.eastFloor !== floorH) ||
            (leave === south && slab.southFloor !== floorH);
          if (hit) covered += 1;
          if (hit) break;
        }
      }
    }
  }
  return covered;
}

test("an independent sprite oracle bounds remaining slab coverage around the lab", () => {
  const store = labBuilding();
  const slabsByDiagonal = oracleSlabs(store);
  const residual: Array<{ x: number; y: number; pixels: number }> = [];
  for (let yIndex = 0; yIndex < 180; yIndex += 1) {
    const y = 40 + yIndex / 10;
    for (let xIndex = 0; xIndex < 190; xIndex += 1) {
      const x = 12 + xIndex / 10;
      if (x >= 18 && x < 26 && y >= 46 && y < 52) continue;
      const tracker = new StructureTracker();
      tracker.update(store, x, y, 2, Infinity);
      const pixels = coveredPixels(
        slabsByDiagonal,
        x,
        y,
        2,
        (hitX, hitY) => tracker.cutoffAt(hitX, hitY, Infinity),
      );
      if (pixels > 0) residual.push({ x, y, pixels });
    }
  }
  assert.ok(residual.length <= 4 && residual.every(({ pixels }) => pixels <= 6), JSON.stringify(residual));
});

test("the feet oracle keeps the spawn's high slab cut while steps stay low", () => {
  const store = labBuilding();
  const tracker = new StructureTracker();
  tracker.update(store, 21.5, 45.5, 2, Infinity);
  assert.equal(tracker.cutoffAt(20, 46, Infinity), 6);
  const spawn = new StructureTracker();
  const slabsByDiagonal = oracleSlabs(store);
  assert.equal(coveredPixels(slabsByDiagonal, 21.5, 41.5, 2, () => Infinity), 185);
  assert.equal(spawn.update(store, 21.5, 41.5, 2, Infinity).length, 1);
  assert.equal(spawn.cutoffAt(25, 46, Infinity), 6);
  assert.equal(coveredPixels(slabsByDiagonal, 21.5, 41.5, 2,
    (x, y) => spawn.cutoffAt(x, y, Infinity)), 0);
});

test("a covering structure stays selected for one tile and releases beyond its anchor", () => {
  const store = labBuilding();
  const tracker = new StructureTracker();
  assert.equal(tracker.update(store, 24.5, 45.5, 2, Infinity).length, 1);
  const withinMargin = tracker.update(store, 25.5, 44.5, 2, Infinity);
  assert.equal(isCovered(store, 25.5, 44.5, 2, (x, y) => tracker.cutoffAt(x, y, Infinity)), false);
  assert.deepEqual(withinMargin, []);
  assert.equal(tracker.cutoffAt(20, 46, Infinity), 6);
  assert.equal(tracker.update(store, 27.5, 43.5, 2, Infinity).length, 1);
  assert.equal(tracker.cutoffAt(20, 46, Infinity), Infinity);
});

test("height changes release an unhit structure and chunk rebuilds invalidate old and new bounds", () => {
  const store = labBuilding();
  const tracker = new StructureTracker();
  tracker.update(store, 24.5, 45.5, 2, Infinity);
  assert.equal(tracker.update(store, 24.5, 45.5, 20, Infinity).length, 1);

  const second = new StructureTracker();
  second.update(store, 24.5, 45.5, 2, Infinity);
  assert.equal(second.touches(0, 1, SIZE), true);
  const oldChunk = store.get(0, 1)!;
  const changedIndex = (51 - SIZE) * SIZE + 25;
  const updated: Chunk = {
    ...oldChunk,
    revision: oldChunk.revision + 1,
    levels: oldChunk.levels.map((level) => {
      const floor = [...level.floor_h];
      const north = [...level.wall_n];
      const west = [...level.wall_w];
      floor[changedIndex] = NO_FLOOR;
      north[changedIndex] = 0;
      west[changedIndex] = 0;
      return { ...level, floor_h: floor, wall_n: north, wall_w: west };
    }),
  };
  store.set(updated);
  assert.ok(second.rebuild(store).length > 0);
});
