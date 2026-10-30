import type { ImageListItem } from '../api/types'

/** A virtualized grid row: a group's header, or a run of tiles (items[start, end)) belonging to a group. */
export type GridRow =
  | { kind: 'header'; group: string }
  | { kind: 'tiles'; start: number; end: number; group: string | null }

/** A photo's calendar day ("YYYY-MM-DD"): the UTC date of sortDate, which is the wall-clock day. Never goes through Date. */
export const dayOf = (item: ImageListItem): string => item.sortDate.slice(0, 10)

/**
 * Lays the items out in rows of `columns` tiles. With `groupKey`, a header row precedes each run of
 * equal keys and tile rows never span two groups; without it the rows are plain chunks.
 */
export function buildRows(
  items: ImageListItem[],
  columns: number,
  groupKey?: (item: ImageListItem) => string,
): GridRow[] {
  const rows: GridRow[] = []
  const perRow = Math.max(1, columns)
  let start = 0
  while (start < items.length) {
    const first = items[start]!
    const group = groupKey?.(first) ?? null
    let groupEnd = items.length
    if (groupKey) {
      groupEnd = start + 1
      while (groupEnd < items.length && groupKey(items[groupEnd]!) === group) groupEnd++
      rows.push({ kind: 'header', group: group! })
    }
    for (let rowStart = start; rowStart < groupEnd; rowStart += perRow) {
      rows.push({ kind: 'tiles', start: rowStart, end: Math.min(rowStart + perRow, groupEnd), group })
    }
    start = groupEnd
  }
  return rows
}
