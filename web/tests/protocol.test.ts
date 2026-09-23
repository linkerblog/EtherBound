import assert from "node:assert/strict";
import test from "node:test";
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
