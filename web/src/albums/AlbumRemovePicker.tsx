import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItemButton,
  ListItemText,
} from '@mui/material'
import type { AlbumRef } from '../api/types'

type Props = {
  albums: readonly AlbumRef[]
  disabled?: boolean
  onChoose: (album: AlbumRef) => void
  onClose: () => void
}

/** Lists the albums a photo is in, for choosing which one to remove it from. */
export function AlbumRemovePicker({ albums, disabled = false, onChoose, onClose }: Props) {
  return (
    <Dialog
      open
      onClose={onClose}
      fullWidth
      maxWidth="xs"
      slotProps={{ paper: { className: 'rounded-2xl' } }}
    >
      <DialogTitle sx={{ fontWeight: 600 }}>Remove from album</DialogTitle>
      <DialogContent>
        <List dense aria-label="Albums" sx={{ maxHeight: 320, overflowY: 'auto' }}>
          {albums.map((album) => (
            <ListItemButton
              key={album.id}
              autoFocus={album === albums[0]}
              disabled={disabled}
              onClick={() => onChoose(album)}
              className="transition-colors duration-200 ease-in-out"
              sx={{ borderRadius: '8px', mb: 0.25 }}
            >
              <ListItemText primary={album.name} />
            </ListItemButton>
          ))}
        </List>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}>
        <Button onClick={onClose} sx={{ borderRadius: '8px', textTransform: 'none' }}>
          Cancel
        </Button>
      </DialogActions>
    </Dialog>
  )
}
