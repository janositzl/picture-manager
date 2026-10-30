import KeyboardArrowDownIcon from '@mui/icons-material/KeyboardArrowDown'
import KeyboardArrowUpIcon from '@mui/icons-material/KeyboardArrowUp'
import UnfoldMoreIcon from '@mui/icons-material/UnfoldMore'
import {
  Box,
  Button,
  CircularProgress,
  IconButton,
  ListItemText,
  Menu,
  MenuItem,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import type { ImageDay } from '../api/types'
import { formatDay } from './dayFormat'

type Props = {
  /** In the order the grid shows them. */
  days: ImageDay[]
  /** The day at the top of the grid, or null before anything is shown. */
  current: string | null
  /** A jump to a day that isn't loaded yet is in progress. */
  loading: boolean
  onSelect: (day: string) => void
}

/** Shows the day at the top of the grid and jumps to the previous/next/any day. */
export function DayBar({ days, current, loading, onSelect }: Props) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  if (days.length === 0) return null

  const index = days.findIndex((entry) => entry.day === current)
  const active = days[index === -1 ? 0 : index]!
  const activeIndex = index === -1 ? 0 : index
  const previous = days[activeIndex - 1]
  const next = days[activeIndex + 1]

  return (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 0.5,
        px: 2.5,
        py: 0.5,
        borderBottom: 1,
        borderColor: 'divider',
        bgcolor: 'background.paper',
      }}
    >
      <IconButton
        size="small"
        aria-label="Previous day"
        disabled={previous === undefined}
        onClick={() => previous && onSelect(previous.day)}
      >
        <KeyboardArrowUpIcon fontSize="small" />
      </IconButton>
      <IconButton
        size="small"
        aria-label="Next day"
        disabled={next === undefined}
        onClick={() => next && onSelect(next.day)}
      >
        <KeyboardArrowDownIcon fontSize="small" />
      </IconButton>
      <Button
        size="small"
        color="inherit"
        aria-haspopup="menu"
        aria-label="Choose a day"
        onClick={(event) => setAnchor(event.currentTarget)}
        endIcon={<UnfoldMoreIcon fontSize="small" />}
        sx={{ textTransform: 'none', fontWeight: 600 }}
      >
        {formatDay(active.day)}
        <Typography component="span" variant="body2" color="text.secondary" sx={{ ml: 1 }}>
          {active.count === 1 ? '1 photo' : `${active.count} photos`}
        </Typography>
      </Button>
      {loading && <CircularProgress size={16} aria-label="Loading day" />}
      <Menu
        anchorEl={anchor}
        open={anchor !== null}
        onClose={() => setAnchor(null)}
        slotProps={{ paper: { sx: { maxHeight: 360 } } }}
      >
        {days.map((entry) => (
          <MenuItem
            key={entry.day}
            selected={entry.day === active.day}
            onClick={() => {
              setAnchor(null)
              onSelect(entry.day)
            }}
          >
            <ListItemText
              primary={formatDay(entry.day)}
              secondary={entry.count === 1 ? '1 photo' : `${entry.count} photos`}
            />
          </MenuItem>
        ))}
      </Menu>
    </Box>
  )
}
