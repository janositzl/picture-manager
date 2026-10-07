import CloseIcon from '@mui/icons-material/Close'
import RotateRightIcon from '@mui/icons-material/RotateRight'
import { Box, Button, IconButton, Typography } from '@mui/material'

type Props = {
  count: number
  onAddToAlbum: () => void
  /** Unknown-group views: assign just the selected photos to a person. */
  onAssign?: () => void
  onClear: () => void
  onRemove?: () => void
  /** Folder view: hide the selected photos (or unhide them, when they are all hidden). */
  onHide?: () => void
  hideLabel?: 'Hide' | 'Unhide'
  /** Folder view: turn the selected photos' thumbnails 90° right (for photos with wrong EXIF orientation). */
  onRotate?: () => void
}

/** Replaces a grid's header while photos are selected. */
export function SelectionBar({
  count,
  onAddToAlbum,
  onAssign,
  onClear,
  onRemove,
  onHide,
  hideLabel = 'Hide',
  onRotate,
}: Props) {
  return (
    <Box
      role="toolbar"
      aria-label="Selection"
      className="backdrop-blur-sm"
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 1.5,
        px: 2,
        py: 1.25,
        borderBottom: 1,
        borderColor: 'divider',
        bgcolor: 'action.selected',
      }}
    >
      <IconButton
        aria-label="Clear selection"
        onClick={onClear}
        className="transition-all duration-200 ease-in-out hover:scale-[1.05]"
      >
        <CloseIcon fontSize="small" />
      </IconButton>
      <Typography sx={{ flex: 1, fontWeight: 600, letterSpacing: '-0.01em' }}>
        {count} selected
      </Typography>
      <Button
        variant="contained"
        size="small"
        onClick={onAddToAlbum}
        disableElevation
        sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600 }}
      >
        Add to album…
      </Button>
      {onAssign && (
        <Button
          variant="outlined"
          size="small"
          onClick={onAssign}
          sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600 }}
        >
          Assign to person…
        </Button>
      )}
      {onRotate && (
        <Button
          variant="outlined"
          size="small"
          onClick={onRotate}
          startIcon={<RotateRightIcon fontSize="small" />}
          sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600 }}
        >
          Rotate right
        </Button>
      )}
      {onHide && (
        <Button
          variant="outlined"
          size="small"
          onClick={onHide}
          sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600 }}
        >
          {hideLabel}
        </Button>
      )}
      {onRemove && (
        <Button
          color="error"
          size="small"
          onClick={onRemove}
          sx={{ borderRadius: '8px', textTransform: 'none' }}
        >
          Remove from album
        </Button>
      )}
      <Button size="small" onClick={onClear} sx={{ borderRadius: '8px', textTransform: 'none' }}>
        Clear
      </Button>
    </Box>
  )
}
