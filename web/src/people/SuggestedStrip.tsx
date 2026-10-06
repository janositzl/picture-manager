import CheckIcon from '@mui/icons-material/Check'
import CloseIcon from '@mui/icons-material/Close'
import ExpandLessIcon from '@mui/icons-material/ExpandLess'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import { Box, Button, IconButton, Tooltip, Typography } from '@mui/material'
import { useMemo, useState } from 'react'
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
      { onError: () => notify("Couldn't save that decision.") },
    )

  const openViewer = (id: number) =>
    setSearchParams(withParams(searchParams, { image: id }), {
      state: { viewer: true, list: 'suggested' },
    })

  return (
    <Box component="section" aria-label="Suggested" sx={{ px: 2, py: 1.5, borderBottom: 1, borderColor: 'divider' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: open ? 1 : 0 }}>
        <IconButton
          size="small"
          aria-label={open ? 'Collapse suggestions' : 'Expand suggestions'}
          onClick={() => setOpen(!open)}
        >
          {open ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        </IconButton>
        <Typography variant="subtitle1" component="h2" sx={{ fontWeight: 600 }}>
          Suggested
        </Typography>
        <Box sx={{ flex: 1 }} />
        <FaceModeToggle value={mode} onChange={onModeChange} label="Suggested view" />
        <Button
          size="small"
          variant="contained"
          disabled={acceptAll.isPending}
          onClick={() =>
            acceptAll.mutate(personId, {
              onSuccess: ({ count }) =>
                notify(`Accepted ${count} ${count === 1 ? 'suggestion' : 'suggestions'}.`),
              onError: () => notify("Couldn't accept the suggestions."),
            })
          }
        >
          Accept all
        </Button>
      </Box>
      {open && (
        <Box sx={{ display: 'flex', gap: 1, overflowX: 'auto', pb: 0.5 }}>
          {items.map((item) => {
            const faceId = item.faceId ?? null
            const useCrop = mode === 'faces' && faceId !== null
            const src = useCrop ? faceThumbnailUrl(faceId) : item.thumbnailUrl
            const name = `${item.fileName}${item.extension}`
            return (
              <Box key={item.id} sx={{ position: 'relative', width: TILE, height: TILE, flexShrink: 0 }}>
                <Box
                  component="button"
                  type="button"
                  aria-label={`Open ${name}`}
                  onClick={() => openViewer(item.id)}
                  sx={{ width: '100%', height: '100%', p: 0, border: 0, borderRadius: '10px', overflow: 'hidden', cursor: 'pointer', bgcolor: 'action.hover' }}
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
                <Box sx={{ position: 'absolute', left: 0, right: 0, bottom: 0, display: 'flex', justifyContent: 'space-between', p: 0.5 }}>
                  <Tooltip title="Reject">
                    <IconButton
                      size="small"
                      aria-label={`Reject suggestion for ${name}`}
                      onClick={() => act(item, 'reject')}
                      sx={{ color: 'common.white', bgcolor: 'rgba(0,0,0,0.5)', '&:hover': { bgcolor: 'error.main' } }}
                    >
                      <CloseIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title="Accept">
                    <IconButton
                      size="small"
                      aria-label={`Accept suggestion for ${name}`}
                      onClick={() => act(item, 'accept')}
                      sx={{ color: 'common.white', bgcolor: 'rgba(0,0,0,0.5)', '&:hover': { bgcolor: 'success.main' } }}
                    >
                      <CheckIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                </Box>
              </Box>
            )
          })}
          {images.hasNextPage && (
            <Button size="small" onClick={() => void images.fetchNextPage({ cancelRefetch: false })} sx={{ flexShrink: 0 }}>
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
    </Box>
  )
}
