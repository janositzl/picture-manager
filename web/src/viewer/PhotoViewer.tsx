import ChevronLeftIcon from '@mui/icons-material/ChevronLeft'
import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import CloseIcon from '@mui/icons-material/Close'
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined'
import PlaylistAddIcon from '@mui/icons-material/PlaylistAdd'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import { Box, Button, CircularProgress, Dialog, IconButton, Stack, Typography } from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router'
import { AlbumPicker } from '../albums/AlbumPicker'
import { readLastUsedAlbum, writeLastUsedAlbum } from '../albums/preferences'
import { useAlbumAdder } from '../albums/useAlbumAdder'
import { isNotFound } from '../api/client'
import { useSetFavorite } from '../api/favorites'
import { albumsQuery, useImage } from '../api/queries'
import type { ImageListItem } from '../api/types'
import { useNotify } from '../app/notify'
import { parseGridParams, withParams } from '../routing/urlState'
import { InfoPanel } from './InfoPanel'

const INFO_PANEL_KEY = 'pm.viewer.infoOpen'

function readInfoOpen(): boolean {
  try {
    return localStorage.getItem(INFO_PANEL_KEY) !== 'false'
  } catch {
    return true
  }
}

function writeInfoOpen(open: boolean): void {
  try {
    localStorage.setItem(INFO_PANEL_KEY, String(open))
  } catch {
    // Storage unavailable (private mode, blocked): the panel just won't remember.
  }
}

type ViewerHistoryState = { viewer?: boolean } | null

export type ViewerItem = ImageListItem & { isMissing?: boolean }
/** What the viewer steps through: the grid's loaded items, and how to load more. */
export type ViewerList = {
  items: readonly ViewerItem[]
  hasNextPage: boolean
  fetchNextPage: () => unknown
}

const isMissingItem = (item: object): boolean => 'isMissing' in item && item.isMissing === true

