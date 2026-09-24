import type { MenuEntry } from "../net/protocol";

/** Options placed in one ring before a wider one starts. */
export const RADIAL_CAPACITY = 8;
/** Radius of the first ring, in CSS pixels from the hub. */
export const RADIAL_BASE_RADIUS = 104;
/** How much each further ring grows. */
export const RADIAL_RING_STEP = 60;

export type RadialSlot = {
  entry: MenuEntry;
  /** Position of the entry in the server list; also the focus index. */
  index: number;
  ring: number;
  angle: number;
  radius: number;
  /** Offset from the hub, in CSS pixels. */
  x: number;
  y: number;
};

/**
 * Places every entry the server returned on concentric rings around the hub, so the menu never
 * hides an option. The first ring starts at the top and runs clockwise; alternate rings are
 * offset by half a step so their labels do not line up.
 */
export function radialSlots(entries: MenuEntry[], capacity = RADIAL_CAPACITY): RadialSlot[] {
  const slots: RadialSlot[] = [];
  for (let start = 0, ring = 0; start < entries.length; start += capacity, ring += 1) {
    const count = Math.min(capacity, entries.length - start);
    const radius = RADIAL_BASE_RADIUS + ring * RADIAL_RING_STEP;
    const offset = ring % 2 === 1 ? Math.PI / count : 0;
    for (let position = 0; position < count; position += 1) {
      const index = start + position;
      const angle = -Math.PI / 2 + offset + (2 * Math.PI * position) / count;
      slots.push({
        entry: entries[index]!,
        index,
        ring,
        angle,
        radius,
        x: Math.cos(angle) * radius,
        y: Math.sin(angle) * radius,
      });
    }
  }
  return slots;
}

/** Index of the first entry that can be picked, or 0 when none can. */
export function firstAvailable(entries: MenuEntry[]): number {
  const index = entries.findIndex((entry) => entry.available);
  return index === -1 ? 0 : index;
}

/** Moves focus around the ring, wrapping, so every option stays reachable by key. */
export function stepFocus(current: number, step: number, length: number): number {
  if (length === 0) return 0;
  return (current + step + length) % length;
}