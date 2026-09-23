import assert from "node:assert/strict";
import test from "node:test";
import { changedChunkKeys, cutoffChunkKeys, materialChunkKeys, structureChunkKeys, tileChunkKeys, viewerHeightChunkKeys, type DirtyChunk } from "../src/game/dirty";

const chunks: DirtyChunk[] = [
  { key: "0,0", cx: 0, cy: 0, hMin: -4, hMax: 12, hasLevels: true },
  { key: "1,0", cx: 1, cy: 0, hMin: 12, hMax: 24, hasLevels: true },
  { key: "-1,0", cx: -1, cy: 0, hMin: -8, hMax: 0, hasLevels: false },
  { key: "0,1", cx: 0, cy: 1, hMin: 0, hMax: 18, hasLevels: false },
  { key: "2,2", cx: 2, cy: 2, hMin: 0, hMax: 8, hasLevels: true },
];

test("each redraw cause marks only its required chunks", () => {
  assert.deepEqual(new Set(changedChunkKeys(chunks, "0,0")), new Set(["0,0", "1,0", "-1,0", "0,1"]));
  assert.deepEqual(new Set(materialChunkKeys(chunks)), new Set(chunks.map((chunk) => chunk.key)));
  assert.deepEqual(new Set(viewerHeightChunkKeys(chunks, 0, 12)), new Set(["0,0", "0,1", "2,2"]));
  assert.deepEqual(new Set(cutoffChunkKeys(chunks)), new Set(["0,0", "1,0", "2,2"]));
  assert.deepEqual(tileChunkKeys(chunks, 32, { x: 1000, y: 1000 }, { x: 1001, y: 1000 }), []);
  assert.deepEqual(tileChunkKeys(chunks, 32, { x: 31, y: 1 }, { x: 31, y: 1 }), ["0,0", "1,0"]);
  assert.deepEqual(structureChunkKeys(chunks, 32, [
    { minX: 31, minY: 2, maxX: 34, maxY: 5 },
    { minX: 64, minY: 64, maxX: 70, maxY: 70 },
  ]), ["0,0", "1,0", "2,2"]);
});
