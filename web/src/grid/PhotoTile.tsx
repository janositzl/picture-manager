import BrokenImageIcon from '@mui/icons-material/BrokenImage'
import HourglassEmptyIcon from '@mui/icons-material/HourglassEmpty'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import { Box, IconButton, Typography } from '@mui/material'
import { useState, type ReactNode } from 'react'
import type { ImageListItem } from '../api/types'

type Props = {
  item: ImageListItem
  size: number
  caption: string | null
  dimmed: boolean
  onOpen: (id: number) => void
  onToggleFavorite: (item: ImageListItem) => void
}

export function PhotoTile({ item, size, caption, dimmed, onOpen, onToggleFavorite }: Props) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const name = `${item.fileName}${item.extension}`
  const thumbnail = item.thumbnailUrl

  let content: ReactNode
  if (thumbnail === null) {
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

  return (
    <Box
      role="button"
      tabIndex={0}
      aria-label={name}
      title={name}
      data-testid={`tile-${item.id}`}
      data-dimmed={dimmed}
      onClick={() => onOpen(item.id)}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          onOpen(item.id)
        }
      }}
      sx={{
        position: 'relative',
        width: size,
        height: size,
        flexShrink: 0,
        overflow: 'hidden',
        cursor: 'pointer',
        bgcolor: 'action.hover',
        opacity: dimmed ? 0.4 : 1,
        '& .tile-star': { opacity: item.isFavorite ? 1 : 0 },
        '&:hover .tile-star, &:focus-within .tile-star': { opacity: 1 },
      }}
    >
      {content}
      <IconButton
        className="tile-star"
        size="small"
        aria-label={item.isFavorite ? `Remove ${name} from favorites` : `Add ${name} to favorites`}
        aria-pressed={item.isFavorite}
        onClick={(event) => {
          event.stopPropagation()
          onToggleFavorite(item)
        }}
        onKeyDown={(event) => event.stopPropagation()}
        sx={{
          position: 'absolute',
          top: 4,
          right: 4,
          color: 'warning.main',
          bgcolor: 'rgba(0,0,0,0.35)',
          '&:hover': { bgcolor: 'rgba(0,0,0,0.55)' },
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
          py: 0.25,
          color: 'common.white',
          bgcolor: 'rgba(0,0,0,0.5)',
        }}
      >
        <Typography variant="caption" noWrap component="div">
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
