#!/usr/bin/env node
// Summarises a `--trace-walk PATH` CSV from the Godot client (Fix19): how evenly Niko and the view
// move per frame while D is held. Usage: node scripts/trace-walk.mjs PATH
import { readFileSync } from "node:fs";

const path = process.argv[2];
if (!path) {
  console.error("usage: node scripts/trace-walk.mjs PATH");
  process.exit(2);
}

const WALKING_SPEED = 4; // MoveOp.WalkingSpeed, m/s
const R = Math.sqrt(0.5);
const PIXELS_PER_UNIT = 32 / R; // PixelView.PixelsPerUnit
// PixelView's camera axes: right = (r, 0, -r), up = back x right with back = (a, b, a).
const A = Math.cos(Math.PI / 6) * R;
const B = Math.sin(Math.PI / 6);
const UP = (() => {
  const [bx, by, bz] = [A, B, A];
  const [rx, ry, rz] = [R, 0, -R];
  const v = [by * rz - bz * ry, bz * rx - bx * rz, bx * ry - by * rx];
  const n = Math.hypot(...v);
  return v.map((c) => c / n);
})();

const [header, ...lines] = readFileSync(path, "utf8").trim().split(/\r?\n/);
const keys = header.split(",");
const rows = lines.map((line) => Object.fromEntries(line.split(",").map((v, i) => [keys[i], Number(v)])));

// Whole screen pixels the view scrolled: the snapped camera plus the rounded remainder, as PixelView draws it.
const screen = (row) => {
  const sr = Math.round((row.cam_x * R - row.cam_z * R) * PIXELS_PER_UNIT);
  const su = Math.round((row.cam_x * UP[0] + row.cam_y * UP[1] + row.cam_z * UP[2]) * PIXELS_PER_UNIT);
  return [sr * row.scale + Math.round(row.rem_x * row.scale), su * row.scale - Math.round(row.rem_y * row.scale)];
};

const moving = rows.map((row, i) => i > 0 && Math.hypot(row.x - rows[i - 1].x, row.z - rows[i - 1].z) > 1e-6);
const first = moving.indexOf(true);
const last = moving.lastIndexOf(true);
if (first < 0) {
  console.error("trace-walk: Niko never moved");
  process.exit(1);
}
// Skip the first half second (start of the walk) and the last 0.2 s (the stop).
const t0 = rows[first].usec + 500_000;
const t1 = rows[last].usec - 200_000;
const walk = rows.map((row, i) => ({ row, prev: rows[i - 1] })).filter(({ row, prev }) => prev && row.usec >= t0 && row.usec <= t1);

const histogram = (values) => {
  const counts = new Map();
  for (const v of values) counts.set(v, (counts.get(v) ?? 0) + 1);
  return [...counts].sort((a, b) => a[0] - b[0]).map(([v, n]) => `${v}:${n}`).join(" ");
};

const ratios = walk.map(({ row, prev }) => Math.hypot(row.x - prev.x, row.z - prev.z) / (WALKING_SPEED * row.delta));
const steady = ratios.filter((r) => Math.abs(r - 1) <= 0.05).length;
const stopped = ratios.filter((r) => r < 0.05).length;
const deltas = walk.map(({ row }) => (row.delta * 1000).toFixed(1));
const scroll = walk.map(({ row, prev }) => {
  const [a, b] = [screen(prev), screen(row)];
  return Math.round(Math.hypot(b[0] - a[0], b[1] - a[1]));
});
const gaps = [];
let since = 0;
for (const { row } of walk) {
  since++;
  if (row.arrived) {
    gaps.push(since);
    since = 0;
  }
}

console.log(`frames analysed: ${walk.length}`);
console.log(`frame delta ms: ${histogram(deltas)}`);
console.log(`speed within 5%: ${steady}/${walk.length} (${((100 * steady) / walk.length).toFixed(1)}%), stopped frames: ${stopped}`);
console.log(`speed ratio min/max: ${Math.min(...ratios).toFixed(2)} / ${Math.max(...ratios).toFixed(2)}`);
console.log(`speed ratio: ${histogram(ratios.map((r) => r.toFixed(2)))}`);
console.log(`screen px per frame: ${histogram(scroll)}`);
console.log(`frames between arrivals: ${histogram(gaps.slice(1))}`);
