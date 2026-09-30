export const TILE_GAP = 4
/** Breathing room on the right and bottom edges of a grid. */
export const GRID_PADDING = 8

export function columnCount(width: number, minTileWidth: number): number {
  return Math.max(1, Math.floor(width / minTileWidth))
}

/** Square tile edge that fills a row of `columns` tiles separated by TILE_GAP. */
export function tileSize(width: number, columns: number): number {
  return Math.max(0, Math.floor((width - TILE_GAP * (columns - 1)) / columns))
}
