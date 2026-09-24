import assert from "node:assert/strict";
import test from "node:test";
import { wallEndRuns, wallJoints, type WallJoint, type WallJointLookup } from "../src/game/wallJoints";

function lookupOf(entries: Record<string, Partial<WallJoint>>): WallJointLookup {
  return (edge, x, y) => ({ shown: false, base: 0, top: 0, ...entries[`${edge}:${x},${y}`] });
}

const wall = (base: number, top: number): Partial<WallJoint> => ({ shown: true, base, top });

test("a lone north wall shows its full end face", () => {
  const joints = wallJoints("n", 0, 0, 0, lookupOf({ "n:0,0": wall(4, 10) }));
  assert.deepEqual(joints.end, { from: 4, to: 10 });
});

test("a continued run and a covering perpendicular wall hide the end face", () => {
  const continued = wallJoints("n", 0, 0, 0, lookupOf({
    "n:0,0": wall(4, 10),
    "n:1,0": wall(4, 10),
  }));
  assert.equal(continued.end, null);
  const covered = wallJoints("n", 0, 0, 0, lookupOf({
    "n:0,0": wall(4, 10),
    "w:1,-1": wall(4, 10),
  }));
  assert.equal(covered.end, null);
});

test("a doorway neighbour shows the end face and a stub exposes only its top", () => {
  const doorway = wallJoints("n", 0, 0, 0, lookupOf({
    "n:0,0": wall(4, 10),
    "n:1,0": { shown: false, base: 4, top: 4 },
  }));
  assert.deepEqual(doorway.end, { from: 4, to: 10 });
  const stub = wallJoints("n", 0, 0, 0, lookupOf({
    "n:0,0": wall(4, 10),
    "n:1,0": wall(4, 5),
  }));
  assert.deepEqual(stub.end, { from: 5, to: 10 });
});

test("a west wall shows its south end from its own neighbours", () => {
  const joints = wallJoints("w", 0, 0, 0, lookupOf({ "w:0,0": wall(4, 10) }));
  assert.deepEqual(joints.end, { from: 4, to: 10 });
  const continued = wallJoints("w", 0, 0, 0, lookupOf({
    "w:0,0": wall(4, 10),
    "w:0,1": wall(4, 10),
  }));
  assert.equal(continued.end, null);
});

test("a window end face skips the glass band", () => {
  const runs = wallEndRuns({ from: 4, to: 10 }, 4, true);
  assert.deepEqual(runs, [{ from: 4, to: 6 }, { from: 8, to: 10 }]);
  assert.deepEqual(wallEndRuns({ from: 4, to: 10 }, 4, false), [{ from: 4, to: 10 }]);
});

test("the post appears only for a tile's own north and west pair with equal tops", () => {
  const equal = wallJoints("n", 0, 0, 0, lookupOf({
    "n:0,0": wall(4, 10),
    "w:0,0": wall(4, 10),
  }));
  assert.equal(equal.post, true);
  const different = wallJoints("n", 0, 0, 0, lookupOf({
    "n:0,0": wall(4, 10),
    "w:0,0": wall(4, 9),
  }));
  assert.equal(different.post, false);
  const alone = wallJoints("n", 0, 0, 0, lookupOf({ "n:0,0": wall(4, 10) }));
  assert.equal(alone.post, false);
  const west = wallJoints("w", 0, 0, 0, lookupOf({
    "n:0,0": wall(4, 10),
    "w:0,0": wall(4, 10),
  }));
  assert.equal(west.post, false);
});
