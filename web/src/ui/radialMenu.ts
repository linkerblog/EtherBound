import type { MenuEntry, MenuPlace } from "../net/protocol";

/** Options placed in one ring before a wider one starts. */
export const RADIAL_CAPACITY = 8;
/** Radius of the first ring, in CSS pixels from the hub. */
export const RADIAL_BASE_RADIUS = 104;
/** How much each further ring grows. */
export const RADIAL_RING_STEP = 60;
/** Size of a level-2 target slot; fixed so columns can be laid out without measuring. */
export const RADIAL_SLOT_W = 132;
export const RADIAL_SLOT_H = 34;
/** Space between two slots of a column, and between a column and its neighbours. */
export const RADIAL_COLUMN_GAP = 4;
/** The hub's CSS width. */
export const RADIAL_HUB_W = 172;

export type RadialSlot<T = MenuEntry> = {
  entry: T;
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
export function radialSlots<T>(entries: T[], capacity = RADIAL_CAPACITY): RadialSlot<T>[] {
  const slots: RadialSlot<T>[] = [];
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
export function firstAvailable(entries: { available: boolean }[]): number {
  const index = entries.findIndex((entry) => entry.available);
  return index === -1 ? 0 : index;
}

/** Moves focus around the ring, wrapping, so every option stays reachable by key. */
export function stepFocus(current: number, step: number, length: number): number {
  if (length === 0) return 0;
  return (current + step + length) % length;
}

/** Every entry of one op, in server order: one level-1 slot of the radial. */
export type VerbGroup = {
  op: string;
  label: string;
  entries: MenuEntry[];
  /** Dimmed only when no entry under it can be picked. */
  available: boolean;
  tags: string[];
};

/** Groups entries by op in first-appearance order, which is catalog order, so a verb keeps its place. */
export function groupByVerb(entries: MenuEntry[]): VerbGroup[] {
  const groups = new Map<string, VerbGroup>();
  for (const entry of entries) {
    let group = groups.get(entry.op);
    if (!group) {
      group = { op: entry.op, label: entry.label, entries: [], available: false, tags: [] };
      groups.set(entry.op, group);
    }
    group.entries.push(entry);
    group.available ||= entry.available;
    for (const tag of entry.tags) if (!group.tags.includes(tag)) group.tags.push(tag);
  }
  return [...groups.values()];
}

// Screen direction of each neighbour under `toScreen`: 0 = up, then clockwise.
const OCTANTS: Record<string, number> = {
  "-1,-1": 0,
  "0,-1": 1,
  "1,-1": 2,
  "1,0": 3,
  "1,1": 4,
  "0,1": 5,
  "-1,1": 6,
  "-1,0": 7,
};

/** Screen octant (0 = up, clockwise) of a neighbour offset, or null for Niko's own tile. */
export function octantOf(dx: number, dy: number): number | null {
  return OCTANTS[`${dx},${dy}`] ?? null;
}

/** Numpad digit of each octant, so the keypad is the 3x3 around Niko on screen; 5 is the hub. */
export const OCTANT_DIGITS = [8, 9, 6, 3, 2, 1, 4, 7] as const;
export const HUB_DIGIT = 5;

export type TargetSlot = {
  entry: MenuEntry;
  /** Null for a row inside the hub (Niko's own tile). */
  octant: number | null;
  /** Position inside its column or among the hub rows, 0 nearest the hub. */
  row: number;
  /** Centre offset from the hub, in CSS pixels; 0, 0 for hub rows. */
  x: number;
  y: number;
  digit: number;
};

/**
 * Lays out the entries of one verb by where they act: own-tile entries become hub rows, the rest
 * a column in their tile's screen direction. Order is hub rows, then octants clockwise from up,
 * which is also the focus order.
 */
export function targetSlots(entries: MenuEntry[], hubHalfHeight: number): TargetSlot[] {
  const columns: MenuEntry[][] = Array.from({ length: 8 }, () => []);
  const hub: MenuEntry[] = [];
  for (const entry of entries) {
    const octant = octantOf(entry.tile_dx, entry.tile_dy);
    if (octant === null) hub.push(entry);
    else columns[octant]!.push(entry);
  }
  const step = RADIAL_SLOT_H + RADIAL_COLUMN_GAP;
  const radius = Math.max(RADIAL_BASE_RADIUS, hubHalfHeight + 56);
  const lane = Math.max(RADIAL_SLOT_W + RADIAL_COLUMN_GAP, RADIAL_HUB_W / 2 + RADIAL_COLUMN_GAP + RADIAL_SLOT_W / 2);
  const halfHeight = (count: number) => (count === 0 ? 0 : (count * step - RADIAL_COLUMN_GAP) / 2);
  // A diagonal clears the left/right column of its lane, which is centred on the hub.
  const rightClear = Math.max(radius / 2, halfHeight(columns[2]!.length) + RADIAL_COLUMN_GAP + RADIAL_SLOT_H / 2);
  const leftClear = Math.max(radius / 2, halfHeight(columns[6]!.length) + RADIAL_COLUMN_GAP + RADIAL_SLOT_H / 2);
  // First slot centre of each column and which way the column grows (-1 up, 1 down, 0 centred).
  const anchors: [number, number, number][] = [
    [0, -radius, -1],
    [lane, -rightClear, -1],
    [lane, 0, 0],
    [lane, rightClear, 1],
    [0, radius, 1],
    [-lane, leftClear, 1],
    [-lane, 0, 0],
    [-lane, -leftClear, -1],
  ];
  const slots: TargetSlot[] = hub.map((entry, row) => ({ entry, octant: null, row, x: 0, y: 0, digit: HUB_DIGIT }));
  columns.forEach((column, octant) => {
    const [x, y, grow] = anchors[octant]!;
    column.forEach((entry, row) => {
      const offset = grow === 0 ? (row - (column.length - 1) / 2) * step : grow * row * step;
      slots.push({ entry, octant, row, x, y: y + offset, digit: OCTANT_DIGITS[octant]! });
    });
  });
  return slots;
}

/**
 * Slot a numpad digit focuses: the first of its column, or the next one down the column when the
 * current focus is already in it. Null when that direction has nothing.
 */
export function digitFocus(slots: TargetSlot[], digit: number, current: number): number | null {
  const column = slots.flatMap((slot, index) => (slot.digit === digit ? [index] : []));
  if (column.length === 0) return null;
  const at = column.indexOf(current);
  return column[at === -1 ? 0 : (at + 1) % column.length]!;
}

/** What a level-2 slot names: the entry's subject, or the surface of the tile it acts on. */
export function targetName(entry: MenuEntry, places: MenuPlace[]): string | null {
  if (entry.subject) return entry.subject;
  if ("target" in entry.action && entry.action.target?.kind === "self") return null;
  const dx = entry.tile_dx;
  const dy = entry.tile_dy;
  return places.find((place) => place.dx === dx && place.dy === dy)?.label ?? null;
}
