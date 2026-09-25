import assert from "node:assert/strict";
import test from "node:test";
import {
  HUB_DIGIT,
  OCTANT_DIGITS,
  RADIAL_BASE_RADIUS,
  RADIAL_CAPACITY,
  RADIAL_HUB_W,
  RADIAL_RING_STEP,
  RADIAL_SLOT_H,
  RADIAL_SLOT_W,
  digitFocus,
  firstAvailable,
  groupByVerb,
  octantOf,
  radialSlots,
  stepFocus,
  targetName,
  targetSlots,
  type TargetSlot,
} from "../src/ui/radialMenu";
import { toScreen } from "../src/game/iso";
import type { MenuEntry } from "../src/net/protocol";

function entry(op: string, available = true, dx = 0, dy = 0, tags: string[] = []): MenuEntry {
  return {
    op,
    label: op.toUpperCase(),
    tags,
    available,
    reason: available ? null : "not now",
    subject: null,
    action: { op: "wait" },
    tile_dx: dx,
    tile_dy: dy,
  };
}

// Neighbour offsets in octant order: up, then clockwise on screen.
const NEIGHBOURS: [number, number][] = [[-1, -1], [0, -1], [1, -1], [1, 0], [1, 1], [0, 1], [-1, 1], [-1, 0]];

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

test("verbs group in first-appearance order and lose no entry", () => {
  const list = [
    entry("inspect", false, 0, 0, ["ether"]),
    entry("dig", false, 1, 0),
    entry("inspect", true, 1, 0, ["illegal"]),
    entry("wait"),
    entry("dig", false, 0, 1),
  ];
  const groups = groupByVerb(list);
  assert.deepEqual(groups.map((group) => group.op), ["inspect", "dig", "wait"]);
  assert.equal(groups.reduce((total, group) => total + group.entries.length, 0), list.length);
  assert.deepEqual(groups[0]!.entries, [list[0], list[2]]);
  assert.equal(groups[0]!.available, true);
  assert.equal(groups[1]!.available, false);
  assert.deepEqual([...groups[0]!.tags].sort(), ["ether", "illegal"]);
});

test("octants follow the screen direction of each neighbour", () => {
  const names = ["up", "up-right", "right", "down-right", "down", "down-left", "left", "up-left"];
  NEIGHBOURS.forEach(([dx, dy], expected) => {
    assert.equal(octantOf(dx, dy), expected, names[expected]);
    const { sx, sy } = toScreen(dx, dy, 0);
    // Octant 0 is straight up and each one turns 45 degrees clockwise.
    const octant = Math.round((Math.atan2(sy, sx) + Math.PI / 2) / (Math.PI / 4) + 8) % 8;
    assert.equal(octant, expected, names[expected]);
  });
  assert.equal(octantOf(0, 0), null);
});

/** The radial of the 25/09/2026 shot: Niko by a chest, 28 entries over 12 ops. */
function shotEntries(): MenuEntry[] {
  const chest = (op: string): MenuEntry => ({ ...entry(op, true, 1, -1), subject: "Chest" });
  return [
    chest("hit"),
    ...NEIGHBOURS.map(([dx, dy]) => entry("dig", dx !== 0, dx, dy)),
    chest("break"),
    chest("drag"),
    entry("inspect"),
    ...NEIGHBOURS.map(([dx, dy]) => entry("inspect", true, dx, dy)),
    chest("inspect"),
    chest("climb"),
    { ...entry("wait"), action: { op: "wait", target: { kind: "self" } } },
    chest("close"),
    chest("open"),
    chest("take"),
    chest("pull"),
    chest("push"),
  ];
}

type Rect = { left: number; top: number; right: number; bottom: number };

function slotRect(slot: TargetSlot): Rect {
  return {
    left: slot.x - RADIAL_SLOT_W / 2,
    right: slot.x + RADIAL_SLOT_W / 2,
    top: slot.y - RADIAL_SLOT_H / 2,
    bottom: slot.y + RADIAL_SLOT_H / 2,
  };
}

function overlaps(a: Rect, b: Rect): boolean {
  return a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
}

/** Hub height as the CSS draws it: header and detail lines plus one row per own-tile entry. */
function hubHalfHeight(rows: number): number {
  return (52 + rows * 24) / 2;
}

