import BrokenImageIcon from '@mui/icons-material/BrokenImage'
import HourglassEmptyIcon from '@mui/icons-material/HourglassEmpty'
import ImageNotSupportedIcon from '@mui/icons-material/ImageNotSupported'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import { Box, Checkbox, IconButton, Typography } from '@mui/material'
import { useState, type HTMLAttributes, type ReactNode } from 'react'
import type { ImageListItem } from '../api/types'
import type { SelectMods } from './useSelection'

export type TileSelection = {
  selecting: boolean
  selected: boolean
  onSelect: (id: number, mods: SelectMods) => void
}

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
  dimmed: boolean
  missing?: boolean
  selection?: TileSelection
  activator?: TileActivator
  onOpen: (id: number) => void
  onToggleFavorite: (item: ImageListItem) => void
}

export function PhotoTile({
  item,
  size,
  caption,
  dimmed,
  missing = false,
  selection,
  activator,
  onOpen,
  onToggleFavorite,
}: Props) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const name = `${item.fileName}${item.extension}`
  const thumbnail = item.thumbnailUrl
  const selecting = selection?.selecting ?? false
  const selected = selection?.selected ?? false

  let content: ReactNode
  if (missing) {
    content = <Placeholder icon={<ImageNotSupportedIcon />} label="File missing" />
  } else if (item.isInvalid) {
    content = <Placeholder icon={<BrokenImageIcon />} label="Corrupt file" />
  } else if (thumbnail === null) {
    content = <Placeholder icon={<HourglassEmptyIcon />} label="Processing" />
  } else if (failedSrc === thumbnail) {
    content = <Placeholder icon={<BrokenImageIcon />} label="Thumbnail unavailable" />
  } else {
    content = (
      <img
        src={thumbnail}
        alt=""
        loading="lazy"
        onError={() => setFailedSrc(thumbnail)}
        style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }}
      />
    )
  }

  // While selecting, activating a tile toggles it instead of opening the viewer.
  const activate = (shift: boolean) => {
    if (selection && selecting) selection.onSelect(item.id, { shift })
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
        if (selection && !selecting && (event.ctrlKey || event.metaKey)) {
          selection.onSelect(item.id, { shift: false })
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
        opacity: dimmed ? 0.4 : 1,
        outline: selected ? '3px solid' : 'none',
        outlineColor: 'primary.main',
        outlineOffset: -3,
        boxShadow: selected ? 2 : 0,
        '& .tile-star': { opacity: item.isFavorite ? 1 : 0 },
        '& .tile-check': { opacity: selecting ? 1 : 0 },
        '&:hover .tile-star, &:focus-within .tile-star, &:hover .tile-check, &:focus-within .tile-check':
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
      {selection && (
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
            selection.onSelect(item.id, {
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
        sx={{
          position: 'absolute',
          left: 0,
          right: 0,
          bottom: 0,
          px: 1,
          py: 0.75,
          color: 'common.white',
          background: 'linear-gradient(to top, rgba(0,0,0,0.7), rgba(0,0,0,0))',
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
