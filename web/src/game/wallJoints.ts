export type WallEdge = "n" | "w";

export type WallJoint = { shown: boolean; base: number; top: number };

export type WallJointLookup = (edge: WallEdge, x: number, y: number, z: number) => WallJoint;

export type WallEndRange = { from: number; to: number };

export type WallJoints = { end: WallEndRange | null; post: boolean };

/**
 * The end face and corner post of one wall edge, from its neighbours' shown flags, bases and drawn
 * tops. The body grows outward, so a north wall shows its east end and a west wall its south end,
 * both hidden by whatever continues the run or stands on the same plane.
 */
export function wallJoints(
  edge: WallEdge,
  x: number,
  y: number,
  z: number,
  lookup: WallJointLookup,
): WallJoints {
  const self = lookup(edge, x, y, z);
  if (!self.shown) return { end: null, post: false };
  const along = edge === "n" ? lookup("n", x + 1, y, z) : lookup("w", x, y + 1, z);
  const across = edge === "n" ? lookup("w", x + 1, y - 1, z) : lookup("n", x - 1, y + 1, z);
  const coverTop = Math.max(along.shown ? along.top : along.base, across.shown ? across.top : across.base);
  let end: WallEndRange | null = null;
  if (!across.shown) {
    const from = Math.max(self.base, coverTop);
    if (from < self.top) end = { from, to: self.top };
  }
  const west = edge === "n" ? lookup("w", x, y, z) : self;
  const post = edge === "n" && self.shown && west.shown && west.top === self.top;
  return { end, post };
}

/** The vertical runs an end face draws: solid above and below a window's glass band. */
export function wallEndRuns(end: WallEndRange, base: number, window: boolean): WallEndRange[] {
  if (!window) return [end];
  const runs: WallEndRange[] = [];
  const glassFrom = base + 2;
  const glassTo = base + 4;
  if (end.from < glassFrom) runs.push({ from: end.from, to: Math.min(glassFrom, end.to) });
  if (end.to > glassTo) runs.push({ from: Math.max(glassTo, end.from), to: end.to });
  return runs;
}
