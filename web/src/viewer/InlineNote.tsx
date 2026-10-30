import CheckIcon from '@mui/icons-material/Check'
import CloseIcon from '@mui/icons-material/Close'
import EditOutlinedIcon from '@mui/icons-material/EditOutlined'
import { Box, IconButton, TextField, Tooltip, Typography } from '@mui/material'
import { useState, type KeyboardEvent } from 'react'
import { NOTE_MAX_LENGTH, noteErrorMessage, useSetImageNote } from '../api/notes'

type Props = {
  imageId: number
  note: string | null | undefined
  /** Editing needs the folder-actions permission; without it the note is read-only. */
  canEdit: boolean
}

/** The photo's note with a pencil: click it and the text becomes editable in place. */
export function InlineNote({ imageId, note, canEdit }: Props) {
  const save = useSetImageNote(imageId)
  const [draft, setDraft] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const editing = draft !== null

  const cancel = () => {
    setDraft(null)
    setError(null)
  }
  const commit = () => {
    if (draft === null || save.isPending) return
    if (draft.trim() === (note ?? '')) return cancel()
    save.mutate(draft, {
      onSuccess: cancel,
      onError: (e) => setError(noteErrorMessage(e)),
    })
  }
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape') {
      // Keep the viewer dialog from closing along with the editor.
      event.stopPropagation()
      cancel()
    } else if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault()
      commit()
    }
  }

  return (
    <div>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
        <Typography variant="caption" sx={{ color: 'grey.400' }}>
          Note
        </Typography>
        {canEdit && !editing && (
          <Tooltip title={note ? 'Edit note' : 'Add note'}>
            <IconButton
              size="small"
              aria-label={note ? 'Edit note' : 'Add note'}
              onClick={() => setDraft(note ?? '')}
              sx={{ color: 'grey.400', p: 0.25 }}
            >
              <EditOutlinedIcon sx={{ fontSize: 16 }} />
            </IconButton>
          </Tooltip>
        )}
      </Box>
      {editing ? (
        <Box sx={{ mt: 0.5 }}>
          <TextField
            autoFocus
            multiline
            minRows={2}
            maxRows={8}
            fullWidth
            size="small"
            value={draft}
            disabled={save.isPending}
            error={error !== null}
            helperText={error ?? undefined}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={onKeyDown}
            slotProps={{
              htmlInput: { maxLength: NOTE_MAX_LENGTH, 'aria-label': 'Note' },
            }}
            sx={{
              '& .MuiInputBase-root': { color: 'grey.100', fontSize: 14 },
              '& .MuiOutlinedInput-notchedOutline': { borderColor: 'rgba(255,255,255,0.2)' },
            }}
          />
          <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 0.5, mt: 0.5 }}>
            <IconButton
              size="small"
              aria-label="Cancel note"
              onClick={cancel}
              disabled={save.isPending}
              sx={{ color: 'grey.400' }}
            >
              <CloseIcon fontSize="small" />
            </IconButton>
            <IconButton
              size="small"
              aria-label="Save note"
              onClick={commit}
              disabled={save.isPending}
              sx={{ color: 'primary.light' }}
            >
              <CheckIcon fontSize="small" />
            </IconButton>
          </Box>
        </Box>
      ) : (
        <Typography
          variant="body2"
          sx={{
            mt: 0.25,
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word',
            color: note ? 'inherit' : 'grey.500',
          }}
        >
          {note || 'No note'}
        </Typography>
      )}
    </div>
  )
}
