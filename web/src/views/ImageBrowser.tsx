import { Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { AlbumPicker } from '../albums/AlbumPicker'
import type { AddTarget } from '../api/albums'
import { useSetFavorite } from '../api/favorites'
import { useSetHidden } from '../api/hidden'
import type { ImageFilter } from '../api/imageFilter'
import { faceThumbnailUrl } from '../api/people'
import { fetchImageIds, useImageDays, useImages } from '../api/queries'
import { useRotateThumbnails } from '../api/rotation'
import type { ImageListItem } from '../api/types'
import { useNotify } from '../app/notify'
import { PhotoGrid, type PhotoGridHandle } from '../grid/PhotoGrid'
import { PhotoTile } from '../grid/PhotoTile'
import { dayOf } from '../grid/rows'
import { SelectionBar } from '../grid/SelectionBar'
import { useSelection } from '../grid/useSelection'
import { parseGridParams, withParams } from '../routing/urlState'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { PhotoViewer } from '../viewer/PhotoViewer'
import { DayBar } from './DayBar'
import { formatDay } from './dayFormat'
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
  /** Person views: offers using the single selected photo's face as the person's cover; clearSelection ends the selection. */
  onSetCoverSelected?: (faceId: number, clearSelection: () => void) => void
  /** False when something else (e.g. a suggestions strip) owns the viewer for ?image=. */
  viewerEnabled?: boolean
  /** Folder view only: the selection bar offers Hide / Unhide. */
  hideable?: boolean
  /** Folder view only: the selection bar offers Rotate right (thumbnails only). */
  rotatable?: boolean
  /** Folder view only: one continuous grid with a section per day and a day bar (the filter must sort by date). */
  groupByDay?: boolean
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
  onSetCoverSelected,
  viewerEnabled = true,
  hideable = false,
  rotatable = false,
  groupByDay = false,
}: Props) {
  const [searchParams, setSearchParams] = useSearchParams()
  const { image } = parseGridParams(searchParams)
  const images = useImages(filter)
  const setFavorite = useSetFavorite()
  const items = useMemo(() => images.data?.pages.flatMap((page) => page.items) ?? [], [images.data])
  const ids = useMemo(() => items.map((item) => item.id), [items])
  const filterKey = JSON.stringify(filter)

  const days = useImageDays(filter, groupByDay)
  const dayCounts = useMemo(
    () => new Map(days.data?.map((entry) => [entry.day, entry.count])),
    [days.data],
  )
  const gridRef = useRef<PhotoGridHandle>(null)
  const [topDay, setTopDay] = useState<string | null>(null)
  // A day picked in the day bar that may not be loaded yet: load pages until its header exists, then scroll.
  const pendingDay = useRef<string | null>(null)
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = images
  const advancePendingDay = useCallback(() => {
    const day = pendingDay.current
    if (day === null) return
    if (gridRef.current?.scrollToGroup(day)) pendingDay.current = null
    else if (hasNextPage && !isFetchingNextPage) void fetchNextPage({ cancelRefetch: false })
    else if (!hasNextPage) pendingDay.current = null
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])
  useEffect(() => {
    advancePendingDay()
  }, [advancePendingDay, items])
  const jumpToDay = useCallback(
    (day: string) => {
      setTopDay(day)
      pendingDay.current = day
      advancePendingDay()
    },
    [advancePendingDay],
  )
  const notify = useNotify()
  const [selectingAll, setSelectingAll] = useState(false)
  // "Select all" covers every matching photo; the loaded pages are only part of them while more pages remain.
  async function selectAllPhotos() {
    if (!images.hasNextPage) {
      selection.selectAll()
      return
    }
    setSelectingAll(true)
    try {
      const result = await fetchImageIds(filter)
      selection.selectIds(result.ids)
      if (result.truncated)
        notify(`Selected the first ${result.ids.length.toLocaleString()} photos.`)
    } catch {
      notify("Couldn't select all photos.")
    } finally {
      setSelectingAll(false)
    }
  }
  const selection = useSelection(ids, filterKey, { onSelectAll: () => void selectAllPhotos() })
  const [pickerTarget, setPickerTarget] = useState<AddTarget | null>(null)
  const setHidden = useSetHidden()
  // Grid order for the loaded photos, then any selected ones that are not loaded (after Select all).
  const selectedIds = useMemo(() => {
    const loaded = new Set(ids)
    return [
      ...ids.filter((id) => selection.selected.has(id)),
      ...[...selection.selected].filter((id) => !loaded.has(id)),
    ]
  }, [ids, selection.selected])
  const selectedItems = items.filter((item) => selection.selected.has(item.id))
  // Unhide only when every selected photo is hidden; a mixed selection hides the rest.
  const allHidden =
    selectedItems.length > 0 && selectedItems.every((item) => item.isHidden === true)
  const coverFaceId = selectedItems.length === 1 ? (selectedItems[0]?.faceId ?? null) : null
  const rotateThumbnails = useRotateThumbnails()
  const rotateSelected = () => rotateThumbnails.mutate({ imageIds: selectedIds, degrees: 90 })
  const hideSelected = () =>
    setHidden.mutate(
      { imageIds: selectedIds, isHidden: !allHidden },
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
        // A new filter (folder, sort, query) or grouping starts a new grid at the top.
        key={`${filterKey}|${groupByDay}`}
        ref={gridRef}
        items={items}
        groupKey={groupByDay ? dayOf : undefined}
        onTopGroupChange={groupByDay ? setTopDay : undefined}
        renderGroupHeader={(day) => (
          <Typography component="h2" sx={{ fontWeight: 600, fontSize: 15 }}>
            {formatDay(day)}
            <Typography component="span" variant="body2" color="text.secondary" sx={{ ml: 1 }}>
              {dayCounts.get(day) ?? ''}
            </Typography>
          </Typography>
        )}
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
          onAddToAlbum={() => setPickerTarget({ imageIds: selectedIds })}
          onAssign={onAssignSelected && (() => onAssignSelected(selectedIds, selection.clear))}
          onSetCover={
            onSetCoverSelected && coverFaceId !== null
              ? () => onSetCoverSelected(coverFaceId, selection.clear)
              : undefined
          }
          onRotate={rotatable ? rotateSelected : undefined}
          onHide={hideable ? hideSelected : undefined}
          hideLabel={allHidden ? 'Unhide' : 'Hide'}
          onSelectAll={() => void selectAllPhotos()}
          selectAllPending={selectingAll}
          onClear={selection.clear}
        />
      ) : (
        header
      )}
      {banner}
      {groupByDay && days.data !== undefined && (
        <DayBar
          days={days.data}
          current={topDay ?? days.data[0]?.day ?? null}
          loading={isFetchingNextPage}
          onSelect={jumpToDay}
        />
      )}
      {body}
      {image !== null && viewerEnabled && (
        <PhotoViewer
          faceReview={faceReview}
          hideable={hideable}
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
