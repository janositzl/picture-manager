import { LinearProgress } from '@mui/material'
import { useVirtualizer } from '@tanstack/react-virtual'
import {
  Fragment,
  useCallback,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
  type ReactNode,
  type Ref,
} from 'react'
import type { ImageListItem } from '../api/types'
import { columnCount, GRID_PADDING, TILE_GAP, tileSize } from './columns'
import { buildRows } from './rows'
import { TILE_MIN_WIDTH, useTileSize } from './tileSize'

/** What a parent can ask of a grouped grid. */
export type PhotoGridHandle = {
  /** Scrolls the group's header to the top; false when the group isn't loaded (yet). */
  scrollToGroup: (group: string) => boolean
}

type Props = {
  items: ImageListItem[]
  hasNextPage: boolean
  isFetchingNextPage: boolean
  fetchNextPage: () => void
  renderTile: (item: ImageListItem, size: number) => ReactNode
  /** Groups the items into sections (items must already be ordered so each group is contiguous). */
  groupKey?: (item: ImageListItem) => string
  renderGroupHeader?: (group: string) => ReactNode
  /** The group at the top of the view; called when it changes while scrolling. */
  onTopGroupChange?: (group: string) => void
  ref?: Ref<PhotoGridHandle>
}

/** Rows within this many of the end of the loaded items trigger the next page. */
const PREFETCH_ROWS = 2
const GROUP_HEADER_HEIGHT = 44

export function PhotoGrid({
  items,
  hasNextPage,
  isFetchingNextPage,
  fetchNextPage,
  renderTile,
  groupKey,
  renderGroupHeader,
  onTopGroupChange,
  ref,
}: Props) {
  const [scrollElement, setScrollElement] = useState<HTMLDivElement | null>(null)
  const [width, setWidth] = useState(0)
  const [tileSizeKey] = useTileSize()

  const attach = useCallback((element: HTMLDivElement | null) => {
    setScrollElement(element)
    if (element === null) return
    // clientWidth includes the (now symmetric left/right) padding but not the scrollbar gutter.
    const measure = () => setWidth(Math.max(0, element.clientWidth - GRID_PADDING * 2))
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const columns = columnCount(width, TILE_MIN_WIDTH[tileSizeKey])
  const size = tileSize(width, columns)
  const rows = useMemo(() => buildRows(items, columns, groupKey), [items, columns, groupKey])

  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollElement,
    estimateSize: (index) =>
      rows[index]?.kind === 'header' ? GROUP_HEADER_HEIGHT : size + TILE_GAP,
    overscan: 2,
  })

  useEffect(() => {
    virtualizer.measure()
  }, [size, rows, virtualizer])

  useImperativeHandle(
    ref,
    () => ({
      scrollToGroup: (group) => {
        const index = rows.findIndex((row) => row.kind === 'header' && row.group === group)
        if (index < 0) return false
        virtualizer.scrollToIndex(index, { align: 'start' })
        return true
      },
    }),
    [rows, virtualizer],
  )

  const virtualRows = virtualizer.getVirtualItems()
  const lastVisibleRow = virtualRows.at(-1)?.index ?? -1

  // The first row that still reaches into the view (overscan rows above it don't count).
  const scrollOffset = virtualizer.scrollOffset ?? 0
  const topRow = virtualRows.find((row) => row.end > scrollOffset)
  const topGroup = topRow === undefined ? null : (rows[topRow.index]?.group ?? null)
  const reportedGroup = useRef<string | null>(null)
  useEffect(() => {
    if (topGroup === null || topGroup === reportedGroup.current) return
    reportedGroup.current = topGroup
    onTopGroupChange?.(topGroup)
  }, [topGroup, onTopGroupChange])

  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage && lastVisibleRow >= rows.length - 1 - PREFETCH_ROWS) {
      fetchNextPage()
    }
  }, [hasNextPage, isFetchingNextPage, lastVisibleRow, rows.length, fetchNextPage])

  return (
    <div
      ref={attach}
      className="min-h-0 flex-1 overflow-y-auto bg-zinc-50/60 dark:bg-transparent"
      data-testid="photo-grid"
      // A stable gutter stops the scrollbar appearing/disappearing from resizing the tiles in a loop.
      style={{
        scrollbarGutter: 'stable',
        paddingLeft: GRID_PADDING,
        paddingRight: GRID_PADDING,
        paddingTop: GRID_PADDING,
        paddingBottom: GRID_PADDING,
      }}
    >
      <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
        {width > 0 &&
          virtualRows.map((virtualRow) => {
            const row = rows[virtualRow.index]
            if (row === undefined) return null
            return (
              <div
                key={virtualRow.key}
                style={{
                  position: 'absolute',
                  top: 0,
                  left: 0,
                  right: 0,
                  transform: `translateY(${virtualRow.start}px)`,
                  display: 'flex',
                  gap: TILE_GAP,
                  ...(row.kind === 'header' && { height: GROUP_HEADER_HEIGHT, alignItems: 'center' }),
                }}
              >
                {row.kind === 'header'
                  ? renderGroupHeader?.(row.group)
                  : items.slice(row.start, row.end).map((item) => (
                      <Fragment key={item.id}>{renderTile(item, size)}</Fragment>
                    ))}
              </div>
            )
          })}
      </div>
      {isFetchingNextPage && <LinearProgress />}
    </div>
  )
}
