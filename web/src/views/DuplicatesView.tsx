import { Box, LinearProgress, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { AlbumPicker } from '../albums/AlbumPicker'
import type { AddTarget } from '../api/albums'
import { useSetFavorite } from '../api/favorites'
import { useDuplicates } from '../api/queries'
import { PhotoTile } from '../grid/PhotoTile'
import { SelectionBar } from '../grid/SelectionBar'
import { useSelection } from '../grid/useSelection'
import { parseGridParams, withParams } from '../routing/urlState'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { PhotoViewer } from '../viewer/PhotoViewer'
import { GridSkeleton } from './GridSkeleton'

const TILE_SIZE = 180
/** Load the next page of groups when this close (px) to the bottom. */
const LOAD_MORE_MARGIN = 400
const noMorePages = () => undefined

/** Read-only review of identical photos: delete extra copies on disk, then rescan. */
export function DuplicatesView() {
  const [searchParams, setSearchParams] = useSearchParams()
  const { image } = parseGridParams(searchParams)
  const groups = useDuplicates()
  const setFavorite = useSetFavorite()
  const all = useMemo(() => groups.data?.pages.flatMap((page) => page.items) ?? [], [groups.data])
  const ids = useMemo(() => all.flatMap((group) => group.images.map((item) => item.id)), [all])
  const selection = useSelection(ids, 'duplicates')
  const [pickerTarget, setPickerTarget] = useState<AddTarget | null>(null)
  const scrollRef = useRef<HTMLDivElement>(null)
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = groups

  const loadMoreIfNearEnd = useCallback(() => {
    const element = scrollRef.current
    if (element === null || !hasNextPage || isFetchingNextPage) return
    if (element.scrollTop + element.clientHeight >= element.scrollHeight - LOAD_MORE_MARGIN) {
      void fetchNextPage()
    }
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])

  // Also after each page: a short first page may not fill the screen, so no scroll event would come.
  useEffect(loadMoreIfNearEnd, [loadMoreIfNearEnd, all.length])

  const open = (id: number) =>
    setSearchParams(withParams(searchParams, { image: id }), { state: { viewer: true } })
  const viewerGroup =
    image === null ? undefined : all.find((group) => group.images.some((item) => item.id === image))

  let body: ReactNode
  if (groups.isPending) {
    body = <GridSkeleton />
  } else if (groups.isError) {
    body = (
      <QueryErrorAlert message="Couldn't load duplicates." onRetry={() => void groups.refetch()} />
    )
  } else if (all.length === 0) {
    body = <EmptyMessage>No duplicates found.</EmptyMessage>
  } else {
    body = (
      <Box
        ref={scrollRef}
        onScroll={loadMoreIfNearEnd}
        sx={{
          flex: 1,
          minHeight: 0,
          overflowY: 'auto',
          p: 2,
          display: 'flex',
          flexDirection: 'column',
          gap: 3,
        }}
      >
        {all.map((group) => (
          <Box
            component="section"
            key={group.contentHash}
            aria-labelledby={`dup-${group.contentHash}`}
          >
            <Typography
              id={`dup-${group.contentHash}`}
              variant="subtitle1"
              component="h2"
              gutterBottom
            >
              {group.count} copies
            </Typography>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
              {group.images.map((item) => (
                <PhotoTile
                  key={item.id}
                  item={item}
                  size={TILE_SIZE}
                  caption={item.folderPath}
                  dimmed={false}
                  selection={{
                    selecting: selection.isSelecting,
                    selected: selection.selected.has(item.id),
                    onSelect: selection.toggle,
                  }}
                  onOpen={open}
                  onToggleFavorite={(tile) =>
                    setFavorite.mutate({ id: tile.id, isFavorite: !tile.isFavorite })
                  }
                />
              ))}
            </Box>
          </Box>
        ))}
        {isFetchingNextPage && <LinearProgress />}
      </Box>
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
          onClear={selection.clear}
        />
      ) : (
        <Box sx={{ px: 2, py: 1, borderBottom: 1, borderColor: 'divider' }}>
          <Typography variant="h6" component="h1">
            Duplicates
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Photos with identical content. Delete extra copies on disk, then rescan.
          </Typography>
        </Box>
      )}
      {body}
      {image !== null && (
        <PhotoViewer
          list={{
            items: viewerGroup?.images ?? [],
            hasNextPage: false,
            fetchNextPage: noMorePages,
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
