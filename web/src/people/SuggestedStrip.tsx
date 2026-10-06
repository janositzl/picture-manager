import CheckIcon from '@mui/icons-material/Check'
import CloseIcon from '@mui/icons-material/Close'
import ExpandLessIcon from '@mui/icons-material/ExpandLess'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import { Box, Button, IconButton, LinearProgress, Tooltip, Typography } from '@mui/material'
import { useMemo, useState, type KeyboardEvent } from 'react'
import { useLocation, useSearchParams } from 'react-router'
import type { ImageFilter } from '../api/imageFilter'
import { faceThumbnailUrl, useAcceptAllSuggestions, useImageSuggestion } from '../api/people'
import { useImages } from '../api/queries'
import type { ImageListItem } from '../api/types'
import { useNotify } from '../app/notify'
import { parseGridParams, withParams } from '../routing/urlState'
import { PhotoViewer } from '../viewer/PhotoViewer'
import type { FaceMode } from './faceModes'
import { FaceModeToggle } from './FaceModeToggle'
import { openedFromStrip } from './openedFromStrip'

const TILE = 112

/** The tile's neighbour, as the element to focus: a tile wrapper's first button, or a plain button (Show more). */
function focusTarget(sibling: Element | null): HTMLElement | null {
  if (sibling === null) return null
  return sibling.matches('button') ? (sibling as HTMLElement) : sibling.querySelector<HTMLElement>('button')
}

type Props = {
  personId: number
  sort: ImageFilter['sort']
  order: ImageFilter['order']
  mode: FaceMode
  onModeChange: (mode: FaceMode) => void
}

