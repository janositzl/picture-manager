import { useCallback, useState } from 'react'
import type { AlbumImageItem } from '../api/types'
import { columnCount, GRID_PADDING, TILE_GAP, tileSize } from '../grid/columns'
import { PhotoTile } from '../grid/PhotoTile'
import type { Selection } from '../grid/useSelection'

type Props = {
  items: AlbumImageItem[]
  showFolders: boolean
  selection: Selection
  onOpen: (id: number) => void
  onToggleFavorite: (item: AlbumImageItem) => void
}

/** The whole album in one plain grid (not virtualized), so any tile can be dragged anywhere. */
export function AlbumGrid({ items, showFolders, selection, onOpen, onToggleFavorite }: Props) {
  const [width, setWidth] = useState(0)

  const attach = useCallback((element: HTMLDivElement | null) => {
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

  return (
    <div
      ref={attach}
      className="min-h-0 flex-1 overflow-y-auto"
      data-testid="album-grid"
      style={{ scrollbarGutter: 'stable', paddingRight: GRID_PADDING, paddingBottom: GRID_PADDING }}
    >
      {width > 0 && (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: `repeat(${columns}, ${size}px)`,
            gap: TILE_GAP,
          }}
        >
          {items.map((item) => (
            <PhotoTile
              key={item.id}
              item={item}
              size={size}
              caption={showFolders ? item.folderPath : null}
              dimmed={false}
              missing={item.isMissing}
              selection={{
                selecting: selection.isSelecting,
                selected: selection.selected.has(item.id),
                onSelect: selection.toggle,
              }}
              onOpen={onOpen}
              onToggleFavorite={() => onToggleFavorite(item)}
            />
          ))}
        </div>
      )}
    </div>
  )
}