/** Full-screen viewer for ?image=, stepping through the same cached list as the grid below it. */
export function PhotoViewer({ list }: { list: ViewerList }) {
  const [searchParams, setSearchParams] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const imageId = parseGridParams(searchParams).image
  const detail = useImage(imageId)
  const setFavorite = useSetFavorite()
  const [infoOpen, setInfoOpen] = useState(readInfoOpen)
  const [loadedSrc, setLoadedSrc] = useState<string | null>(null)
  const pendingNext = useRef(false)
  const queryClient = useQueryClient()
  const notify = useNotify()
  const { addTo } = useAlbumAdder()
  const [pickerOpen, setPickerOpen] = useState(false)

  const items = list.items
  const index = imageId === null ? -1 : items.findIndex((item) => item.id === imageId)
  const inList = index >= 0
  const current = inList ? items[index] : detail.data
  const prev = inList ? items[index - 1] : undefined
  const next = inList ? items[index + 1] : undefined
  const canGoPrev = prev !== undefined
  const canGoNext = inList && (next !== undefined || list.hasNextPage)
  const notFound = !inList && isNotFound(detail.error)

  const canAdd = current !== undefined && !isMissingItem(current)

  // Shift+A: straight into the last used album; without one (or if it's gone), fall back to the picker.
  const quickAdd = async () => {
    if (current === undefined || !canAdd) return
    const lastId = readLastUsedAlbum()
    const albums = lastId === null ? [] : await queryClient.ensureQueryData(albumsQuery)
    const album = albums.find((a) => a.id === lastId)
    if (album === undefined) {
      if (lastId !== null) writeLastUsedAlbum(null)
      setPickerOpen(true)
      return
    }
    const outcome = await addTo(album, { imageIds: [current.id] })
    if (outcome === 'gone') {
      notify('That album no longer exists.')
      setPickerOpen(true)
    } else if (outcome === 'failed') {
      notify("Couldn't add photos.")
    }
  }

  // Stepping replaces the entry, so Back closes the viewer instead of replaying every photo.
  const show = (id: number) =>
    setSearchParams(withParams(searchParams, { image: id }), {
      replace: true,
      state: location.state,
    })

  const close = () => {
    if ((location.state as ViewerHistoryState)?.viewer) navigate(-1)
    else setSearchParams(withParams(searchParams, { image: null }), { replace: true })
  }

  const goPrev = () => {
    if (prev !== undefined) show(prev.id)
  }

  const goNext = () => {
    if (next !== undefined) {
      show(next.id)
    } else if (inList && list.hasNextPage) {
      pendingNext.current = true
      void list.fetchNextPage()
    }
  }

  const toggleFavorite = () => {
    if (current !== undefined)
      setFavorite.mutate({ id: current.id, isFavorite: !current.isFavorite })
  }

  const toggleInfo = () => {
    const nextOpen = !infoOpen
    setInfoOpen(nextOpen)
    writeInfoOpen(nextOpen)
  }

  const onNextLoaded = useEffectEvent((id: number) => {
    pendingNext.current = false
    show(id)
  })

  useEffect(() => {
    if (pendingNext.current && next !== undefined) onNextLoaded(next.id)
  }, [next])

  const onKeyDown = useEffectEvent((event: KeyboardEvent) => {
    if (pickerOpen) return
    if (event.altKey || event.ctrlKey || event.metaKey) return
    if (event.target instanceof HTMLElement && event.target.closest('input, textarea')) return
    switch (event.key) {
      case 'ArrowLeft':
        event.preventDefault()
        goPrev()
        break
      case 'ArrowRight':
        event.preventDefault()
        goNext()
        break
      case 'Escape':
        event.preventDefault()
        close()
        break
      case 'f':
      case 'F':
        toggleFavorite()
        break
      case 'i':
      case 'I':
        toggleInfo()
        break
      case 'a':
      case 'A':
        // preventDefault: without it, the keystroke that opens the picker also lands in its
        // auto-focused filter field once focus moves there, filtering out every album.
        event.preventDefault()
        if (event.shiftKey) void quickAdd()
        else if (canAdd) setPickerOpen(true)
        break
    }
  })

  useEffect(() => {
    // Capture phase: MUI's Modal unconditionally stops Escape from bubbling
    // (regardless of disableEscapeKeyDown), so a bubble-phase listener never sees it.
    const listener = (event: KeyboardEvent) => onKeyDown(event)
    window.addEventListener('keydown', listener, true)
    return () => window.removeEventListener('keydown', listener, true)
  }, [])

  // Warm the browser cache so ←/→ feel instant.
  useEffect(() => {
    for (const neighbour of [prev, next]) {
      if (neighbour?.previewUrl) new Image().src = neighbour.previewUrl
    }
  }, [prev, next])

  const src = current?.previewUrl ?? null
  const name = current === undefined ? 'Photo viewer' : `${current.fileName}${current.extension}`

  let stage
  if (notFound) {
    stage = (
      <Stack spacing={2} sx={{ alignItems: 'center' }}>
        <Typography>This photo is no longer available.</Typography>
        <Button variant="contained" onClick={close}>
          Close
        </Button>
      </Stack>
    )
  } else if (current === undefined) {
    stage = <CircularProgress color="inherit" />
  } else if (isMissingItem(current)) {
    stage = (
      <Typography sx={{ color: 'grey.400' }}>
        The file for this photo is missing on disk.
      </Typography>
    )
  } else if (current.isInvalid) {
    stage = <Typography sx={{ color: 'grey.400' }}>This photo can't be displayed.</Typography>
  } else if (src === null) {
    stage = (
      <Typography sx={{ color: 'grey.400' }}>This photo hasn't been processed yet.</Typography>
    )
  } else {
    stage = (
      <>
        {loadedSrc !== src && <CircularProgress color="inherit" sx={{ position: 'absolute' }} />}
        <img
          key={src}
          src={src}
          alt={name}
          onLoad={() => setLoadedSrc(src)}
          style={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }}
        />
      </>
    )
  }

  return (
    <>
      <Dialog
        open
        fullScreen
        slotProps={{
          paper: { 'aria-label': name, sx: { bgcolor: 'common.black', color: 'common.white' } },
        }}
      >
        <Box sx={{ display: 'flex', height: '100%' }}>
          <Box
            sx={{
              position: 'relative',
              flex: 1,
              minWidth: 0,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            {stage}
            {canGoPrev && (
              <IconButton
                aria-label="Previous photo"
                onClick={goPrev}
                className="transition-all duration-200 ease-in-out hover:scale-[1.08]"
                sx={{
                  position: 'absolute',
                  left: 12,
                  color: 'common.white',
                  bgcolor: 'rgba(0,0,0,0.35)',
                  backdropFilter: 'blur(4px)',
                  '&:hover': { bgcolor: 'rgba(0,0,0,0.55)' },
                }}
              >
                <ChevronLeftIcon fontSize="large" />
              </IconButton>
            )}
            {canGoNext && (
              <IconButton
                aria-label="Next photo"
                onClick={goNext}
                className="transition-all duration-200 ease-in-out hover:scale-[1.08]"
                sx={{
                  position: 'absolute',
                  right: 12,
                  color: 'common.white',
                  bgcolor: 'rgba(0,0,0,0.35)',
                  backdropFilter: 'blur(4px)',
                  '&:hover': { bgcolor: 'rgba(0,0,0,0.55)' },
                }}
              >
                <ChevronRightIcon fontSize="large" />
              </IconButton>
            )}
            <Box sx={{ position: 'absolute', top: 12, right: 12, display: 'flex', gap: 0.75 }}>
              {current !== undefined && (
                <IconButton
                  aria-label={current.isFavorite ? 'Remove from favorites' : 'Add to favorites'}
                  aria-pressed={current.isFavorite}
                  onClick={toggleFavorite}
                  className="transition-all duration-200 ease-in-out hover:scale-[1.08]"
                  sx={{
                    color: 'warning.main',
                    bgcolor: 'rgba(0,0,0,0.35)',
                    backdropFilter: 'blur(4px)',
                    '&:hover': { bgcolor: 'rgba(0,0,0,0.55)' },
                  }}
                >
                  {current.isFavorite ? <StarIcon /> : <StarBorderIcon />}
                </IconButton>
              )}
              {canAdd && (
                <IconButton
                  aria-label="Add to album"
                  onClick={() => setPickerOpen(true)}
                  className="transition-all duration-200 ease-in-out hover:scale-[1.08]"
                  sx={{
                    color: 'common.white',
                    bgcolor: 'rgba(0,0,0,0.35)',
                    backdropFilter: 'blur(4px)',
                    '&:hover': { bgcolor: 'rgba(0,0,0,0.55)' },
                  }}
                >
                  <PlaylistAddIcon />
                </IconButton>
              )}
              <IconButton
                aria-label={infoOpen ? 'Hide info' : 'Show info'}
                onClick={toggleInfo}
                className="transition-all duration-200 ease-in-out hover:scale-[1.08]"
                sx={{
                  color: 'common.white',
                  bgcolor: 'rgba(0,0,0,0.35)',
                  backdropFilter: 'blur(4px)',
                  '&:hover': { bgcolor: 'rgba(0,0,0,0.55)' },
                }}
              >
                <InfoOutlinedIcon />
              </IconButton>
              <IconButton
                aria-label="Close viewer"
                onClick={close}
                className="transition-all duration-200 ease-in-out hover:scale-[1.08]"
                sx={{
                  color: 'common.white',
                  bgcolor: 'rgba(0,0,0,0.35)',
                  backdropFilter: 'blur(4px)',
                  '&:hover': { bgcolor: 'rgba(0,0,0,0.55)' },
                }}
              >
                <CloseIcon />
              </IconButton>
            </Box>
          </Box>
          {infoOpen && !notFound && (
            <InfoPanel
              detail={detail.data}
              isLoading={detail.isPending}
              isError={detail.isError}
              onRetry={() => void detail.refetch()}
            />
          )}
        </Box>
      </Dialog>
      {pickerOpen && current !== undefined && (
        <AlbumPicker target={{ imageIds: [current.id] }} onClose={() => setPickerOpen(false)} />
      )}
    </>
  )
}