function assertNoOverlap(slots: TargetSlot[], hubHalf: number): void {
  const hub = { left: -RADIAL_HUB_W / 2, right: RADIAL_HUB_W / 2, top: -hubHalf, bottom: hubHalf };
  const placed = slots.filter((slot) => slot.octant !== null).map(slotRect);
  placed.forEach((rect, index) => {
    assert.ok(!overlaps(rect, hub), `slot ${index} overlaps the hub`);
    placed.slice(index + 1).forEach((other, offset) => {
      assert.ok(!overlaps(rect, other), `slots ${index} and ${index + offset + 1} overlap`);
    });
  });
}

test("the shot fixture fits on two rings of verbs, and every target once, with no overlap", () => {
  const list = shotEntries();
  assert.equal(list.length, 28);
  const groups = groupByVerb(list);
  assert.equal(groups.length, 12);
  assert.equal(Math.max(...radialSlots(groups).map((slot) => slot.ring)), 1);
  assert.equal(groups.filter((group) => group.entries.length === 1).length, 10);
  for (const group of groups) {
    const rows = group.entries.filter((item) => item.tile_dx === 0 && item.tile_dy === 0).length;
    const half = hubHalfHeight(rows);
    const slots = targetSlots(group.entries, half);
    assert.equal(slots.length, group.entries.length, group.op);
    assert.deepEqual(new Set(slots.map((slot) => slot.entry)), new Set(group.entries), group.op);
    assertNoOverlap(slots, half);
  }
});

test("long columns still keep clear of each other and of the hub", () => {
  const list = [
    ...Array.from({ length: 3 }, () => entry("take")),
    ...NEIGHBOURS.flatMap(([dx, dy], octant) => Array.from({ length: 1 + (octant % 4) * 2 }, () => entry("take", true, dx, dy))),
  ];
  const half = hubHalfHeight(3);
  const slots = targetSlots(list, half);
  assert.equal(slots.length, list.length);
  assertNoOverlap(slots, half);
});

test("level-2 slots run hub rows first, then octants clockwise from up, growing away from the hub", () => {
  const list = [entry("take", true, 1, 1), entry("take", true, -1, -1), entry("take"), entry("take", true, 0, -1), entry("take", true, -1, -1)];
  const slots = targetSlots(list, 30);
  assert.deepEqual(slots.map((slot) => slot.octant), [null, 0, 0, 1, 4]);
  assert.ok(slots[1]!.y < 0 && Math.abs(slots[1]!.x) < 1e-9);
  assert.ok(slots[2]!.y < slots[1]!.y, "an upper column grows up");
  assert.ok(slots[3]!.x > 0 && slots[3]!.y < 0);
  assert.ok(slots[4]!.y > 0 && Math.abs(slots[4]!.x) < 1e-9);
});

test("numpad digits reach every column and repeat down it", () => {
  const list = [entry("take"), ...NEIGHBOURS.flatMap(([dx, dy]) => [entry("take", true, dx, dy), entry("take", true, dx, dy)])];
  const slots = targetSlots(list, 30);
  OCTANT_DIGITS.forEach((digit, octant) => {
    const first = digitFocus(slots, digit, 0);
    assert.notEqual(first, null);
    assert.equal(slots[first!]!.octant, octant);
    assert.equal(slots[first!]!.row, 0);
    const second = digitFocus(slots, digit, first!);
    assert.equal(slots[second!]!.octant, octant);
    assert.equal(slots[second!]!.row, 1);
    assert.equal(digitFocus(slots, digit, second!), first, "the column wraps");
  });
  assert.equal(slots[digitFocus(slots, HUB_DIGIT, 3)!]!.octant, null);
  assert.equal(digitFocus(targetSlots([entry("take", true, 1, 0)], 30), 8, 0), null);
});

test("a target is named by its subject, else by the surface of its tile", () => {
  const places = [{ dx: 0, dy: 0, h: 2, label: "Asphalt" }, { dx: 1, dy: 0, h: 2, label: "Grass" }];
  assert.equal(targetName({ ...entry("take", true, 1, 0), subject: "Apple" }, places), "Apple");
  assert.equal(targetName(entry("dig", true, 1, 0), places), "Grass");
  assert.equal(targetName({ ...entry("wait"), action: { op: "wait", target: { kind: "self" } } }, places), null);
});
