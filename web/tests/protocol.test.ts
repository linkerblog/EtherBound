import assert from "node:assert/strict";
import test from "node:test";
import { interpolateTrajectory } from "../src/game/physics";
import type { PhysicsPosition } from "../src/net/protocol";
import { readActors } from "../src/net/protocol";

test("actor protocol preserves carried slots and live load", () => {
  const actors = readActors({
    niko: {
      id: "niko", kind: "player", x: 2, y: 3, z: 0, h: 0,
      carried: [{ id: 9, kind: "bottle", name: "Bottle", quantity: 3, slot: "right" }],
      load_kg: 12.4,
    },
  });
  assert.equal(actors.niko?.load_kg, 12.4);
  assert.deepEqual(actors.niko?.carried, [{ id: 9, kind: "bottle", name: "Bottle", quantity: 3, slot: "right" }]);
});

test("physics animation interpolates only the server trajectory", () => {
  const path: PhysicsPosition[] = [
    { kind: "object", id: 9, x: 3, y: 4, h: 2 },
    { kind: "object", id: 9, x: 4, y: 4, h: 2 },
    { kind: "object", id: 9, x: 4, y: 5, h: 1 },
  ];

  assert.deepEqual(interpolateTrajectory(path, 0.75), {
    kind: "object",
    id: 9,
    x: 4,
    y: 4.5,
    h: 1.5,
  });
  assert.equal(interpolateTrajectory(path, 2)?.x, 4);
  assert.equal(interpolateTrajectory([], 0.5), null);
});
