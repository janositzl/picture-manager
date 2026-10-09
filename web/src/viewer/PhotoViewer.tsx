import ChevronLeftIcon from '@mui/icons-material/ChevronLeft'
import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import CloseIcon from '@mui/icons-material/Close'
import FaceRetouchingNaturalIcon from '@mui/icons-material/FaceRetouchingNatural'
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined'
import PlaylistAddIcon from '@mui/icons-material/PlaylistAdd'
import RotateRightIcon from '@mui/icons-material/RotateRight'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import {
  Box,
  Button,
  CircularProgress,
  Dialog,
  IconButton,
  Stack,
  Tooltip,
  Typography,
} from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router'
import { AlbumPicker } from '../albums/AlbumPicker'
import { AlbumRemovePicker } from '../albums/AlbumRemovePicker'
import { readLastUsedAlbum, writeLastUsedAlbum } from '../albums/preferences'
import { useAlbumAdder } from '../albums/useAlbumAdder'
import { usePermissions } from '../api/auth'
import { isNotFound } from '../api/client'
import { useRemoveImagesFromAlbum } from '../api/albums'
import { useSetFavorite } from '../api/favorites'
import { albumsQuery, useImage } from '../api/queries'
import { useRotateThumbnails } from '../api/rotation'
import type { AlbumRef, ImageListItem } from '../api/types'
import { useNotify } from '../app/notify'
import { parseGridParams, withParams } from '../routing/urlState'
import { FaceOverlay } from './FaceOverlay'
import { InfoPanel } from './InfoPanel'
import { ConfirmedPeople, PeopleInPhoto } from './PeopleInPhoto'
import { useFaceReview } from './useFaceReview'

const INFO_PANEL_KEY = 'pm.viewer.infoOpen'
/** Face review has its own People panel, so there Info starts closed and is remembered separately. */
const REVIEW_INFO_PANEL_KEY = 'pm.viewer.infoOpen.review'

function readInfoOpen(key: string, fallback: boolean): boolean {
  try {
    const stored = localStorage.getItem(key)
    return stored === null ? fallback : stored !== 'false'
  } catch {
    return fallback
  }
}

