import type { Direction } from "../net/protocol";

export const TILE_W = 64;
export const TILE_H = 32;
export const H_PX = 16;

export const BASE_DEPTH = -1_000_000;

export function toScreen(x: number, y: number, h: number): { sx: number; sy: number } {
  return {
    sx: (x - y) * TILE_W / 2,
    sy: (x + y) * TILE_H / 2 - h * H_PX,
  };
}

export function screenToRay(sx: number, sy: number): { s: number; t: number } {
  return { s: sx / (TILE_W / 2), t: sy / (TILE_H / 2) };
}

export function keysToWorld(kx: number, ky: number): Direction {
  const x = kx + ky;
  const y = ky - kx;
  const length = Math.hypot(x, y);
  return length === 0 ? { x: 0, y: 0 } : { x: x / length, y: y / length };
}

export function baseDepth(cx: number, cy: number): number {
  return BASE_DEPTH + cx + cy;
}

export function rowDepth(row: number): number {
  return 2 * row;
}

export function nikoDepth(x: number, y: number): number {
  return 2 * (Math.floor(x) + Math.floor(y)) + 1;
}
