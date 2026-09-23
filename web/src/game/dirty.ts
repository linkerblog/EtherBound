export type DirtyChunk = {
  key: string;
  cx: number;
  cy: number;
  hMin: number;
  hMax: number;
  hasLevels: boolean;
};

export type TilePosition = { x: number; y: number };
export type ChunkBounds = { minX: number; minY: number; maxX: number; maxY: number };

export function changedChunkKeys(chunks: DirtyChunk[], key: string): string[] {
  const changed = chunks.find((chunk) => chunk.key === key);
  if (!changed) return [];
  return chunks.filter((chunk) => Math.abs(chunk.cx - changed.cx) + Math.abs(chunk.cy - changed.cy) <= 1)
    .map((chunk) => chunk.key);
}

export function materialChunkKeys(chunks: DirtyChunk[]): string[] {
  return chunks.map((chunk) => chunk.key);
}

export function viewerHeightChunkKeys(chunks: DirtyChunk[], from: number, to: number): string[] {
  if (from === to) return [];
  const lower = Math.min(from, to);
  const upper = Math.max(from, to);
  return chunks.filter((chunk) => chunk.hMin < upper && chunk.hMax > lower).map((chunk) => chunk.key);
}

export function cutoffChunkKeys(chunks: DirtyChunk[]): string[] {
  return chunks.filter((chunk) => chunk.hasLevels).map((chunk) => chunk.key);
}

export function tileChunkKeys(
  chunks: DirtyChunk[],
  chunkSize: number,
  from: TilePosition,
  to: TilePosition,
): string[] {
  return chunks.filter((chunk) => chunk.hasLevels && ([from, to].some((tile) => {
    const minX = chunk.cx * chunkSize;
    const minY = chunk.cy * chunkSize;
    return minX <= tile.x + 8 && minX + chunkSize > tile.x - 8 &&
      minY <= tile.y + 8 && minY + chunkSize > tile.y - 8;
  }))).map((chunk) => chunk.key);
}

export function structureChunkKeys(
  chunks: DirtyChunk[],
  chunkSize: number,
  bounds: ChunkBounds[],
): string[] {
  return chunks.filter((chunk) => {
    const minX = chunk.cx * chunkSize;
    const minY = chunk.cy * chunkSize;
    const maxX = minX + chunkSize;
    const maxY = minY + chunkSize;
    return bounds.some((area) => minX < area.maxX && maxX > area.minX && minY < area.maxY && maxY > area.minY);
  }).map((chunk) => chunk.key);
}
