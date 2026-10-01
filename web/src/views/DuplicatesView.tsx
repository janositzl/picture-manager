import { Box, LinearProgress, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { AlbumPicker } from '../albums/AlbumPicker'
import type { AddTarget } from '../api/albums'
import { useSetFavorite } from '../api/favorites'
import { useDuplicates, useSimilarDuplicates } from '../api/queries'
import type { ImageListItem } from '../api/types'
import { PhotoTile } from '../grid/PhotoTile'
import { SelectionBar } from '../grid/SelectionBar'
import { TILE_MIN_WIDTH, useTileSize } from '../grid/tileSize'
import { useSelection } from '../grid/useSelection'
import { parseGridParams, withParams } from '../routing/urlState'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { PhotoViewer } from '../viewer/PhotoViewer'
import { GridSkeleton } from './GridSkeleton'

/** Load the next page of groups when this close (px) to the bottom. */
const LOAD_MORE_MARGIN = 400
const noMorePages = () => undefined

type Mode = 'exact' | 'similar'
type ViewGroup = { key: string; label: string; images: ImageListItem[] }

const SUBTITLE: Record<Mode, string> = {
  exact: 'Photos with identical content. Delete extra copies on disk, then rescan.',
  similar:
    'Photos that look the same, including resized or re-encoded copies. Delete extra copies on disk, then rescan.',
}

/** Read-only review of identical photos: delete extra copies on disk, then rescan. */
export function DuplicatesView() {
  const [searchParams, setSearchParams] = useSearchParams()
  const { image } = parseGridParams(searchParams)
  const mode: Mode = searchParams.get('mode') === 'similar' ? 'similar' : 'exact'
  const exactQuery = useDuplicates()
  const similarQuery = useSimilarDuplicates(mode === 'similar')
  const groups = mode === 'similar' ? similarQuery : exactQuery
  const setFavorite = useSetFavorite()
  const [tileSizeKey] = useTileSize()
  const all = useMemo<ViewGroup[]>(
    () =>
      mode === 'similar'
        ? (similarQuery.data?.pages.flatMap((page) => page.items) ?? []).map((group) => ({
            key: group.key,
            label: `${group.count} similar photos`,
            images: group.images,
          }))
        : (exactQuery.data?.pages.flatMap((page) => page.items) ?? []).map((group) => ({
            key: group.contentHash,
            label: `${group.count} copies`,
            images: group.images,
          })),
    [mode, exactQuery.data, similarQuery.data],
  )
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
    body = (
      <EmptyMessage>
        {mode === 'similar' ? 'No similar photos found.' : 'No duplicates found.'}
      </EmptyMessage>
    )
  } else {
    body = (
      <Box
        ref={scrollRef}
        onScroll={loadMoreIfNearEnd}
        className="bg-zinc-50/60 dark:bg-transparent"
        sx={{
          flex: 1,
          minHeight: 0,
          overflowY: 'auto',
          p: 3,
          display: 'flex',
          flexDirection: 'column',
          gap: 4,
        }}
      >
        {all.map((group) => (
          <Box
            component="section"
            key={group.key}
            aria-labelledby={`dup-${group.key}`}
            className="rounded-xl border border-zinc-100 bg-white p-4 shadow-sm dark:border-zinc-800 dark:bg-zinc-900"
          >
            <Typography
              id={`dup-${group.key}`}
              variant="subtitle1"
              component="h2"
              gutterBottom
              sx={{ fontWeight: 600, letterSpacing: '-0.01em' }}
            >
              {group.label}
            </Typography>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
              {group.images.map((item, index) => (
                <PhotoTile
                  key={item.id}
                  item={item}
                  size={TILE_MIN_WIDTH[tileSizeKey]}
                  caption={
                    mode === 'similar'
                      ? `${item.folderPath} · ${item.width} × ${item.height}`
                      : item.folderPath
                  }
                  badge={mode === 'similar' && index === 0 ? 'Largest' : undefined}
                  dimmed={false}
                  selecting={selection.isSelecting}
                  selected={selection.selected.has(item.id)}
                  onSelect={selection.toggle}
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
        <Box sx={{ px: 2.5, py: 1.25, borderBottom: 1, borderColor: 'divider' }}>
          <Typography
            variant="h6"
            component="h1"
            sx={{ fontWeight: 600, letterSpacing: '-0.01em' }}
          >
            Duplicates
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {SUBTITLE[mode]}
          </Typography>
          <ToggleButtonGroup
            exclusive
            size="small"
            value={mode}
            aria-label="Duplicate mode"
            sx={{ mt: 1 }}
            onChange={(_event, next: Mode | null) => {
              if (next !== null)
                setSearchParams(
                  withParams(searchParams, { mode: next === 'similar' ? next : null, image: null }),
                )
            }}
          >
            <ToggleButton value="exact">Exact</ToggleButton>
            <ToggleButton value="similar">Similar</ToggleButton>
          </ToggleButtonGroup>
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
