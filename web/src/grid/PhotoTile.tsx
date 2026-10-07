import BrokenImageIcon from '@mui/icons-material/BrokenImage'
import HourglassEmptyIcon from '@mui/icons-material/HourglassEmpty'
import ImageNotSupportedIcon from '@mui/icons-material/ImageNotSupported'
import VisibilityOffOutlinedIcon from '@mui/icons-material/VisibilityOffOutlined'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import { Box, Checkbox, Chip, IconButton, Typography } from '@mui/material'
import { memo, useEffect, useState, type HTMLAttributes, type ReactNode } from 'react'
import type { ImageListItem } from '../api/types'
import type { SelectMods } from './useSelection'

/** Tiles this small can't fit both the photo and a readable caption, so the caption hides until hover. */
const CAPTION_HOVER_ONLY_BELOW = 130
/** How long a tile must stay mounted before its thumbnail is actually requested. */
const IMAGE_REQUEST_DELAY_MS = 100

/** dnd-kit's drag handle wiring for a sortable tile (the tile itself is the handle). */
export type TileActivator = {
  ref: (element: HTMLElement | null) => void
  props: HTMLAttributes<HTMLElement>
  /** True while this tile is being dragged, including the Space that drops it. */
  dragging?: boolean
}

type Props = {
  item: ImageListItem
  size: number
  caption: string | null
  /** A small label pinned to the tile's top edge, e.g. "Largest". */
  badge?: string
  dimmed: boolean
  missing?: boolean
  selecting?: boolean
  selected?: boolean
  onSelect?: (id: number, mods: SelectMods) => void
  activator?: TileActivator
  /** A face crop shown instead of the thumbnail (cover-fitted). */
  thumbnailOverride?: string
  /** In a fast-scrolling virtualized grid: don't request a thumbnail for a tile scrolled past within IMAGE_REQUEST_DELAY_MS. */
  deferImage?: boolean
  onOpen: (id: number) => void
  onToggleFavorite: (item: ImageListItem) => void
}