/** The images where AI suggests this person: accept or reject each, or accept them all. */
export function SuggestedStrip({ personId, sort, order, mode, onModeChange }: Props) {
  const filter: ImageFilter = { kind: 'person', personId, state: 'suggested', sort, order }
  const images = useImages(filter)
  const items = useMemo(() => images.data?.pages.flatMap((page) => page.items) ?? [], [images.data])
  const [open, setOpen] = useState(true)
  const [reviewed, setReviewed] = useState(0)
  const [searchParams, setSearchParams] = useSearchParams()
  const location = useLocation()
  const { image } = parseGridParams(searchParams)
  const suggestion = useImageSuggestion()
  const acceptAll = useAcceptAllSuggestions()
  const notify = useNotify()

  if (items.length === 0) return null

  const act = (item: ImageListItem, action: 'accept' | 'reject') =>
    suggestion.mutate(
      { personId, imageId: item.id, action },
      {
        onSuccess: () => setReviewed((count) => count + 1),
        onError: () => notify("Couldn't save that decision."),
      },
    )

  // Arrow keys move between tiles; A / R decide the focused one and hand focus to its neighbour.
  const onTileKeyDown = (event: KeyboardEvent<HTMLElement>, item: ImageListItem) => {
    if (event.ctrlKey || event.metaKey || event.altKey) return
    const tile = event.currentTarget
    const next = focusTarget(tile.nextElementSibling)
    const previous = focusTarget(tile.previousElementSibling)
    switch (event.key.toLowerCase()) {
      case 'arrowright':
        event.preventDefault()
        next?.focus()
        break
      case 'arrowleft':
        event.preventDefault()
        previous?.focus()
        break
      case 'a':
      case 'r':
        event.preventDefault()
        ;(next ?? previous)?.focus()
        act(item, event.key.toLowerCase() === 'a' ? 'accept' : 'reject')
        break
    }
  }

  const remaining = items.length
  const total = reviewed + remaining

  const openViewer = (id: number) =>
    setSearchParams(withParams(searchParams, { image: id }), {
      state: { viewer: true, list: 'suggested' },
    })

  return (
    <section
      aria-label="Suggested"
      className="mx-4 mb-1 mt-3 rounded-2xl border border-solid border-zinc-200 bg-zinc-50/70 p-3 dark:border-zinc-800 dark:bg-zinc-900/50"
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: open ? 1.5 : 0 }}>
        <IconButton
          size="small"
          aria-label={open ? 'Collapse suggestions' : 'Expand suggestions'}
          onClick={() => setOpen(!open)}
        >
          {open ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        </IconButton>
        <Typography variant="subtitle1" component="h2" sx={{ fontWeight: 600, letterSpacing: '-0.01em' }}>
          Suggested
        </Typography>
        <span className="rounded-full bg-[#ecebfb] px-2 py-0.5 text-xs font-medium tabular-nums text-[#4b46c4] dark:bg-indigo-500/15 dark:text-indigo-300">
          {items.length}
          {images.hasNextPage ? '+' : ''}
        </span>
        <Box sx={{ flex: 1 }} />
        {open && (
          <span className="hidden text-xs text-zinc-400 lg:inline">
            <kbd className="font-sans">←</kbd> <kbd className="font-sans">→</kbd> move ·{' '}
            <kbd className="font-sans">A</kbd> accept · <kbd className="font-sans">R</kbd> reject
          </span>
        )}
        <FaceModeToggle value={mode} onChange={onModeChange} label="Suggested view" />
        <Button
          size="small"
          variant="contained"
          color="success"
          disableElevation
          startIcon={<CheckIcon fontSize="small" />}
          sx={{ borderRadius: '12px', textTransform: 'none', fontWeight: 500 }}
          disabled={acceptAll.isPending}
          onClick={() =>
            acceptAll.mutate(personId, {
              onSuccess: ({ count }) => {
                setReviewed((value) => value + count)
                notify(`Accepted ${count} ${count === 1 ? 'suggestion' : 'suggestions'}.`)
              },
              onError: () => notify("Couldn't accept the suggestions."),
            })
          }
        >
          Accept all
        </Button>
      </Box>
      {reviewed > 0 && (
        <div className="mb-2 flex items-center gap-3">
          <LinearProgress
            variant="determinate"
            value={(reviewed / total) * 100}
            aria-label="Review progress"
            sx={{ flex: 1, height: 6, borderRadius: 3 }}
          />
          <span className="shrink-0 text-xs tabular-nums text-zinc-500">
            {reviewed} reviewed · {remaining}
            {images.hasNextPage ? '+' : ''} left
          </span>
        </div>
      )}
      {open && (
        // Tiles wrap into rows; the area grows with its content up to 70vh, then scrolls. Drag the bottom-right corner to resize.
        <Box
          sx={{
            display: 'flex',
            flexWrap: 'wrap',
            gap: 1.5,
            overflowY: 'auto',
            resize: 'vertical',
            minHeight: TILE + 16,
            maxHeight: '70vh',
            p: 0.5,
          }}
        >
          {items.map((item) => {
            const faceId = item.faceId ?? null
            const useCrop = mode === 'faces' && faceId !== null
            const src = useCrop ? faceThumbnailUrl(faceId) : item.thumbnailUrl
            const name = `${item.fileName}${item.extension}`
            return (
              <Box
                key={item.id}
                className="group transition-all duration-200 ease-in-out hover:scale-[1.03] focus-within:scale-[1.03]"
                onKeyDown={(event) => onTileKeyDown(event, item)}
                sx={{ position: 'relative', width: TILE, height: TILE, flexShrink: 0 }}
              >
                <Box
                  component="button"
                  type="button"
                  aria-label={`Open ${name}`}
                  onClick={() => openViewer(item.id)}
                  sx={{ width: '100%', height: '100%', p: 0, border: 0, borderRadius: '14px', overflow: 'hidden', cursor: 'pointer', bgcolor: 'action.hover', boxShadow: '0 1px 2px rgba(0,0,0,0.06)' }}
                >
                  {src !== null && (
                    <img
                      src={src}
                      alt=""
                      loading="lazy"
                      style={{ width: '100%', height: '100%', objectFit: useCrop ? 'cover' : 'contain', display: 'block' }}
                    />
                  )}
                </Box>
                <Box
                  className="opacity-70 transition-opacity duration-200 group-hover:opacity-100 group-focus-within:opacity-100"
                  sx={{ position: 'absolute', left: 0, right: 0, bottom: 0, display: 'flex', justifyContent: 'space-between', p: 0.75 }}
                >
                  <Tooltip title="Reject">
                    <IconButton
                      size="small"
                      aria-label={`Reject suggestion for ${name}`}
                      onClick={() => act(item, 'reject')}
                      sx={{ color: 'common.white', bgcolor: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(4px)', transition: 'all 200ms ease-in-out', '&:hover': { bgcolor: 'error.main' } }}
                    >
                      <CloseIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title="Accept">
                    <IconButton
                      size="small"
                      aria-label={`Accept suggestion for ${name}`}
                      onClick={() => act(item, 'accept')}
                      sx={{ color: 'common.white', bgcolor: 'rgba(0,0,0,0.55)', backdropFilter: 'blur(4px)', transition: 'all 200ms ease-in-out', '&:hover': { bgcolor: 'success.main' } }}
                    >
                      <CheckIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                </Box>
              </Box>
            )
          })}
          {images.hasNextPage && (
            <Button
              size="small"
              onClick={() => void images.fetchNextPage({ cancelRefetch: false })}
              sx={{ flexShrink: 0, width: TILE, borderRadius: '14px', textTransform: 'none', bgcolor: 'action.hover' }}
            >
              Show more
            </Button>
          )}
        </Box>
      )}
      {image !== null && openedFromStrip(location.state) && (
        <PhotoViewer
          faceReview={{ personId }}
          list={{
            items,
            hasNextPage: images.hasNextPage,
            fetchNextPage: () => images.fetchNextPage({ cancelRefetch: false }),
          }}
        />
      )}
    </section>
  )
}
