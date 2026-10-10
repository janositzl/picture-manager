import AddIcon from '@mui/icons-material/Add'
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItemButton,
  ListItemText,
  TextField,
  Typography,
} from '@mui/material'
import { useState, type ReactNode } from 'react'
import { useCreateAlbum, type AddTarget } from '../api/albums'
import { useAlbums } from '../api/queries'
import { canEdit, isOwner } from './access'
import { albumFormErrors } from './errors'
import { photoCount } from './messages'
import { useAlbumAdder } from './useAlbumAdder'

type Props = {
  target: AddTarget
  /** Selected photos left out because their file is missing; mentioned in the result message. */
  unavailable?: number
  onClose: () => void
  onAdded?: () => void
}

export function AlbumPicker({ target, unavailable = 0, onClose, onAdded }: Props) {
  const albums = useAlbums()
  const { addTo, isAdding } = useAlbumAdder()
  const createAlbum = useCreateAlbum()
  const [filter, setFilter] = useState('')
  const [newName, setNewName] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const busy = isAdding || createAlbum.isPending

  const choose = async (album: { id: number; name: string }) => {
    setError(null)
    const outcome = await addTo(album, target, unavailable)
    if (outcome === 'added') {
      onAdded?.()
      onClose()
    } else {
      setError(outcome === 'gone' ? 'That album no longer exists.' : "Couldn't add photos.")
    }
  }

  const createAndAdd = async () => {
    const name = (newName ?? '').trim()
    if (name === '') return
    setError(null)
    try {
      const album = await createAlbum.mutateAsync({ name, description: null })
      await choose(album)
    } catch (err) {
      const errors = albumFormErrors(err)
      setError(errors.name ?? errors.form ?? "Couldn't create the album.")
    }
  }

  const needle = filter.trim().toLowerCase()
  const editable = (albums.data ?? []).filter(canEdit)
  const visible = editable.filter((album) => album.name.toLowerCase().includes(needle))

  let list: ReactNode
  if (albums.isPending) {
    list = <CircularProgress size={24} sx={{ my: 2 }} />
  } else if (albums.isError) {
    list = (
      <Alert severity="error" sx={{ my: 1 }}>
        Couldn't load albums.
      </Alert>
    )
  } else if (visible.length === 0) {
    list = (
      <Typography color="text.secondary" sx={{ my: 2 }}>
        {editable.length === 0 ? 'No albums yet.' : 'No albums match.'}
      </Typography>
    )
  } else {
    list = (
      <List dense aria-label="Albums" sx={{ maxHeight: 320, overflowY: 'auto' }}>
        {visible.map((album) => (
          <ListItemButton
            key={album.id}
            disabled={busy}
            onClick={() => void choose(album)}
            className="transition-colors duration-200 ease-in-out"
            sx={{ borderRadius: '8px', mb: 0.25 }}
          >
            <ListItemText
              primary={album.name}
              secondary={
                isOwner(album)
                  ? photoCount(album.imageCount)
                  : `Shared by ${album.ownerDisplayName} · ${photoCount(album.imageCount)}`
              }
            />
          </ListItemButton>
        ))}
      </List>
    )
  }

  return (
    <Dialog
      open
      onClose={onClose}
      fullWidth
      maxWidth="xs"
      slotProps={{ paper: { className: 'rounded-2xl' } }}
    >
      <DialogTitle sx={{ fontWeight: 600 }}>Add to album</DialogTitle>
      <DialogContent>
        <TextField
          label="Filter albums"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          autoFocus
          fullWidth
          size="small"
          margin="dense"
          sx={{ '& .MuiOutlinedInput-root': { borderRadius: '8px' } }}
        />
        {error !== null && (
          <Alert severity="error" sx={{ my: 1, borderRadius: '10px' }}>
            {error}
          </Alert>
        )}
        {list}
        {newName === null ? (
          <Button
            startIcon={<AddIcon />}
            onClick={() => setNewName('')}
            sx={{ mt: 1, borderRadius: '8px', textTransform: 'none' }}
          >
            New album
          </Button>
        ) : (
          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault()
              void createAndAdd()
            }}
            sx={{ display: 'flex', gap: 1, mt: 1 }}
          >
            <TextField
              label="New album name"
              value={newName}
              onChange={(event) => setNewName(event.target.value)}
              size="small"
              fullWidth
              autoFocus
              sx={{ '& .MuiOutlinedInput-root': { borderRadius: '8px' } }}
            />
            <Button
              type="submit"
              variant="contained"
              disableElevation
              disabled={busy || newName.trim() === ''}
              sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600, flexShrink: 0 }}
            >
              Create and add
            </Button>
          </Box>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}>
        <Button onClick={onClose} sx={{ borderRadius: '8px', textTransform: 'none' }}>
          Cancel
        </Button>
      </DialogActions>
    </Dialog>
  )
}
