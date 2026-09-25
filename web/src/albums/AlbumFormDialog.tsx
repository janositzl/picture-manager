import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
} from '@mui/material'
import { useState, type FormEvent } from 'react'
import { useCreateAlbum, useUpdateAlbum } from '../api/albums'
import type { AlbumDetail } from '../api/types'
import { albumFormErrors, type AlbumFormErrors } from './errors'

type Props = ({ mode: 'create' } | { mode: 'edit'; album: AlbumDetail }) & {
  onClose: () => void
  onSaved: (album: AlbumDetail) => void
}

export function AlbumFormDialog(props: Props) {
  const initial = props.mode === 'edit' ? props.album : null
  const [name, setName] = useState(initial?.name ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [errors, setErrors] = useState<AlbumFormErrors>({})
  const create = useCreateAlbum()
  const update = useUpdateAlbum(initial?.id ?? 0)
  const busy = create.isPending || update.isPending

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setErrors({})
    const input = {
      name: name.trim(),
      description: description.trim() === '' ? null : description.trim(),
    }
    try {
      const saved =
        props.mode === 'edit' ? await update.mutateAsync(input) : await create.mutateAsync(input)
      props.onSaved(saved)
    } catch (error) {
      setErrors(albumFormErrors(error))
    }
  }

  return (
    <Dialog open onClose={props.onClose} fullWidth maxWidth="xs">
      <form onSubmit={(event) => void submit(event)}>
        <DialogTitle>{props.mode === 'edit' ? 'Edit album' : 'New album'}</DialogTitle>
        <DialogContent>
          {errors.form !== undefined && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {errors.form}
            </Alert>
          )}
          <TextField
            label="Name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            autoFocus
            fullWidth
            margin="dense"
            error={errors.name !== undefined}
            helperText={errors.name}
          />
          <TextField
            label="Description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            multiline
            minRows={2}
            fullWidth
            margin="dense"
            error={errors.description !== undefined}
            helperText={errors.description}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={props.onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={busy || name.trim() === ''}>
            {props.mode === 'edit' ? 'Save' : 'Create'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
