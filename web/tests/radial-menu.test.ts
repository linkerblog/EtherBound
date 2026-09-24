import assert from "node:assert/strict";
import test from "node:test";
import { RADIAL_BASE_RADIUS, RADIAL_CAPACITY, RADIAL_RING_STEP, firstAvailable, radialSlots, stepFocus } from "../src/ui/radialMenu";
import type { MenuEntry } from "../src/net/protocol";

function entry(op: string, available = true): MenuEntry {
  return { op, label: op.toUpperCase(), tags: [], available, reason: available ? null : "not now", subject: null, action: { op: "wait" } };
}

function entries(count: number): MenuEntry[] {
  return Array.from({ length: count }, (_, index) => entry(`op${index}`));
}

test("every entry is placed exactly once on the first ring", () => {
  const list = entries(RADIAL_CAPACITY);
  const slots = radialSlots(list);
  assert.equal(slots.length, list.length);
  assert.deepEqual(slots.map((slot) => slot.entry.op), list.map((item) => item.op));
  for (const slot of slots) {
    assert.equal(slot.ring, 0);
    assert.ok(Math.abs(slot.radius - RADIAL_BASE_RADIUS) < 1e-9);
  }
});

test("the first ring starts at the top and runs clockwise", () => {
  const slots = radialSlots(entries(4));
  assert.ok(Math.abs(slots[0]!.angle + Math.PI / 2) < 1e-9);
  assert.ok(Math.abs(slots[0]!.x) < 1e-9);
  assert.ok(slots[0]!.y < 0);
  const second = slots[1]!;
  assert.ok(second.angle > slots[0]!.angle && second.x > 0);
});

test("overflow entries go to a wider ring, with no option dropped", () => {
  const list = entries(RADIAL_CAPACITY + 3);
  const slots = radialSlots(list);
  assert.equal(slots.length, list.length);
  assert.equal(slots.filter((slot) => slot.ring === 0).length, RADIAL_CAPACITY);
  const secondRing = slots.filter((slot) => slot.ring === 1);
  assert.equal(secondRing.length, 3);
  for (const slot of secondRing) {
    assert.ok(Math.abs(slot.radius - (RADIAL_BASE_RADIUS + RADIAL_RING_STEP)) < 1e-9);
  }
});

test("focus moves in a ring and starts on the first pickable option", () => {
  assert.equal(firstAvailable([entry("a", false), entry("b"), entry("c")]), 1);
  assert.equal(firstAvailable([entry("a", false), entry("b", false)]), 0);
  assert.equal(stepFocus(0, -1, 4), 3);
  assert.equal(stepFocus(3, 1, 4), 0);
  assert.equal(stepFocus(0, 1, 0), 0);
});