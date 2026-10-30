import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
} from '@mui/material'
import { useState } from 'react'
import { NOTE_MAX_LENGTH, noteErrorMessage, useSetFolderNote } from '../api/notes'

type Props = {
  folderId: number
  folderName: string
  note: string | null | undefined
  onClose: () => void
}

export function FolderNoteDialog({ folderId, folderName, note, onClose }: Props) {
  const save = useSetFolderNote(folderId)
  const [draft, setDraft] = useState(note ?? '')
  const [error, setError] = useState<string | null>(null)

  const submit = () =>
    save.mutate(draft, { onSuccess: onClose, onError: (e) => setError(noteErrorMessage(e)) })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{note ? 'Edit note' : 'Add note'}</DialogTitle>
      <DialogContent>
        <TextField
          autoFocus
          multiline
          minRows={3}
          maxRows={10}
          fullWidth
          margin="dense"
          label={`Note for ${folderName}`}
          value={draft}
          error={error !== null}
          helperText={error ?? undefined}
          onChange={(event) => setDraft(event.target.value)}
          slotProps={{ htmlInput: { maxLength: NOTE_MAX_LENGTH } }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={save.isPending}>
          Cancel
        </Button>
        <Button variant="contained" onClick={submit} disabled={save.isPending}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  )
}
