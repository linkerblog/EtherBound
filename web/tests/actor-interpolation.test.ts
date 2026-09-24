import assert from "node:assert/strict";
import test from "node:test";
import {
  ActorInterpolator,
  MAX_TICK_MS,
  MIN_TICK_MS,
  clampInterval,
  interpolationFactor,
  mixPosition,
} from "../src/game/extras";

test("interpolationFactor clamps at both ends", () => {
  assert.equal(interpolationFactor(0, 100), 0);
  assert.equal(interpolationFactor(50, 100), 0.5);
  assert.equal(interpolationFactor(150, 100), 1);
  assert.equal(interpolationFactor(-10, 100), 0);
  assert.equal(interpolationFactor(10, 0), 1);
});

test("clampInterval holds the believable tick window", () => {
  assert.equal(clampInterval(1), MIN_TICK_MS);
  assert.equal(clampInterval(200), 200);
  assert.equal(clampInterval(10_000), MAX_TICK_MS);
  assert.equal(clampInterval(Number.NaN), MAX_TICK_MS);
});

test("mixPosition is a linear mix on the ground plane", () => {
  const previous = { x: 0, y: 0, z: 0, h: 2 };
  const latest = { x: 10, y: -4, z: 1, h: 3 };
  assert.deepEqual(mixPosition(previous, latest, 0), { x: 0, y: 0, z: 1, h: 3 });
  assert.deepEqual(mixPosition(previous, latest, 0.5), { x: 5, y: -2, z: 1, h: 3 });
  assert.deepEqual(mixPosition(previous, latest, 1), { x: 10, y: -4, z: 1, h: 3 });
});

test("a first sighting snaps and later ticks mix", () => {
  const interpolator = new ActorInterpolator();
  assert.equal(interpolator.sample(0), null);

  interpolator.update({ x: 4, y: 4, z: 0, h: 1 }, 1_000);
  assert.deepEqual(interpolator.sample(1_000), { x: 4, y: 4, z: 0, h: 1 });

  interpolator.update({ x: 8, y: 4, z: 0, h: 1 }, 1_200);
  assert.deepEqual(interpolator.sample(1_200), { x: 4, y: 4, z: 0, h: 1 });
  assert.deepEqual(interpolator.sample(1_300), { x: 6, y: 4, z: 0, h: 1 });
  assert.deepEqual(interpolator.sample(1_400), { x: 8, y: 4, z: 0, h: 1 });
  assert.deepEqual(interpolator.sample(9_999), { x: 8, y: 4, z: 0, h: 1 });
});

test("a repeated spot does not send the body backwards", () => {
  const interpolator = new ActorInterpolator();
  interpolator.update({ x: 0, y: 0, z: 0, h: 0 }, 1_000);
  interpolator.update({ x: 10, y: 0, z: 0, h: 0 }, 1_100);
  interpolator.update({ x: 10, y: 0, z: 0, h: 0 }, 1_200);

  assert.deepEqual(interpolator.sample(1_300), { x: 10, y: 0, z: 0, h: 0 });
});

test("reset forgets the trail", () => {
  const interpolator = new ActorInterpolator();
  interpolator.update({ x: 1, y: 1, z: 0, h: 0 }, 0);
  interpolator.reset();
  assert.equal(interpolator.sample(10), null);
});