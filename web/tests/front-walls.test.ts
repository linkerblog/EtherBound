import assert from "node:assert/strict";
import test from "node:test";
import type { components } from "../src/net/schema";
import { CUT_DEPTH, CUT_WIDTH, isFrontWall, MAX_WALL_H, wallInTheWay } from "../src/world/cutaway";
import { ChunkStore } from "../src/world/ChunkStore";
import { EDGE_N_DOORWAY, EDGE_W_DOORWAY, NO_FLOOR } from "../src/world/rules";
import { labBuilding } from "./fixtures/labBuilding";

type Chunk = components["schemas"]["ChunkResponse"];
type Level = components["schemas"]["ChunkLevelResponse"];
type Edge = "n" | "w";
type Wall = { edge: Edge; x: number; y: number; base: number };
type WallSpec = { edge: Edge; x: number; y: number; doorway?: boolean };
const SIZE = 32;
const index = (x: number, y: number): number => y * SIZE + x;

function makeStore(walls: WallSpec[]): ChunkStore {
  const level: Level = {
    z: 0,
    floor_h: Array(SIZE * SIZE).fill(NO_FLOOR) as number[],
    floor_mat: Array(SIZE * SIZE).fill(1) as number[],
    wall_n: Array(SIZE * SIZE).fill(0) as number[],
    wall_w: Array(SIZE * SIZE).fill(0) as number[],
    edge_flags: Array(SIZE * SIZE).fill(0) as number[],
    flags: Array(SIZE * SIZE).fill(0) as number[],
  };
  for (const wall of walls) {
    (wall.edge === "n" ? level.wall_n : level.wall_w)[index(wall.x, wall.y)] = 1;
    if (wall.doorway) level.edge_flags[index(wall.x, wall.y)] |= wall.edge === "n" ? EDGE_N_DOORWAY : EDGE_W_DOORWAY;
  }
  const chunk: Chunk = {
    cx: 0,
    cy: 0,
    revision: 1,
    ground_h: Array(SIZE * SIZE).fill(0) as number[],
    surface_mat: Array(SIZE * SIZE).fill(1) as number[],
    levels: [level],
  };
  const store = new ChunkStore();
  store.set(chunk);
  return store;
}

function buildingWalls(store: ChunkStore): Wall[] {
  const walls: Wall[] = [];
  for (let y = 46; y <= 52; y += 1) {
    for (let x = 18; x <= 26; x += 1) {
      for (const level of store.levelsAt(x, y)) {
        const cell = store.levelCell(x, y, level.z)!;
        const base = store.wallBaseH(x, y, level.z, cell.floor_h);
        if (cell.wall_n !== 0 && (cell.edge_flags & EDGE_N_DOORWAY) === 0) walls.push({ edge: "n", x, y, base });
        if (cell.wall_w !== 0 && (cell.edge_flags & EDGE_W_DOORWAY) === 0) walls.push({ edge: "w", x, y, base });
      }
    }
  }
  return walls;
}

function oldFrontWall(wall: Wall, nikoTile: { x: number; y: number }, viewerH: number): boolean {
  const { edge, x, y, base } = wall;
  const { x: nx, y: ny } = nikoTile;
  const mx = edge === "n" ? x + 0.5 : x;
  const my = edge === "n" ? y : y + 0.5;
  return (edge === "n" ? y > ny : x > nx) &&
    (mx + my) - (nx + ny + 1) > 0 && (mx + my) - (nx + ny + 1) <= CUT_DEPTH &&
    Math.abs((mx - my) - (nx - ny)) <= CUT_WIDTH &&
    base < viewerH + 4 && base + MAX_WALL_H > viewerH;
}

function front(store: ChunkStore, wall: Wall, nikoTile: { x: number; y: number }, viewerH: number): boolean {
  return isFrontWall(store, wall.edge, wall.x, wall.y, wall.base, nikoTile, viewerH);
}

test("the reported south-wall notch is blocked by the east wall", () => {
  const store = labBuilding();
  const niko = { x: 26, y: 50 };
  const southWall = { edge: "n", x: 25, y: 52, base: 2 } as const;
  assert.equal(oldFrontWall(southWall, niko, 2), true);
  assert.equal(isFrontWall(store, "n", southWall.x, southWall.y, southWall.base, niko, 2), false);
});

test("inside the lab room, the old wall-stub window is unchanged", () => {
  const store = labBuilding();
  const walls = buildingWalls(store);
  for (let ny = 46; ny < 52; ny += 1) {
    for (let nx = 18; nx < 26; nx += 1) {
      const niko = { x: nx, y: ny };
      for (const wall of walls) {
        assert.equal(front(store, wall, niko, 2), oldFrontWall(wall, niko, 2),
          `wall ${wall.edge} ${wall.x},${wall.y} from ${nx},${ny}`);
      }
    }
  }
});

test("outside the north wall, only unobstructed north-wall sections are stubbed", () => {
  const store = labBuilding();
  const niko = { x: 21, y: 44 };
  const candidates = buildingWalls(store).filter((wall) => oldFrontWall(wall, niko, 2));
  assert.ok(candidates.some((wall) => wall.edge === "n"));
  for (const wall of candidates) {
    assert.equal(front(store, wall, niko, 2), wall.edge === "n" && wall.y === 46,
      `wall ${wall.edge} ${wall.x},${wall.y}`);
  }
});

test("outside the west doorway, the wall blocks south-wall stubs beyond it", () => {
  const store = labBuilding();
  const niko = { x: 16, y: 48 };
  const candidates = buildingWalls(store).filter((wall) => oldFrontWall(wall, niko, 2));
  assert.ok(candidates.some((wall) => wall.edge === "w"));
  for (const wall of candidates) {
    assert.equal(front(store, wall, niko, 2), wall.edge === "w",
      `wall ${wall.edge} ${wall.x},${wall.y}`);
  }
  assert.equal(isFrontWall(store, "n", 19, 52, 2, niko, 2), false);
});

test("a doorway leaves its line open, while a corner blocks on either crossed edge", () => {
  const doorway = makeStore([
    { edge: "w", x: 2, y: 2, doorway: true },
    { edge: "w", x: 4, y: 2 },
  ]);
  const niko = { x: 1, y: 2 };
  assert.equal(isFrontWall(doorway, "w", 4, 2, 0, niko, 0), true);

  const closed = makeStore([{ edge: "w", x: 2, y: 2 }, { edge: "w", x: 4, y: 2 }]);
  assert.equal(isFrontWall(closed, "w", 4, 2, 0, niko, 0), false);

  for (const edge of ["n", "w"] as const) {
    assert.equal(wallInTheWay(makeStore([{ edge, x: 2, y: 2 }]), { x: 0.5, y: 0.5 }, { x: 3.5, y: 3.5 }, 0), true);
  }
});