function PhotoTileComponent({
  item,
  size,
  caption,
  badge,
  dimmed,
  missing = false,
  selecting = false,
  selected = false,
  onSelect,
  activator,
  deferImage = false,
  thumbnailOverride,
  onOpen,
  onToggleFavorite,
}: Props) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const [imageReady, setImageReady] = useState(!deferImage)
  const [loaded, setLoaded] = useState(false)
  const name = `${item.fileName}${item.extension}`
  const thumbnail = thumbnailOverride ?? item.thumbnailUrl

  useEffect(() => {
    if (!deferImage) return
    const timer = setTimeout(() => setImageReady(true), IMAGE_REQUEST_DELAY_MS)
    return () => clearTimeout(timer)
  }, [deferImage])

  let content: ReactNode
  if (missing) {
    content = <Placeholder icon={<ImageNotSupportedIcon />} label="File missing" />
  } else if (item.isInvalid) {
    content = <Placeholder icon={<BrokenImageIcon />} label="Corrupt file" />
  } else if (thumbnail === null) {
    content = <Placeholder icon={<HourglassEmptyIcon />} label="Processing" />
  } else if (failedSrc === thumbnail) {
    content = <Placeholder icon={<BrokenImageIcon />} label="Thumbnail unavailable" />
  } else if (!imageReady) {
    content = null
  } else {
    content = (
      <img
        src={thumbnail}
        alt=""
        decoding="async"
        onLoad={() => setLoaded(true)}
        onError={() => setFailedSrc(thumbnail)}
        style={{
          width: '100%',
          height: '100%',
          objectFit: thumbnailOverride === undefined ? 'contain' : 'cover',
          display: 'block',
          opacity: loaded ? 1 : 0,
          transition: 'opacity 0.15s ease-in-out',
        }}
      />
    )
  }

  // While selecting, activating a tile toggles it instead of opening the viewer.
  const activate = (shift: boolean) => {
    if (onSelect && selecting) onSelect(item.id, { shift })
    else onOpen(item.id)
  }

  return (
    <Box
      ref={activator?.ref}
      {...activator?.props}
      role="button"
      tabIndex={0}
      aria-label={name}
      title={name}
      data-testid={`tile-${item.id}`}
      data-dimmed={dimmed}
      data-selected={selected}
      onClick={(event) => {
        if (onSelect && !selecting && (event.ctrlKey || event.metaKey)) {
          onSelect(item.id, { shift: false })
          return
        }
        activate(event.shiftKey)
      }}
      onKeyDown={(event) => {
        // dnd-kit picks the tile up on Space (and marks the event handled); Enter still opens it.
        activator?.props.onKeyDown?.(event)
        if (event.defaultPrevented) return
        // The drop Space reaches here too: dnd-kit's own end handler runs on a later, document-level
        // listener, so `defaultPrevented` isn't set yet by the time this fires. Read the drag state
        // captured at render instead of trusting that this key was already handled.
        if (activator?.dragging) return
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          activate(event.shiftKey)
        }
      }}
      className="group transition-all duration-200 ease-in-out hover:brightness-[1.04]"
      sx={{
        position: 'relative',
        width: size,
        height: size,
        flexShrink: 0,
        overflow: 'hidden',
        borderRadius: '10px',
        cursor: 'pointer',
        bgcolor: 'action.hover',
        opacity: dimmed ? 0.4 : item.isHidden ? 0.55 : 1,
        outline: selected ? '3px solid' : 'none',
        outlineColor: 'primary.main',
        outlineOffset: -3,
        boxShadow: selected ? 2 : 0,
        '& .tile-star': { opacity: item.isFavorite ? 1 : 0 },
        '& .tile-check': { opacity: selecting ? 1 : 0 },
        '& .tile-caption': { opacity: size < CAPTION_HOVER_ONLY_BELOW ? 0 : 1 },
        '&:hover .tile-star, &:focus-within .tile-star, &:hover .tile-check, &:focus-within .tile-check, &:hover .tile-caption, &:focus-within .tile-caption':
          { opacity: 1 },
      }}
    >
      {content}
      {selected && (
        <Box
          aria-hidden
          sx={{
            position: 'absolute',
            inset: 0,
            bgcolor: 'primary.main',
            opacity: 0.2,
            pointerEvents: 'none',
          }}
        />
      )}
      {item.isHidden && (
        <VisibilityOffOutlinedIcon
          aria-hidden
          fontSize="small"
          sx={{
            position: 'absolute',
            top: 8,
            right: 8,
            color: 'common.white',
            filter: 'drop-shadow(0 0 2px rgba(0,0,0,0.7))',
            pointerEvents: 'none',
          }}
        />
      )}
      {onSelect && (
        <Checkbox
          className="tile-check"
          size="small"
          checked={selected}
          slotProps={{ input: { 'aria-label': `Select ${name}` } }}
          onClick={(event) => event.stopPropagation()}
          onKeyDown={(event) => {
            // Only Enter/Space are the tile's own activation keys; Escape and Ctrl+A must still
            // reach the window-level selection shortcuts while the checkbox holds focus.
            if (event.key === 'Enter' || event.key === ' ') event.stopPropagation()
          }}
          onChange={(event) =>
            onSelect(item.id, {
              shift: (event.nativeEvent as MouseEvent).shiftKey === true,
            })
          }
          sx={{
            position: 'absolute',
            top: 6,
            left: 6,
            p: 0.5,
            borderRadius: '6px',
            color: 'common.white',
            bgcolor: 'rgba(0,0,0,0.4)',
            backdropFilter: 'blur(2px)',
            transition: 'background-color 0.2s ease-in-out',
            '&:hover': { bgcolor: 'rgba(0,0,0,0.6)' },
            '&.Mui-checked': { color: 'common.white' },
          }}
        />
      )}
      {badge !== undefined && (
        <Chip
          size="small"
          label={badge}
          sx={{
            position: 'absolute',
            top: 8,
            left: 42,
            color: 'common.white',
            bgcolor: 'rgba(0,0,0,0.5)',
            backdropFilter: 'blur(2px)',
          }}
        />
      )}
      <IconButton
        size="small"
        aria-label={item.isFavorite ? `Remove ${name} from favorites` : `Add ${name} to favorites`}
        aria-pressed={item.isFavorite}
        onClick={(event) => {
          event.stopPropagation()
          onToggleFavorite(item)
        }}
        onKeyDown={(event) => event.stopPropagation()}
        className="tile-star transition-all duration-200 ease-in-out"
        sx={{
          position: 'absolute',
          top: 6,
          right: 6,
          color: 'warning.main',
          bgcolor: 'rgba(0,0,0,0.4)',
          backdropFilter: 'blur(2px)',
          '&:hover': { bgcolor: 'rgba(0,0,0,0.6)' },
        }}
      >
        {item.isFavorite ? <StarIcon fontSize="small" /> : <StarBorderIcon fontSize="small" />}
      </IconButton>
      <Box
        className="tile-caption"
        sx={{
          position: 'absolute',
          left: 0,
          right: 0,
          bottom: 0,
          px: 1,
          py: 0.75,
          color: 'common.white',
          background: 'linear-gradient(to top, rgba(0,0,0,0.7), rgba(0,0,0,0))',
          transition: 'opacity 0.2s ease-in-out',
        }}
      >
        <Typography variant="caption" noWrap component="div" sx={{ fontWeight: 500 }}>
          {name}
        </Typography>
        {caption !== null && (
          <Typography variant="caption" noWrap component="div" sx={{ opacity: 0.8 }}>
            {caption}
          </Typography>
        )}
      </Box>
    </Box>
  )
}

export const PhotoTile = memo(PhotoTileComponent)

function Placeholder({ icon, label }: { icon: ReactNode; label: string }) {
  return (
    <Box
      sx={{
        width: '100%',
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 0.5,
        color: 'text.secondary',
      }}
    >
      {icon}
      <Typography variant="caption">{label}</Typography>
    </Box>
  )
}
