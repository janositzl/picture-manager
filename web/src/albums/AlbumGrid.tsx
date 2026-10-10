import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import {
  rectSortingStrategy,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { useCallback, useState, type HTMLAttributes, type ReactNode } from 'react'
import type { AlbumImageItem } from '../api/types'
import { columnCount, GRID_PADDING, TILE_GAP, tileSize } from '../grid/columns'
import { PhotoTile, type TileActivator } from '../grid/PhotoTile'
import { TILE_MIN_WIDTH, useTileSize } from '../grid/tileSize'
import type { Selection } from '../grid/useSelection'

type Props = {
  items: AlbumImageItem[]
  showFolders: boolean
  selection: Selection
  onOpen: (id: number) => void
  onToggleFavorite: (item: AlbumImageItem) => void
  onMove: (activeId: number, overId: number) => void
  /** Viewers can't reorder, so dragging is off for them. */
  reorderable: boolean
}

/** The whole album in one plain grid (not virtualized), so any tile can be dragged anywhere. */
export function AlbumGrid({
  items,
  showFolders,
  selection,
  onOpen,
  onToggleFavorite,
  onMove,
  reorderable,
}: Props) {
  const [width, setWidth] = useState(0)
  const [tileSizeKey] = useTileSize()
  const sensors = useSensors(
    // A few pixels of travel before a drag starts, so a click still opens the photo.
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
      keyboardCodes: { start: ['Space'], cancel: ['Escape'], end: ['Space'] },
    }),
  )

  const attach = useCallback((element: HTMLDivElement | null) => {
    if (element === null) return
    // clientWidth includes the (symmetric left/right) padding but not the scrollbar gutter.
    const measure = () => setWidth(Math.max(0, element.clientWidth - GRID_PADDING * 2))
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const columns = columnCount(width, TILE_MIN_WIDTH[tileSizeKey])
  const size = tileSize(width, columns)
  const ids = items.map((item) => item.id)

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (over !== null && active.id !== over.id) onMove(Number(active.id), Number(over.id))
  }

  return (
    <div
      ref={attach}
      className="min-h-0 flex-1 overflow-y-auto bg-zinc-50/60 dark:bg-transparent"
      data-testid="album-grid"
      style={{
        scrollbarGutter: 'stable',
        paddingLeft: GRID_PADDING,
        paddingRight: GRID_PADDING,
        paddingTop: GRID_PADDING,
        paddingBottom: GRID_PADDING,
      }}
    >
      {width > 0 && (
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
          {/* A click must mean "toggle" while selecting, so dragging is off then. */}
          <SortableContext
            items={ids}
            strategy={rectSortingStrategy}
            disabled={selection.isSelecting || !reorderable}
          >
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: `repeat(${columns}, ${size}px)`,
                gap: TILE_GAP,
              }}
            >
              {items.map((item, index) => (
                <SortableTile key={item.id} id={item.id} index={index}>
                  {(activator) => (
                    <PhotoTile
                      item={item}
                      size={size}
                      caption={showFolders ? item.folderPath : null}
                      dimmed={false}
                      missing={item.isMissing}
                      selecting={selection.isSelecting}
                      selected={selection.selected.has(item.id)}
                      onSelect={selection.toggle}
                      activator={activator}
                      onOpen={onOpen}
                      onToggleFavorite={() => onToggleFavorite(item)}
                    />
                  )}
                </SortableTile>
              ))}
            </div>
          </SortableContext>
        </DndContext>
      )}
    </div>
  )
}

function SortableTile({
  id,
  index,
  children,
}: {
  id: number
  index: number
  children: (activator: TileActivator) => ReactNode
}) {
  const {
    attributes,
    listeners,
    setNodeRef,
    setActivatorNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id })
  return (
    <div
      ref={setNodeRef}
      data-sort-index={index}
      style={{
        // Other tiles slide aside to preview where the drop would land.
        transform: CSS.Transform.toString(transform),
        transition,
        position: 'relative',
        zIndex: isDragging ? 1 : undefined,
        opacity: isDragging ? 0.6 : 1,
      }}
    >
      {children({
        ref: setActivatorNodeRef,
        dragging: isDragging,
        props: { ...attributes, ...(listeners as HTMLAttributes<HTMLElement> | undefined) },
      })}
    </div>
  )
}
