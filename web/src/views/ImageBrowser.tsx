import { useCallback, useMemo, useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { AlbumPicker } from '../albums/AlbumPicker'
import type { AddTarget } from '../api/albums'
import { useSetFavorite } from '../api/favorites'
import { useSetHidden } from '../api/hidden'
import type { ImageFilter } from '../api/imageFilter'
import { faceThumbnailUrl } from '../api/people'
import { useImages } from '../api/queries'
import { useRotateThumbnails } from '../api/rotation'
import type { ImageListItem } from '../api/types'
import { PhotoGrid } from '../grid/PhotoGrid'
import { PhotoTile } from '../grid/PhotoTile'
import { SelectionBar } from '../grid/SelectionBar'
import { useSelection } from '../grid/useSelection'
import { parseGridParams, withParams } from '../routing/urlState'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { PhotoViewer } from '../viewer/PhotoViewer'
import { GridSkeleton } from './GridSkeleton'

type Props = {
  filter: ImageFilter
  header: ReactNode
  banner?: ReactNode
  /** Favorites view: an unstarred photo stays, dimmed, until the view is left. */
  dimUnfavorited?: boolean
  captionFor?: (item: ImageListItem) => string | null
  emptyState: ReactNode
  /** Person lists: show each photo's face crop instead of its thumbnail. */
  showFaceCrops?: boolean
  /** Opens the viewer in face-review mode for this person. */
  faceReview?: { personId: number }
  /** Unknown-group views: offers assigning the selected photos; clearSelection ends the selection. */
  onAssignSelected?: (imageIds: number[], clearSelection: () => void) => void
  /** False when something else (e.g. a suggestions strip) owns the viewer for ?image=. */
  viewerEnabled?: boolean
  /** Folder view only: the selection bar offers Hide / Unhide. */
  hideable?: boolean
  /** Folder view only: the selection bar offers Rotate right (thumbnails only). */
  rotatable?: boolean
}

/** Header (or selection bar), banner and virtualized grid for one image filter. */
export function ImageBrowser({
  filter,
  header,
  banner,
  dimUnfavorited = false,
  captionFor,
  emptyState,
  showFaceCrops = false,
  faceReview,
  onAssignSelected,
  viewerEnabled = true,
  hideable = false,
  rotatable = false,
}: Props) {
  const [searchParams, setSearchParams] = useSearchParams()
  const { image } = parseGridParams(searchParams)
  const images = useImages(filter)
  const setFavorite = useSetFavorite()
  const items = useMemo(() => images.data?.pages.flatMap((page) => page.items) ?? [], [images.data])
  const ids = useMemo(() => items.map((item) => item.id), [items])
  const filterKey = JSON.stringify(filter)
  const selection = useSelection(ids, filterKey)
  const [pickerTarget, setPickerTarget] = useState<AddTarget | null>(null)
  const setHidden = useSetHidden()
  const selectedItems = items.filter((item) => selection.selected.has(item.id))
  // Unhide only when every selected photo is hidden; a mixed selection hides the rest.
  const allHidden =
    selectedItems.length > 0 && selectedItems.every((item) => item.isHidden === true)
  const rotateThumbnails = useRotateThumbnails()
  const rotateSelected = () =>
    rotateThumbnails.mutate({ imageIds: selectedItems.map((item) => item.id), degrees: 90 })
  const hideSelected = () =>
    setHidden.mutate(
      { imageIds: selectedItems.map((item) => item.id), isHidden: !allHidden },
      { onSuccess: selection.clear },
    )

  // Opening is a push (Back closes the viewer); the state marks it as opened in-app.
  // The functional update keeps `open` stable across renders instead of depending on `searchParams`.
  const open = useCallback(
    (id: number) =>
      setSearchParams((prev) => withParams(prev, { image: id }), { state: { viewer: true } }),
    [setSearchParams],
  )
  const onToggleFavorite = useCallback(
    (tile: ImageListItem) => setFavorite.mutate({ id: tile.id, isFavorite: !tile.isFavorite }),
    [setFavorite],
  )

  let body: ReactNode
  if (images.isPending) {
    body = <GridSkeleton />
  } else if (images.isError) {
    body = <QueryErrorAlert message="Couldn't load photos." onRetry={() => void images.refetch()} />
  } else if (items.length === 0) {
    body = emptyState
  } else {
    body = (
      <PhotoGrid
        // A new filter (folder, sort, query) starts a new grid at the top.
        key={filterKey}
        items={items}
        hasNextPage={images.hasNextPage}
        isFetchingNextPage={images.isFetchingNextPage}
        fetchNextPage={() => void images.fetchNextPage({ cancelRefetch: false })}
        renderTile={(item, size) => (
          <PhotoTile
            item={item}
            size={size}
            caption={captionFor?.(item) ?? null}
            dimmed={dimUnfavorited && !item.isFavorite}
            selecting={selection.isSelecting}
            selected={selection.selected.has(item.id)}
            onSelect={selection.toggle}
            deferImage
            thumbnailOverride={
              showFaceCrops && item.faceId != null ? faceThumbnailUrl(item.faceId) : undefined
            }
            onOpen={open}
            onToggleFavorite={onToggleFavorite}
          />
        )}
      />
    )
  }

  return (
    <>
      {selection.isSelecting ? (
        <SelectionBar
          count={selection.count}
          onAddToAlbum={() =>
            setPickerTarget({ imageIds: ids.filter((id) => selection.selected.has(id)) })
          }
          onAssign={
            onAssignSelected &&
            (() =>
              onAssignSelected(
                ids.filter((id) => selection.selected.has(id)),
                selection.clear,
              ))
          }
          onRotate={rotatable ? rotateSelected : undefined}
          onHide={hideable ? hideSelected : undefined}
          hideLabel={allHidden ? 'Unhide' : 'Hide'}
          onClear={selection.clear}
        />
      ) : (
        header
      )}
      {banner}
      {body}
      {image !== null && viewerEnabled && (
        <PhotoViewer
          faceReview={faceReview}
          list={{
            items,
            hasNextPage: images.hasNextPage,
            fetchNextPage: () => images.fetchNextPage({ cancelRefetch: false }),
          }}
        />
      )}
      {pickerTarget !== null && (
        <AlbumPicker
          target={pickerTarget}
          onClose={() => setPickerTarget(null)}
          onAdded={selection.clear}
        />
      )}
    </>
  )
}
