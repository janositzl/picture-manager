import CloseIcon from '@mui/icons-material/Close'
import { Box, Button, IconButton, Typography } from '@mui/material'

type Props = {
  count: number
  onAddToAlbum: () => void
  onClear: () => void
  onRemove?: () => void
}

/** Replaces a grid's header while photos are selected. */
export function SelectionBar({ count, onAddToAlbum, onClear, onRemove }: Props) {
  return (
    <Box
      role="toolbar"
      aria-label="Selection"
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 1,
        px: 1,
        py: 1,
        borderBottom: 1,
        borderColor: 'divider',
        bgcolor: 'action.selected',
      }}
    >
      <IconButton aria-label="Clear selection" onClick={onClear}>
        <CloseIcon />
      </IconButton>
      <Typography sx={{ flex: 1 }}>{count} selected</Typography>
      <Button variant="contained" size="small" onClick={onAddToAlbum}>
        Add to album…
      </Button>
      {onRemove && (
        <Button color="error" size="small" onClick={onRemove}>
          Remove from album
        </Button>
      )}
      <Button size="small" onClick={onClear}>
        Clear
      </Button>
    </Box>
  )
}
