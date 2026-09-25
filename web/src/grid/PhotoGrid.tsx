import { LinearProgress } from '@mui/material'
import { useVirtualizer } from '@tanstack/react-virtual'
import { Fragment, useCallback, useEffect, useState, type ReactNode } from 'react'
import type { ImageListItem } from '../api/types'
import { columnCount, GRID_PADDING, TILE_GAP, tileSize } from './columns'

type Props = {
  items: ImageListItem[]
  hasNextPage: boolean
  isFetchingNextPage: boolean
  fetchNextPage: () => void
  renderTile: (item: ImageListItem, size: number) => ReactNode
}

/** Rows within this many of the end of the loaded items trigger the next page. */
const PREFETCH_ROWS = 2

export function PhotoGrid({
  items,
  hasNextPage,
  isFetchingNextPage,
  fetchNextPage,
  renderTile,
}: Props) {
  const [scrollElement, setScrollElement] = useState<HTMLDivElement | null>(null)
  const [width, setWidth] = useState(0)

  const attach = useCallback((element: HTMLDivElement | null) => {
    setScrollElement(element)
    if (element === null) return
    // clientWidth includes the padding but not the (always reserved) scrollbar gutter.
    const measure = () => setWidth(Math.max(0, element.clientWidth - GRID_PADDING))
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const columns = columnCount(width)
  const size = tileSize(width, columns)
  const rowCount = Math.ceil(items.length / columns)

  const virtualizer = useVirtualizer({
    count: rowCount,
    getScrollElement: () => scrollElement,
    estimateSize: () => size + TILE_GAP,
    overscan: 2,
  })

  useEffect(() => {
    virtualizer.measure()
  }, [size, virtualizer])

  const virtualRows = virtualizer.getVirtualItems()
  const lastVisibleRow = virtualRows.at(-1)?.index ?? -1

  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage && lastVisibleRow >= rowCount - 1 - PREFETCH_ROWS) {
      fetchNextPage()
    }
  }, [hasNextPage, isFetchingNextPage, lastVisibleRow, rowCount, fetchNextPage])

  return (
    <div
      ref={attach}
      className="min-h-0 flex-1 overflow-y-auto"
      data-testid="photo-grid"
      // A stable gutter stops the scrollbar appearing/disappearing from resizing the tiles in a loop.
      style={{ scrollbarGutter: 'stable', paddingRight: GRID_PADDING, paddingBottom: GRID_PADDING }}
    >
      <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
        {width > 0 &&
          virtualRows.map((row) => (
            <div
              key={row.key}
              style={{
                position: 'absolute',
                top: 0,
                left: 0,
                right: 0,
                transform: `translateY(${row.start}px)`,
                display: 'flex',
                gap: TILE_GAP,
              }}
            >
              {items.slice(row.index * columns, row.index * columns + columns).map((item) => (
                <Fragment key={item.id}>{renderTile(item, size)}</Fragment>
              ))}
            </div>
          ))}
      </div>
      {isFetchingNextPage && <LinearProgress />}
    </div>
  )
}