function writeInfoOpen(key: string, open: boolean): void {
  try {
    localStorage.setItem(key, String(open))
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
export function PhotoViewer({
  list,
  onRemoveFromAlbum,
  faceReview: openedFor,
}: {
  /** Set when opened from a person: shows the detected faces and lets the user decide about them. */
  faceReview?: { personId: number }
  list: ViewerList
  /** Set when the list is an album: Shift+D removes from it. Resolves true once the photo is removed. */
  onRemoveFromAlbum?: (imageId: number) => Promise<boolean>
}) {
  const [searchParams, setSearchParams] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const imageId = parseGridParams(searchParams).image
  const detail = useImage(imageId)
  const setFavorite = useSetFavorite()
  const rotate = useRotateThumbnails()
  const { isAdmin } = usePermissions()
  // Any viewer can switch to face review (for photos no person's grid leads to); opened from a person it starts on.
  const [reviewing, setReviewing] = useState(false)
  const faceReview: { personId: number | null } | undefined =
    openedFor ?? (reviewing ? { personId: null } : undefined)
  const infoKey = openedFor !== undefined ? REVIEW_INFO_PANEL_KEY : INFO_PANEL_KEY
  const [infoOpen, setInfoOpen] = useState(() => readInfoOpen(infoKey, openedFor === undefined))
  const [loadedSrc, setLoadedSrc] = useState<string | null>(null)
  const [imageEl, setImageEl] = useState<HTMLImageElement | null>(null)
  const pendingNext = useRef(false)
  const queryClient = useQueryClient()
  const notify = useNotify()
  const { addTo } = useAlbumAdder()
  const [pickerOpen, setPickerOpen] = useState(false)
  const removeFromAlbum = useRemoveImagesFromAlbum()
  const [removeChoices, setRemoveChoices] = useState<AlbumRef[] | null>(null)

  const items = list.items
  const index = imageId === null ? -1 : items.findIndex((item) => item.id === imageId)
  const inList = index >= 0
  // Where the photo last sat in the list. If it then drops out (e.g. its last person was just assigned and it no
  // longer belongs to this unknown group), its neighbours are still the items on either side of that spot.
  const [anchor, setAnchor] = useState<{ id: number; index: number } | null>(null)
  if (inList && imageId !== null && (anchor?.id !== imageId || anchor.index !== index)) setAnchor({ id: imageId, index })
  const gap = !inList && anchor !== null && anchor.id === imageId ? Math.min(anchor.index, items.length) : -1
  const placed = inList || gap >= 0
  const current = inList ? items[index] : detail.data
  const prev = inList ? items[index - 1] : gap >= 0 ? items[gap - 1] : undefined
  const next = inList ? items[index + 1] : gap >= 0 ? items[gap] : undefined
  const canGoPrev = prev !== undefined
  const canGoNext = placed && (next !== undefined || list.hasNextPage)
  const notFound = !placed && isNotFound(detail.error)

  const review = useFaceReview(current?.id ?? null, faceReview)
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

  const removeFrom = async (album: AlbumRef, imageId: number) => {
    try {
      await removeFromAlbum.mutateAsync({ albumId: album.id, imageIds: [imageId] })
      notify(`Removed 1 photo from ${album.name}.`)
      return true
    } catch {
      notify("Couldn't remove photo.")
      return false
    }
  }

  // Shift+D. In an album view: drop the photo from that album and move on to its neighbour.
  // Elsewhere: remove it from the last used album if it's in it, else from its only album;
  // a photo in several others asks which.
  const quickRemove = async () => {
    if (current === undefined) return
    if (onRemoveFromAlbum !== undefined) {
      if (!inList) return
      const neighbour = next ?? prev
      if (!(await onRemoveFromAlbum(current.id))) return
      if (neighbour !== undefined) show(neighbour.id)
      else close()
      return
    }
    if (detail.data === undefined || detail.data.id !== current.id) return
    const albums = detail.data.albums
    if (albums.length === 0) {
      notify("This photo isn't in any album.")
      return
    }
    const lastId = readLastUsedAlbum()
    const target =
      albums.find((a) => a.id === lastId) ?? (albums.length === 1 ? albums[0] : undefined)
    if (target === undefined) setRemoveChoices(albums)
    else await removeFrom(target, current.id)
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
    } else if (placed && list.hasNextPage) {
      pendingNext.current = true
      void list.fetchNextPage()
    }
  }

  const toggleFavorite = () => {
    if (current !== undefined)
      setFavorite.mutate({ id: current.id, isFavorite: !current.isFavorite })
  }

  const rotateRight = () => {
    if (isAdmin && current !== undefined && canAdd && !rotate.isPending)
      rotate.mutate({ imageIds: [current.id], degrees: 90 })
  }

  const toggleReview = () => {
    if (!reviewing) setInfoOpen(false)
    setReviewing(!reviewing)
  }

  const toggleInfo = () => {
    const nextOpen = !infoOpen
    setInfoOpen(nextOpen)
    writeInfoOpen(infoKey, nextOpen)
  }

  const onNextLoaded = useEffectEvent((id: number) => {
    pendingNext.current = false
    show(id)
  })

  useEffect(() => {
    if (pendingNext.current && next !== undefined) onNextLoaded(next.id)
  }, [next])

  const onKeyDown = useEffectEvent((event: KeyboardEvent) => {
    if (pickerOpen || removeChoices !== null) return
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
      case 'r':
      case 'R':
        rotateRight()
        break
      case 'a':
      case 'A':
        // preventDefault: without it, the keystroke that opens the picker also lands in its
        // auto-focused filter field once focus moves there, filtering out every album.
        event.preventDefault()
        if (event.shiftKey) void quickAdd()
        else if (canAdd) setPickerOpen(true)
        break
      case 'd':
      case 'D':
        if (event.shiftKey) void quickRemove()
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
          ref={setImageEl}
          src={src}
          alt={name}
          onLoad={() => setLoadedSrc(src)}
          style={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }}
        />
        {faceReview !== undefined && loadedSrc === src && (
          <FaceOverlay
            image={imageEl}
            faces={review.faces}
            hoveredId={review.hoveredId}
            selectedId={review.selectedId}
            onHover={review.setHoveredId}
            onSelect={review.select}
          />
        )}
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
              <Tooltip title="Previous photo (←)">
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
              </Tooltip>
            )}
            {canGoNext && (
              <Tooltip title="Next photo (→)">
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
              </Tooltip>
            )}
            <Box sx={{ position: 'absolute', top: 12, right: 12, display: 'flex', gap: 0.75 }}>
              {current !== undefined && (
                <Tooltip
                  title={current.isFavorite ? 'Remove from favorites (F)' : 'Add to favorites (F)'}
                >
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
                </Tooltip>
              )}
              {canAdd && isAdmin && (
                <Tooltip title="Rotate right (R)">
                  <IconButton
                    aria-label="Rotate right"
                    onClick={rotateRight}
                    className="transition-all duration-200 ease-in-out hover:scale-[1.08]"
                    sx={{
                      color: 'common.white',
                      bgcolor: 'rgba(0,0,0,0.35)',
                      backdropFilter: 'blur(4px)',
                      '&:hover': { bgcolor: 'rgba(0,0,0,0.55)' },
                    }}
                  >
                    <RotateRightIcon />
                  </IconButton>
                </Tooltip>
              )}
              {canAdd && (
                <Tooltip title="Add to album (A, Shift+A for last used)">
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
                </Tooltip>
              )}
              {openedFor === undefined && isAdmin && (
                <Tooltip title={reviewing ? 'Hide face review' : 'Review faces'}>
                  <IconButton
                    aria-label={reviewing ? 'Hide face review' : 'Review faces'}
                    aria-pressed={reviewing}
                    onClick={toggleReview}
                    className="transition-all duration-200 ease-in-out hover:scale-[1.08]"
                    sx={{
                      color: 'common.white',
                      bgcolor: reviewing ? 'rgba(255,255,255,0.3)' : 'rgba(0,0,0,0.35)',
                      backdropFilter: 'blur(4px)',
                      '&:hover': { bgcolor: 'rgba(0,0,0,0.55)' },
                    }}
                  >
                    <FaceRetouchingNaturalIcon />
                  </IconButton>
                </Tooltip>
              )}
              <Tooltip
                title={`${infoOpen ? 'Hide info' : 'Show info'} (I)${current === undefined ? '' : ` – ${name}`}`}
              >
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
              </Tooltip>
              <Tooltip title="Close (Esc)">
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
              </Tooltip>
            </Box>
          </Box>
          {infoOpen && !notFound && (
            <InfoPanel
              detail={detail.data}
              isLoading={detail.isPending}
              isError={detail.isError}
              onRetry={() => void detail.refetch()}
              peopleSlot={
                current === undefined || faceReview !== undefined ? null : <ConfirmedPeople imageId={current.id} />
              }
            />
          )}
          {faceReview !== undefined && current !== undefined && !notFound && (
            <PeopleInPhoto review={review} item={current} />
          )}
        </Box>
      </Dialog>
      {removeChoices !== null && current !== undefined && (
        <AlbumRemovePicker
          albums={removeChoices}
          disabled={removeFromAlbum.isPending}
          onChoose={(album) =>
            void removeFrom(album, current.id).then((removed) => {
              if (removed) setRemoveChoices(null)
            })
          }
          onClose={() => setRemoveChoices(null)}
        />
      )}
      {pickerOpen && current !== undefined && (
        <AlbumPicker target={{ imageIds: [current.id] }} onClose={() => setPickerOpen(false)} />
      )}
    </>
  )
}
